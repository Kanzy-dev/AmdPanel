using System.Diagnostics;
using AmdPanel.Core.DriverManager.Exceptions;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Archives;

namespace AmdPanel.Core.DriverManager.Services;

/// <summary>
/// Распаковка официального .exe дистрибутива AMD в указанный каталог.
/// </summary>
/// <remarks>
/// <para>
/// AMD распространяет драйверы как самораспаковывающиеся 7z-архивы (SFX).
/// Стандартный <c>System.IO.Compression.ZipFile</c> падает с
/// <c>InvalidDataException</c>, потому что это не ZIP, а 7z-LZMA.
/// </para>
/// <para>
/// Стратегия распаковки:
/// </para>
/// <list type="number">
///   <item><b>Прямой .inf</b>: если пользователь выбрал уже распакованный
///   .inf файл (не .exe), возвращаем путь без распаковки.</item>
///   <item><b>7z.exe в системе</b>: поиск в стандартных путях (Program Files,
///   Program Files (x86)). Если найден - запускаем консольную команду
///   <c>7z.exe x -y -o&lt;dest&gt; &lt;source.exe&gt;</c>.</item>
///   <item><b>SharpCompress fallback</b>: если 7z.exe не установлен,
///   парсим SFX-stream вручную через SharpCompress (ищем signature 7z в
///   первых ~1 MB .exe и парсим SevenZipArchive).</item>
/// </list>
/// </remarks>
public sealed class SevenZipExtractor
{
    /// <summary> Стандартные пути поиска 7z.exe в системе. </summary>
    private static readonly string[] s_sevenZipSearchPaths =
    {
        @"C:\Program Files\7-Zip\7z.exe",
        @"C:\Program Files (x86)\7-Zip\7z.exe",
        @"C:\Program Files\7-Zip\7za.exe",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "7z", "7z.exe"),
    };

    /// <summary> Каталог по умолчанию для распаковки: %TEMP%\AMDCleanDriver. </summary>
    public static string DefaultExtractionDirectory =>
        Path.Combine(Path.GetTempPath(), "AMDCleanDriver");

    /// <summary>
    /// Главный метод: определяет тип файла и распаковывает соответствующим способом.
    /// </summary>
    /// <param name="sourcePath"> Путь к .exe (AMD SFX) или к уже готовому .inf. </param>
    /// <param name="destinationDir"> Каталог распаковки (создаётся если нет). </param>
    /// <param name="progress"> Action(percent, message). </param>
    /// <param name="ct"> CancellationToken. </param>
    /// <returns> Путь к найденному .inf файлу драйвера AMD. </returns>
    public async Task<string> ExtractAsync(string sourcePath, string destinationDir,
        Action<int, string>? progress = null, CancellationToken ct = default)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Source file not found: {sourcePath}");

        string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        Directory.CreateDirectory(destinationDir);
        progress?.Invoke(0, $"Source: {Path.GetFileName(sourcePath)}");

        if (ext == ".inf")
        {
            // Уже распакованный .inf — копируем в destination и возвращаем путь
            string destInf = Path.Combine(destinationDir, Path.GetFileName(sourcePath));
            File.Copy(sourcePath, destInf, overwrite: true);
            progress?.Invoke(100, $"Copied .inf: {destInf}");
            return destInf;
        }

        if (ext != ".exe")
            throw new DriverManagerException(
                $"Unsupported file extension: '{ext}'. Expected .exe (AMD SFX) or .inf.");

        // Стратегия 1: попытка через 7z.exe если установлен
        string? sevenZipPath = FindSevenZipExecutable();
        if (sevenZipPath is not null)
        {
            progress?.Invoke(10, $"Using 7-Zip: {sevenZipPath}");
            await ExtractWithSevenZipExeAsync(sevenZipPath, sourcePath, destinationDir, progress, ct);
        }
        else
        {
            // Стратегия 2: SharpCompress парсинг SFX-stream
            progress?.Invoke(10, "7z.exe not found - using SharpCompress fallback");
            await ExtractWithSharpCompressAsync(sourcePath, destinationDir, progress, ct);
        }

        // Ищем главный .inf драйвера в распакованном дереве
        string infPath = LocateMainDriverInf(destinationDir);
        progress?.Invoke(100, $"Located driver .inf: {infPath}");
        return infPath;
    }

    /// <summary> Поиск 7z.exe в стандартных системных путях. </summary>
    public static string? FindSevenZipExecutable()
    {
        foreach (var p in s_sevenZipSearchPaths)
            if (File.Exists(p)) return p;
        // Поиск через PATH (Where.exe)
        try
        {
            using var proc = Process.Start(new ProcessStartInfo("where.exe", "7z.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            });
            if (proc is not null)
            {
                string output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(2000);
                if (proc.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    var first = output.Split('\n', '\r')[0].Trim();
                    if (File.Exists(first)) return first;
                }
            }
        }
        catch { /* where.exe not available */ }
        return null;
    }

    /// <summary> Запускает 7z.exe x -y -o&lt;dest&gt; &lt;source&gt;. </summary>
    private async Task ExtractWithSevenZipExeAsync(string sevenZipPath, string sourcePath,
        string destinationDir, Action<int, string>? progress, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(sevenZipPath,
            $"x -y -o\"{destinationDir}\" \"{sourcePath}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi) ?? throw new DriverManagerException("Failed to start 7z.exe");
        // Async чтение stdout/stderr чтобы не дедлочить pipe buffer
        var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        string stdout = await stdoutTask;
        string stderr = await stderrTask;
        if (p.ExitCode != 0)
            throw new DriverManagerException(
                $"7z.exe exited with code {p.ExitCode}.{Environment.NewLine}stdout: {stdout}{Environment.NewLine}stderr: {stderr}");
        progress?.Invoke(80, "7z.exe extraction complete");
    }

    /// <summary>
    /// SharpCompress fallback: пытается открыть .exe как SevenZipArchive.
    /// SharpCompress умеет читать SFX-stream с self-extracting header,
    /// пропуская PE-обёртку.
    /// </summary>
    private async Task ExtractWithSharpCompressAsync(string sourcePath, string destinationDir,
        Action<int, string>? progress, CancellationToken ct)
    {
        try
        {
            await using var fs = File.OpenRead(sourcePath);
            using var archive = SevenZipArchive.OpenArchive(fs);
            var entries = archive.Entries.ToList();
            int total = entries.Count;
            int done = 0;
            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                if (!entry.IsDirectory && entry.Key is not null)
                {
                    string destFile = Path.Combine(destinationDir, entry.Key);
                    string? destDir = Path.GetDirectoryName(destFile);
                    if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                    entry.WriteToFile(destFile);
                }
                done++;
                progress?.Invoke(10 + (done * 70 / total), $"Extracted {done}/{total}");
                if (done % 50 == 0)
                    await Task.Yield();
            }
            progress?.Invoke(80, "SharpCompress extraction complete");
        }
        catch (Exception ex) when (ex is not DriverManagerException)
        {
            throw new DriverManagerException(
                $"SharpCompress failed to extract .exe (it may not be a valid AMD 7z SFX). " +
                $"Install 7-Zip from https://www.7-zip.org for more reliable extraction. " +
                $"Original error: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Ищет главный .inf файл драйвера AMD в распакованном дереве.
    /// Стандартное расположение: Packages\Drivers\Display\WT6A_INF\*.inf
    /// </summary>
    public static string LocateMainDriverInf(string rootDir)
    {
        // 1. Стандартный путь AMD
        string standardInfDir = Path.Combine(rootDir, "Packages", "Drivers", "Display", "WT6A_INF");
        if (Directory.Exists(standardInfDir))
        {
            var inf = Directory.GetFiles(standardInfDir, "*.inf").FirstOrDefault();
            if (inf is not null) return inf;
        }
        // 2. Поиск по всему дереву - любой .inf с CATALYST/AMD в [Manufacturer] секции
        var allInfs = Directory.GetFiles(rootDir, "*.inf", SearchOption.AllDirectories);
        foreach (var inf in allInfs)
        {
            try
            {
                string text = File.ReadAllText(inf);
                if (text.IndexOf("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 && text.IndexOf("VEN_1002", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return inf;
                }
            }
            catch { /* try next */ }
        }
        // 3. Fallback - первый попавшийся .inf
        if (allInfs.Length > 0) return allInfs[0];
        throw new InfFileNotFoundException(rootDir);
    }
}
