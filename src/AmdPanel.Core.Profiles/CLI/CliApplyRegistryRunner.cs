using System.Text.Json;
using AmdPanel.Core.Profiles.Models;
using AmdPanel.Core.Profiles.Services;
using AmdPanel.Core.Profiles.Storage;

namespace AmdPanel.Core.Profiles.CLI;

/// <summary>
/// Elevated CLI runner для --apply-registry-only: читает список RegistryMutation
/// из временного JSON и применяет напрямую в HKLM. Запускается через
/// UacElevationService.LaunchElevated когда UI-процесс не имеет админ-прав.
/// </summary>
/// <remarks>
/// Контракт: файл JSON содержит массив RegistryMutation, сериализованный через
/// JsonSerializer.Serialize&lt;List&lt;RegistryMutation&gt;&gt;. После применения
/// временный файл удаляется.
/// </remarks>
public static class CliApplyRegistryRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: AmdPanel.exe --apply-registry-only <path-to-task.json>");
            return 2;
        }

        string taskPath = args[1];
        if (!File.Exists(taskPath))
        {
            Console.Error.WriteLine($"Registry task file not found: {taskPath}");
            return 3;
        }

        if (!RegistryApplier.HasAdminRights)
        {
            Console.Error.WriteLine("This command requires administrator privileges.");
            return 5;
        }

        List<RegistryMutation> mutations;
        try
        {
            string json = await File.ReadAllTextAsync(taskPath);
            mutations = JsonSerializer.Deserialize<List<RegistryMutation>>(json)
                        ?? new List<RegistryMutation>();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to deserialize registry task: {ex.Message}");
            return 4;
        }

        if (mutations.Count == 0)
        {
            Console.WriteLine("No registry mutations to apply.");
            return 0;
        }

        var applier = new RegistryApplier();
        try
        {
            applier.ApplyDirectly(mutations);
            Console.WriteLine($"Applied {mutations.Count} registry mutations to HKLM.");
            // Удаляем временный файл задачи
            try { File.Delete(taskPath); } catch { /* не критично */ }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to apply registry mutations: {ex.Message}");
            return 6;
        }
    }
}
