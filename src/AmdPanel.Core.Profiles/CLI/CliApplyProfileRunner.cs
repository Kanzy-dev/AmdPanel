using AmdPanel.Core.Hardware;
using AmdPanel.Core.Profiles.Exceptions;
using AmdPanel.Core.Profiles.Services;
using AmdPanel.Core.Profiles.Storage;

namespace AmdPanel.Core.Profiles.CLI;

/// <summary>
/// CLI runner для --apply-profile: загружает JSON-профиль по пути,
/// инициализирует ADLX, применяет все настройки и выходит.
/// Предназначен для планировщика Windows (запуск при старте ОС).
/// </summary>
public static class CliApplyProfileRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: AmdPanel.exe --apply-profile <path-to-profile.json>");
            return 2;
        }

        string profilePath = args[1];
        if (!File.Exists(profilePath))
        {
            Console.Error.WriteLine($"Profile file not found: {profilePath}");
            return 3;
        }

        GlobalProfile profile;
        try
        {
            string json = File.ReadAllText(profilePath);
            profile = System.Text.Json.JsonSerializer.Deserialize<GlobalProfile>(json)
                      ?? throw new ProfileCorruptedException(profilePath, new InvalidOperationException("null"));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to deserialize profile: {ex.Message}");
            return 4;
        }

        using var ctx = new AdlxContext();
        var hub = new UiHardwareHub(ctx);
        await hub.ScanAsync();

        var repo = new JsonProfileRepository();
        var registry = new RegistryApplier();
        var service = new ProfileService(hub, repo, registry);

        // Non-interactive: watchdog отключён, при невозможности подтвердить режим
        // дисплея - он автоматически откатится.
        var report = await service.ApplyAsync(profile, interactiveWatchdog: false);

        Console.WriteLine($"Profile '{profile.ProfileKey}' applied");
        Console.WriteLine($"  3D:       {report.ThreeDSettingsApplied}");
        Console.WriteLine($"  Display:  {report.DisplayApplied}");
        Console.WriteLine($"  Color:    {report.ColorApplied}");
        Console.WriteLine($"  FreeSync: {report.FreeSyncApplied}");
        Console.WriteLine($"  Scaling:  {report.ScalingApplied}");
        Console.WriteLine($"  Registry: {report.RegistryApplied}");
        if (report.Errors.Count > 0)
        {
            Console.WriteLine("Errors:");
            foreach (var err in report.Errors) Console.WriteLine($"  {err}");
        }
        if (report.Warnings.Count > 0)
        {
            Console.WriteLine("Warnings:");
            foreach (var w in report.Warnings) Console.WriteLine($"  {w}");
        }
        return report.OverallSuccess ? 0 : 1;
    }
}
