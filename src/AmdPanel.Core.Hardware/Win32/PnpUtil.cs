using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AmdPanel.Core.Hardware.Win32;

/// <summary>
/// Win32 PnpUtil: используется менеджером драйверов для
/// pnputil.exe /add-driver ... /install и /delete-driver ... /uninstall /force.
/// Реализовано как thin wrapper над системной утилитой - это гарантированно
/// работает на любой Windows 10/11 без правки драйверного стека WDK.
/// </summary>
internal static class PnpUtil
{
    /// <summary> Запускает pnputil.exe /add-driver inf /install. </summary>
    public static (int exitCode, string output) AddDriver(string infPath, bool install)
    {
        string args = $"/add-driver \"{infPath}\"";
        if (install) args += " /install";
        return RunPnpUtil(args);
    }

    /// <summary> Запускает pnputil.exe /delete-driver oemXX.inf /uninstall /force. </summary>
    public static (int exitCode, string output) DeleteDriver(string oemInfName, bool uninstall, bool force)
    {
        string args = $"/delete-driver \"{oemInfName}\"";
        if (uninstall) args += " /uninstall";
        if (force)     args += " /force";
        return RunPnpUtil(args);
    }

    /// <summary> Список установленных oem*.inf, относящихся к AMD Display class. </summary>
    public static List<string> EnumerateAmdOemInfs()
    {
        // /enum-drivers выводит таблицу с колонками: Published Name, Original Name, Provider, Class Name, Class GUID, Driver Version, Inf Date, Version ID
        var (code, output) = RunPnpUtil("/enum-drivers");
        if (code != 0) return new List<string>();

        var result = new List<string>();
        var lines = output.Split('\n');
        string? currentPublished = null;
        bool nextLineIsAmd = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("Published Name:", StringComparison.OrdinalIgnoreCase))
            {
                currentPublished = line.Substring("Published Name:".Length).Trim();
            }
            else if (line.StartsWith("Class Name:", StringComparison.OrdinalIgnoreCase) &&
                     line.IndexOf("Display", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                nextLineIsAmd = true;
            }
            else if (line.StartsWith("Provider Name:", StringComparison.OrdinalIgnoreCase) &&
                     nextLineIsAmd &&
                     line.IndexOf("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase) >= 0
                     && currentPublished is not null)
            {
                result.Add(currentPublished);
                currentPublished = null;
                nextLineIsAmd = false;
            }
        }
        return result;
    }

    private static (int exitCode, string output) RunPnpUtil(string args)
    {
        // Путь к pnputil.exe жёстко определён в System32 Windows.
        string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "pnputil.exe");
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.Unicode,
            StandardErrorEncoding = System.Text.Encoding.Unicode,
        };
        // Для доступа к /add-driver и /delete-driver требуется administrator.
        // UAC elevation делается на уровне приложения (app.manifest в AmdPanel.App),
        // здесь процесс уже запущен с правами админа.
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return (-1, "Process.Start returned null");
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, stdout + Environment.NewLine + stderr);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
