using System.IO.Compression;
using AmdPanel.Core.DriverManager.Models;
using AmdPanel.Core.Profiles.Storage;

namespace AmdPanel.Core.DriverManager.Services;

/// <summary>
/// Безопасный Clean Install драйвера (Requirement 4).
/// </summary>
/// <remarks>
/// <para>
/// Перед удалением старого драйвера:
/// </para>
/// <list type="number">
///   <item>Создаёт ZIP-резервную копию %AppData%\AmdPanel\profiles\ в
///   %AppData%\AmdPanel\backups\profiles-YYYYMMDD-HHMMSS.zip.</item>
///   <item>Поднимает событие <see cref="ScreenWillFlicker"/> для UI —
///   пользователь получает предупреждение о мигании экрана и временном
///   переключении на базовый видеоадаптер Windows.</item>
///   <item>Вызывает <see cref="DriverPackageInstaller.DeleteDriverAsync"/> для
///   pnputil /delete-driver oemXX.inf /uninstall /force.</item>
///   <item>Опционально требует перезагрузку (через
///   <see cref="RebootRequired"/> событие).</item>
/// </list>
/// </remarks>
public sealed class CleanInstaller
{
    private readonly DriverPackageInstaller _installer;

    /// <summary> Поднимается перед удалением драйвера — UI показывает модальное окно-предупреждение. </summary>
    public event EventHandler? ScreenWillFlicker;

    /// <summary> Поднимается если для завершения clean install требуется перезагрузка. </summary>
    public event EventHandler? RebootRequired;

    /// <summary> Поднимается после создания backup'а — UI показывает путь к архиву. </summary>
    public event EventHandler<BackupCreatedEventArgs>? BackupCreated;

    public CleanInstaller(DriverPackageInstaller installer)
    {
        _installer = installer;
    }

    /// <summary> Создаёт ZIP-резервную копию папки профилей в backups\. </summary>
    /// <returns> Путь к созданному .zip архиву. </returns>
    public string BackupProfiles()
    {
        ProfilePaths.EnsureDirectories();
        string sourceDir = ProfilePaths.ProfilesDirectory;
        if (!Directory.Exists(sourceDir) || !Directory.EnumerateFiles(sourceDir, "*.json").Any())
        {
            // Профилей ещё нет - архив пустой, но создаём структуру для согласованности
            Directory.CreateDirectory(sourceDir);
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string backupPath = Path.Combine(ProfilePaths.BackupsDirectory, $"profiles-{timestamp}.zip");
        if (File.Exists(backupPath)) File.Delete(backupPath);

        // ZipFile.CreateFromDirectory создает архив в одном вызове
        ZipFile.CreateFromDirectory(sourceDir, backupPath,
            CompressionLevel.Optimal, includeBaseDirectory: false);

        BackupCreated?.Invoke(this, new BackupCreatedEventArgs(backupPath, new FileInfo(backupPath).Length));
        return backupPath;
    }

    /// <summary> Восстанавливает профили из указанного архива в %AppData%\AmdPanel\profiles\. </summary>
    public void RestoreProfiles(string backupZipPath)
    {
        if (!File.Exists(backupZipPath))
            throw new FileNotFoundException($"Backup archive not found: {backupZipPath}");
        ProfilePaths.EnsureDirectories();
        string targetDir = ProfilePaths.ProfilesDirectory;
        // Очищаем текущие профили (только .json, .bak оставляем)
        foreach (var f in Directory.EnumerateFiles(targetDir, "*.json"))
            try { File.Delete(f); } catch { }
        ZipFile.ExtractToDirectory(backupZipPath, targetDir, overwriteFiles: true);
    }

    /// <summary>
    /// Удаляет старый драйвер перед установкой нового. Поднимает ScreenWillFlicker
    /// для UI предупреждения. После вызова обычно нужно reboot.
    /// </summary>
    /// <param name="logLine"> Action для логирования. </param>
    /// <param name="ct"> CancellationToken. </param>
    public async Task RemoveOldDriverAsync(Action<string>? logLine = null, CancellationToken ct = default)
    {
        logLine?.Invoke("Backing up user profiles...");
        string backupPath = BackupProfiles();
        logLine?.Invoke($"Backup created: {backupPath}");

        logLine?.Invoke("Notifying user: screen will flicker during driver removal");
        ScreenWillFlicker?.Invoke(this, EventArgs.Empty);

        logLine?.Invoke("Removing old AMD driver via pnputil /delete-driver ...");
        await _installer.DeleteDriverAsync(null, logLine, ct);

        logLine?.Invoke("Old driver removal complete. Reboot recommended before installing new version.");
        RebootRequired?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class BackupCreatedEventArgs : EventArgs
{
    public string ArchivePath { get; }
    public long SizeBytes { get; }
    public BackupCreatedEventArgs(string path, long sizeBytes)
    {
        ArchivePath = path;
        SizeBytes = sizeBytes;
    }
}
