using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace AmdPanel.App.Services;

/// <summary>
/// v1.1.0: Сканирование установленных игр и программ для вкладки
/// "Программные настройки" в стиле NVIDIA Control Panel.
/// </summary>
/// <remarks>
/// Источники программ:
/// <list type="bullet">
///   <item>Steam library (<c>libraryfolders.vdf</c> + <c>appmanifest_*.acf</c>)</item>
///   <item>Windows Uninstall registry (<c>HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall</c> + WOW6432Node)</item>
///   <item>Текущие оконные процессы (через <c>Process.GetProcesses()</c>)</item>
/// </list>
/// Для каждого .exe извлекается иконка через <c>System.Drawing.Icon.ExtractAssociatedIcon</c>
/// и конвертируется в <c>ImageSource</c> через <c>Imaging.CreateBitmapSourceFromHIcon</c>.
/// </remarks>
public sealed class InstalledAppInfo
{
    public string DisplayName { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public ImageSource? Icon { get; init; }
    public string Source { get; init; } = string.Empty;  // "Steam" / "Registry" / "Running"
}

public static class InstalledAppsScanner
{
    /// <summary>
    /// Сканирует все источники и возвращает список установленных программ
    /// с иконками. Дубликаты по ExecutablePath удаляются.
    /// </summary>
    public static List<InstalledAppInfo> ScanAll()
    {
        var result = new Dictionary<string, InstalledAppInfo>(StringComparer.OrdinalIgnoreCase);

        // 1. Steam library
        try
        {
            foreach (var app in ScanSteamLibrary())
            {
                if (!string.IsNullOrEmpty(app.ExecutablePath) && !result.ContainsKey(app.ExecutablePath))
                    result[app.ExecutablePath] = app;
            }
        }
        catch (Exception ex) { Trace.WriteLine($"[InstalledAppsScanner] Steam scan failed: {ex.Message}"); }

        // 2. Uninstall registry
        try
        {
            foreach (var app in ScanUninstallRegistry())
            {
                if (!string.IsNullOrEmpty(app.ExecutablePath) && !result.ContainsKey(app.ExecutablePath))
                    result[app.ExecutablePath] = app;
            }
        }
        catch (Exception ex) { Trace.WriteLine($"[InstalledAppsScanner] Registry scan failed: {ex.Message}"); }

        // 3. Running processes with windows
        try
        {
            foreach (var app in ScanRunningProcesses())
            {
                var key = app.ExecutablePath ?? app.ProcessName;
                if (!string.IsNullOrEmpty(key) && !result.ContainsKey(key))
                    result[key] = app;
            }
        }
        catch (Exception ex) { Trace.WriteLine($"[InstalledAppsScanner] Process scan failed: {ex.Message}"); }

        return result.Values.ToList();
    }

    // =================================================================
    //  Steam library scanner
    // =================================================================

    /// <summary>
    /// Сканирует Steam library: находит libraryfolders.vdf, парсит пути к
    /// библиотекам, и в каждом steamapps/appmanifest_*.acf читает имя игры.
    /// </summary>
    private static IEnumerable<InstalledAppInfo> ScanSteamLibrary()
    {
        // Steam установлен в HKCU\Software\Valve\Steam\SteamPath
        string? steamPath = Registry.CurrentUser
            .OpenSubKey(@"Software\Valve\Steam")?
            .GetValue("SteamPath") as string;
        if (string.IsNullOrEmpty(steamPath))
        {
            // Fallback: стандартные пути
            steamPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        }
        if (!Directory.Exists(steamPath)) yield break;

        // libraryfolders.vdf: список путей к библиотекам Steam
        string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath)) yield break;

        var libraryPaths = new List<string> { Path.Combine(steamPath, "steamapps") };
        try
        {
            string vdfContent = File.ReadAllText(vdfPath);
            // Парсим строки "path"		"D:\\SteamLibrary"
            var matches = Regex.Matches(vdfContent, @"""path""\s+""([^""]+)""");
            foreach (Match m in matches)
            {
                string p = m.Groups[1].Value.Replace("\\\\", "\\");
                if (Directory.Exists(p)) libraryPaths.Add(Path.Combine(p, "steamapps"));
            }
        }
        catch { /* ignore parse errors */ }

