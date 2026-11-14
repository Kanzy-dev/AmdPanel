using System.Security.Principal;
using AmdPanel.Core.Profiles.Exceptions;
using AmdPanel.Core.Profiles.Models;
using AmdPanel.Core.Profiles.Storage;
using Microsoft.Win32;

namespace AmdPanel.Core.Profiles.Services;

/// <summary>
/// Применение UMD-настроек драйвера AMD в HKLM\SYSTEM\...\Control\Class\{GUID}\XXXX\UMD.
/// </summary>
/// <remarks>
/// Запись в HKLM требует прав администратора. Если текущий процесс работает
/// как asInvoker (типичный сценарий UI), RegistryApplier НЕ пытается писать
/// напрямую - вместо этого он сериализует список мутаций во временный JSON
/// и вызывает UacElevationService.LaunchElevated("--apply-registry-only <path>"),
/// что запускает копию AmdPanel.exe с правами администратора и выходит.
/// Это избегает перезапуска всего UI-окна (Requirement 4).
/// </remarks>
public sealed class RegistryApplier
{
    private readonly string _executablePath;

    public RegistryApplier(string? executablePath = null)
    {
        _executablePath = executablePath ?? Environment.ProcessPath ?? string.Empty;
    }

    /// <summary> true, если можно писать в HKLM из текущего процесса. </summary>
    public static bool HasAdminRights
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity)
                    .IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// Применяет список мутаций реестра. Если есть права админа - пишет напрямую;
    /// если нет - сериализует в .json, запускает elevated runner, возвращает true
    /// в случае успешного запуска (UAC подтверждён).
    /// </summary>
    /// <returns> true, если мутации применены напрямую или elevated runner запущен. </returns>
    public bool ApplyMutations(IReadOnlyList<RegistryMutation> mutations)
    {
        if (mutations.Count == 0) return true;

        if (HasAdminRights)
        {
            ApplyDirectly(mutations);
            return true;
        }

        // Нет прав - сериализуем во временный файл и вызываем elevated runner
        string tmpJsonPath = Path.Combine(ProfilePaths.RegistryApplyQueueDirectory,
            $"registry-task-{Guid.NewGuid():N}.json");
        ProfilePaths.EnsureDirectories();
        File.WriteAllText(tmpJsonPath, JsonSerializer.Serialize(mutations.ToList(),
            new JsonSerializerOptions { WriteIndented = true }));

        return LaunchElevatedRunner($"--apply-registry-only \"{tmpJsonPath}\"");
    }

    /// <summary> Применяет мутации напрямую в HKLM. Только если процесс elevated. </summary>
    public void ApplyDirectly(IReadOnlyList<RegistryMutation> mutations)
    {
        foreach (var m in mutations)
            ApplyOne(m);
    }

    /// <summary> Запускает AmdPanel.exe с --apply-registry-only через ShellExecute "runas". </summary>
    public bool LaunchElevatedRunner(string arguments)
    {
        if (string.IsNullOrEmpty(_executablePath))
            throw new ProfileRegistryApplyException("ExecutablePath не определён");
        return LaunchRunas(_executablePath, arguments);
    }

    private static void ApplyOne(RegistryMutation m)
    {
        if (string.IsNullOrEmpty(m.KeyPath) || string.IsNullOrEmpty(m.ValueName))
            throw new ProfileRegistryApplyException($"Некорректная мутация: KeyPath/ValueName пустые");

        // Путь начинается с "SYSTEM\..." - открываем относительно HKLM
        using var key = Registry.LocalMachine.CreateSubKey(m.KeyPath,
            RegistryKeyPermissionCheck.ReadWriteSubTree);
        if (key is null)
            throw new ProfileRegistryApplyException($"Не удалось создать/открыть ключ HKLM\\{m.KeyPath}");

        switch (m.ValueKind)
        {
            case "DWord":
                key.SetValue(m.ValueName, m.DwordValue ?? 0, RegistryValueKind.DWord);
                break;
            case "QWord":
                key.SetValue(m.ValueName, m.DwordValue ?? 0, RegistryValueKind.QWord);
                break;
            case "Binary":
                var bytes = Convert.FromHexString(m.StringValue);
                key.SetValue(m.ValueName, bytes, RegistryValueKind.Binary);
                break;
            case "MultiString":
                var multi = m.StringValue.Split('|');
                key.SetValue(m.ValueName, multi, RegistryValueKind.MultiString);
                break;
            case "ExpandString":
                key.SetValue(m.ValueName, m.StringValue, RegistryValueKind.ExpandString);
                break;
            default: // "String"
                key.SetValue(m.ValueName, m.StringValue, RegistryValueKind.String);
                break;
        }
    }

    /// <summary> Запускает процесс с глаголом "runas" (UAC prompt). </summary>
    private static bool LaunchRunas(string exePath, string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
        };
        try
        {
            using var p = System.Diagnostics.Process.Start(psi);
            return p is not null;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // 1223 = ERROR_CANCELLED (пользователь отказал в UAC)
            if (ex.NativeErrorCode != 1223)
                throw new ProfileRegistryApplyException($"UAC elevation failed: {ex.Message}", ex);
            return false;
        }
    }
}
