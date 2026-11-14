using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using AmdPanel.Core.DriverManager.Exceptions;

namespace AmdPanel.Core.DriverManager.Services;

/// <summary>
/// Тихая установка .inf драйвера AMD через pnputil.exe /add-driver ... /install,
/// с обязательной проверкой сохранения amdadlx64.dll в System32 (Requirement 3).
/// </summary>
/// <remarks>
/// <para>
/// После <c>pnputil.exe /add-driver *.inf /install</c> Windows копирует файлы
/// драйвера в <c>DriverStore\FileRepository\&lt;package&gt;\</c> и регистрирует
/// службу. Однако на некоторых системах с настройками безопасности или при
/// установке Legacy-драйвера Polaris файл <c>amdadlx64.dll</c> может не попасть
/// в System32. Этот класс проверяет и при необходимости копирует его вручную.
/// </para>
/// </remarks>
public sealed class DriverPackageInstaller
{
    private const string System32AdlxDll = @"C:\Windows\System32\amdadlx64.dll";
    private const string System32AdlxDll32 = @"C:\Windows\SysWOW64\amdadlx32.dll";
    private const string DriverStoreRoot = @"C:\Windows\System32\DriverStore\FileRepository";

    /// <summary>
    /// Устанавливает .inf файл драйвера через pnputil.exe /add-driver /install.
    /// </summary>
    /// <param name="infPath"> Полный путь к распакованному .inf файлу. </param>
    /// <param name="logLine"> Action для логирования в UI. </param>
    /// <returns> Имя опубликованного oemXX.inf. </returns>
    public async Task<string> InstallInfAsync(string infPath, Action<string>? logLine = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(infPath))
            throw new DriverManagerException($"INF file not found: {infPath}");

        logLine?.Invoke($"Running pnputil.exe /add-driver \"{infPath}\" /install");
        var (exitCode, output) = await RunPnpUtilAsync($"/add-driver \"{infPath}\" /install");
        logLine?.Invoke($"pnputil exit={exitCode}");
        logLine?.Invoke(output);

        if (exitCode != 0)
            throw new PnpUtilFailedException(exitCode, output,
                $"pnputil.exe /add-driver failed with exit code {exitCode}.");

        // Извлекаем published name oemXX.inf из вывода pnputil
        string? publishedName = ParsePublishedInfName(output);
        if (!string.IsNullOrEmpty(publishedName))
            logLine?.Invoke($"Published INF: {publishedName}");

        // Проверка amdadlx64.dll - ключевая защита (Requirement 3)
        logLine?.Invoke($"Verifying {System32AdlxDll}");
        bool adlxPresent = File.Exists(System32AdlxDll);
        if (!adlxPresent)
        {
            logLine?.Invoke("WARNING: amdadlx64.dll missing in System32 - attempting to copy from DriverStore");
            EnsureAdlxLibraryCopied(infPath, logLine);
            adlxPresent = File.Exists(System32AdlxDll);
            if (!adlxPresent)
                throw new AdlxLibraryMissingException();
        }
        logLine?.Invoke($"OK: amdadlx64.dll present in System32 ({new FileInfo(System32AdlxDll).Length} bytes)");

        return publishedName ?? Path.GetFileNameWithoutExtension(infPath);
    }

    /// <summary>
    /// Удаляет ранее установленный драйвер через pnputil /delete-driver /uninstall /force.
    /// </summary>
    public async Task DeleteDriverAsync(string? publishedInfName, Action<string>? logLine = null,
        CancellationToken ct = default)
    {
        var oems = string.IsNullOrEmpty(publishedInfName)
            ? EnumerateAmdOemInfs()
            : new List<string> { publishedInfName };

        foreach (var oem in oems)
        {
            logLine?.Invoke($"Deleting old driver {oem}");
            var (code, msg) = await RunPnpUtilAsync($"/delete-driver \"{oem}\" /uninstall /force");
            logLine?.Invoke($"pnputil /delete-driver exit={code}");
            if (code != 0)
                logLine?.Invoke($"WARNING: delete-driver {oem} exit={code}: {msg}");
            else
                logLine?.Invoke($"Deleted {oem}");
        }
    }

    /// <summary> Возвращает список oemXX.inf для AMD Display class. </summary>
    public List<string> EnumerateAmdOemInfs()
    {
        var result = new List<string>();
        var (code, output) = RunPnpUtilSync("/enum-drivers");
        if (code != 0) return result;
        string? currentPublished = null;
        bool nextLineIsAmdDisplay = false;
        foreach (var rawLine in output.Split('\n', '\r'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("Published Name:", StringComparison.OrdinalIgnoreCase))
            {
                currentPublished = line.Substring("Published Name:".Length).Trim();
                nextLineIsAmdDisplay = false;
            }
            else if (line.StartsWith("Class Name:", StringComparison.OrdinalIgnoreCase) &&
                     line.IndexOf("Display", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                nextLineIsAmdDisplay = true;
            }
            else if (line.StartsWith("Provider Name:", StringComparison.OrdinalIgnoreCase) &&
                     nextLineIsAmdDisplay &&
                     line.IndexOf("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase) >= 0 &&
                     currentPublished is not null)
            {
                result.Add(currentPublished);
                currentPublished = null;
                nextLineIsAmdDisplay = false;
            }
        }
        return result;
    }

    /// <summary>
    /// Гарантия наличия amdadlx64.dll в System32 после установки.
    /// </summary>
    private void EnsureAdlxLibraryCopied(string infPath, Action<string>? logLine)
    {
        string infDir = Path.GetDirectoryName(infPath) ?? string.Empty;
        string[] searchDirs = { infDir, Path.Combine(infDir, ".."), DriverStoreRoot };
        foreach (var dir in searchDirs)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
            try
            {
                var found = Directory.GetFiles(dir, "amdadlx64.dll", SearchOption.AllDirectories);
                if (found.Length > 0)
                {
                    string src = found[0];
                    logLine?.Invoke($"Copying {src} → {System32AdlxDll}");
                    File.Copy(src, System32AdlxDll, overwrite: true);
                    var found32 = Directory.GetFiles(dir, "amdadlx32.dll", SearchOption.AllDirectories);
                    if (found32.Length > 0)
                    {
                        File.Copy(found32[0], System32AdlxDll32, overwrite: true);
                        logLine?.Invoke($"Copied 32-bit {System32AdlxDll32}");
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                logLine?.Invoke($"Search in {dir} failed: {ex.Message}");
            }
        }
        logLine?.Invoke("ERROR: amdadlx64.dll not found anywhere in driver package or DriverStore.");
    }

    /// <summary> Парсит "Published Name: oemXX.inf" из вывода pnputil. </summary>
    private static string? ParsePublishedInfName(string pnputilOutput)
    {
        var match = Regex.Match(pnputilOutput, @"oem\d+\.inf", RegexOptions.IgnoreCase);
        return match.Success ? match.Value : null;
    }

    private static async Task<(int exitCode, string output)> RunPnpUtilAsync(string args)
    {
        string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "pnputil.exe");
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.Unicode,
            StandardErrorEncoding = Encoding.Unicode,
        };
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return (-1, "Process.Start returned null");
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            return (p.ExitCode, await stdoutTask + Environment.NewLine + await stderrTask);
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }

    private static (int exitCode, string output) RunPnpUtilSync(string args)
    {
        string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "pnputil.exe");
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.Unicode,
            StandardErrorEncoding = Encoding.Unicode,
        };
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return (-1, "Process.Start returned null");
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, stdout + Environment.NewLine + stderr);
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }
}
