namespace AmdPanel.Core.Profiles.Storage;

/// <summary>
/// Пути к каталогам данных AmdPanel в пользовательском профиле.
/// Использует %AppData%\AmdPanel для профилей и логов.
/// На Windows %AppData% обычно = C:\Users\&lt;user&gt;\AppData\Roaming.
/// </summary>
public static class ProfilePaths
{
    public static string AppDataRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AmdPanel");

    public static string ProfilesDirectory { get; } = Path.Combine(AppDataRoot, "profiles");
    public static string BackupsDirectory  { get; } = Path.Combine(AppDataRoot, "backups");
    public static string SettingsFile      { get; } = Path.Combine(AppDataRoot, "settings.json");
    public static string RegistryApplyQueueDirectory { get; } = Path.Combine(AppDataRoot, "registry-tasks");

    /// <summary> Создаёт все каталоги, если их ещё нет. </summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(ProfilesDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(RegistryApplyQueueDirectory);
    }

    public static string ProfileFilePath(string profileKey)
        => Path.Combine(ProfilesDirectory, profileKey + ".json");
}