        // В каждой библиотеке ищем appmanifest_*.acf
        foreach (string libPath in libraryPaths)
        {
            if (!Directory.Exists(libPath)) continue;
            string[] manifests;
            try { manifests = Directory.GetFiles(libPath, "appmanifest_*.acf"); }
            catch { continue; }

            foreach (string manifest in manifests)
            {
                string? name = null;
                string? installDir = null;
                try
                {
                    string content = File.ReadAllText(manifest);
                    var nameMatch = Regex.Match(content, @"""name""\s+""([^""]+)""");
                    if (nameMatch.Success) name = nameMatch.Groups[1].Value;
                    var installMatch = Regex.Match(content, @"""installdir""\s+""([^""]+)""");
                    if (installMatch.Success) installDir = installMatch.Groups[1].Value;
                }
                catch { continue; }

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(installDir)) continue;

                // Ищем .exe в common/{installDir}/
                string gameDir = Path.Combine(libPath, "common", installDir);
                if (!Directory.Exists(gameDir)) continue;

                string? exePath = null;
                try
                {
                    // Берём первый .exe в папке игры (не launcher.exe, а основной)
                    var exes = Directory.GetFiles(gameDir, "*.exe", SearchOption.TopDirectoryOnly);
                    exePath = exes.FirstOrDefault(e =>
                        !e.EndsWith("launcher.exe", StringComparison.OrdinalIgnoreCase) &&
                        !e.EndsWith("steam_api64.dll", StringComparison.OrdinalIgnoreCase));
                    if (exePath is null && exes.Length > 0) exePath = exes[0];
                }
                catch { continue; }

                if (exePath is null) continue;

                yield return new InstalledAppInfo
                {
                    DisplayName = name,
                    ExecutablePath = exePath,
                    ProcessName = Path.GetFileNameWithoutExtension(exePath),
                    Icon = ExtractIcon(exePath),
                    Source = "Steam"
                };
            }
        }
    }

    // =================================================================
    //  Uninstall registry scanner
    // =================================================================

    /// <summary>
    /// Сканирует HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall
    /// (и WOW6432Node) для установленных программ с DisplayIcon.
    /// </summary>
    private static IEnumerable<InstalledAppInfo> ScanUninstallRegistry()
    {
        string[] roots =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        foreach (string root in roots)
        {
            using var key = Registry.LocalMachine.OpenSubKey(root);
            if (key is null) continue;

            foreach (string subKeyName in key.GetSubKeyNames())
            {
                using var subKey = key.OpenSubKey(subKeyName);
                if (subKey is null) continue;

                string? displayName = subKey.GetValue("DisplayName") as string;
                string? displayIcon = subKey.GetValue("DisplayIcon") as string;

                if (string.IsNullOrEmpty(displayName) || string.IsNullOrEmpty(displayIcon))
                    continue;

                // DisplayIcon может быть "C:\path\app.exe,0" — обрезаем запятую
                string exePath = displayIcon.Split(',')[0].Trim().Trim('"');
                if (!File.Exists(exePath)) continue;

                // Фильтр: только .exe, исключаем системные утилиты
                if (exePath.IndexOf("system32", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                yield return new InstalledAppInfo
                {
                    DisplayName = displayName,
                    ExecutablePath = exePath,
                    ProcessName = Path.GetFileNameWithoutExtension(exePath),
                    Icon = ExtractIcon(exePath),
                    Source = "Registry"
                };
            }
        }
    }

    // =================================================================
    //  Running processes scanner
    // =================================================================

    /// <summary>
    /// Сканирует текущие процессы с окнами (отличаются от фоновых сервисов).
    /// Использует Win32 EnumWindows для фильтрации процессов с видимыми окнами.
    /// </summary>
    private static IEnumerable<InstalledAppInfo> ScanRunningProcesses()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.MainWindowHandle == IntPtr.Zero) continue;  // нет окна
                string? exePath = null;
                try { exePath = proc.MainModule?.FileName; }
                catch { /* access denied для системных процессов */ }

                string procName = proc.ProcessName;
                if (string.IsNullOrEmpty(procName)) continue;
                if (seen.Contains(procName.ToLowerInvariant())) continue;
                seen.Add(procName.ToLowerInvariant());

                // Фильтр: исключаем системные процессы
                if (procName.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
                    procName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
                    continue;

                yield return new InstalledAppInfo
                {
                    DisplayName = procName,
                    ExecutablePath = exePath ?? procName,
                    ProcessName = procName,
                    Icon = exePath is not null ? ExtractIcon(exePath) : null,
                    Source = "Running"
                };
            }
            catch { /* ignore individual process errors */ }
        }
    }

    // =================================================================
    //  Icon extraction
    // =================================================================

    /// <summary>
    /// Извлекает иконку из .exe через System.Drawing.Icon.ExtractAssociatedIcon
    /// и конвертирует в ImageSource для WPF binding.
    /// </summary>
    public static ImageSource? ExtractIcon(string exePath)
    {
        try
        {
            if (!File.Exists(exePath)) return null;
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon is null) return null;
            var bmp = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();  // делаем cross-thread safe
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
