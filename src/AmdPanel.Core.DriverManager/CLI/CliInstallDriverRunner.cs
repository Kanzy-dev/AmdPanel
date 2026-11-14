using AmdPanel.Core.DriverManager.Services;
using AmdPanel.Core.Hardware;
using AmdPanel.Core.DriverManager.Models;

namespace AmdPanel.Core.DriverManager.CLI;

/// <summary>
/// Elevated CLI runner для --install-driver: вызывается через
/// UacElevationService.LaunchElevated когда UI-процесс asInvoker.
/// Запускается в консольном режиме, выполняет:
///   1) Extract (если .exe) или копирование (если .inf)
///   2) Опциональный Clean Install (если --clean true)
///   3) pnputil /add-driver /install
///   4) Проверка amdadlx64.dll
/// Лог пишется в stdout/stderr для последующего парсинга UI если нужно.
/// </summary>
public static class CliInstallDriverRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: AmdPanel.exe --install-driver <path.exe|path.inf> [--clean true|false]");
            return 2;
        }

        string sourcePath = args[1];
        bool cleanInstall = false;
        if (args.Length >= 4 && string.Equals(args[2], "--clean", StringComparison.OrdinalIgnoreCase))
            bool.TryParse(args[3], out cleanInstall);

        if (!File.Exists(sourcePath))
        {
            Console.Error.WriteLine($"Source file not found: {sourcePath}");
            return 3;
        }

        if (!RegistryApplierHasAdmin())
        {
            Console.Error.WriteLine("This command must be run as administrator.");
            return 5;
        }

        // ADLX инициализируем только для чтения snapshot (нужен для ArchitectureDetector)
        using var ctx = new AdlxContext();
        var hub = new UiHardwareHub(ctx);
        await hub.ScanAsync();

        var service = new DriverManagerService(hub.Snapshot);
        service.ProgressChanged += (_, p) =>
        {
            string prefix = p.Stage switch
            {
                DriverInstallStage.Downloading          => "[1/4 Download]",
                DriverInstallStage.Extracting7z         => "[2/4 Extract]",
                DriverInstallStage.DeletingOldDriver    => "[3/4 Clean]",
                DriverInstallStage.InstallingInf        => "[4/4 Install]",
                DriverInstallStage.Completed            => "[Done]",
                DriverInstallStage.Failed               => "[FAIL]",
                _ => "[Step]",
            };
            Console.WriteLine($"{prefix} {p.Percent}% — {p.Message}");
        };
        service.ScreenWillFlicker += (_, _) =>
            Console.WriteLine("WARNING: screen will flicker during driver removal.");
        service.RebootRequired += (_, _) =>
            Console.WriteLine("INFO: reboot is required to complete clean install.");
        service.BackupCreated += (_, e) =>
            Console.WriteLine($"Backup created: {e.ArchivePath} ({e.SizeBytes / 1024} KB)");

        try
        {
            await service.InstallFromFileAsync(sourcePath, cleanInstall);
            Console.WriteLine("Driver installation completed successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Installation failed: {ex.Message}");
            return 6;
        }
    }

    private static bool RegistryApplierHasAdmin()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }
}
