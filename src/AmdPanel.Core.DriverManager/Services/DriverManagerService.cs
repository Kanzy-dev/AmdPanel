using AmdPanel.Core.DriverManager.Exceptions;
using AmdPanel.Core.DriverManager.Models;
using AmdPanel.Core.Hardware;

namespace AmdPanel.Core.DriverManager.Services;

/// <summary>
/// Главный сервис менеджера драйверов. Оркестрирует все шаги:
/// 1) Detect — чтение текущего статуса
/// 2) CheckForUpdates — HTTP-запрос к amd.com
/// 3) DownloadAsync — загрузка с прогрессом
/// 4) ExtractAsync — распаковка 7z SFX
/// 5) InstallFromFileAsync — установка через pnputil + проверка amdadlx64.dll
/// 6) CleanInstallAsync — backup + delete old + install new
/// </summary>
public sealed class DriverManagerService
{
    private readonly AmdDriverDownloader _downloader;
    private readonly SevenZipExtractor _extractor;
    private readonly DriverPackageInstaller _installer;
    private readonly CleanInstaller _cleanInstaller;
    private readonly DriverStatusDetector _statusDetector;
    private readonly HardwareSnapshot _snapshot;

    /// <summary> Поднимается на каждом шаге установки с процентом и сообщением. </summary>
    public event EventHandler<DriverInstallProgress>? ProgressChanged;

    /// <summary> Поднимается перед удалением драйвера — UI показывает модальное окно-предупреждение. </summary>
    public event EventHandler? ScreenWillFlicker;

    /// <summary> Поднимается если для завершения clean install требуется перезагрузка. </summary>
    public event EventHandler? RebootRequired;

    /// <summary> Поднимается после создания backup'а профилей. </summary>
    public event EventHandler<BackupCreatedEventArgs>? BackupCreated;

    public DriverManagerService(HardwareSnapshot snapshot)
    {
        _snapshot = snapshot;
        _downloader = new AmdDriverDownloader();
        _extractor = new SevenZipExtractor();
        _installer = new DriverPackageInstaller();
        _statusDetector = new DriverStatusDetector();
        _cleanInstaller = new CleanInstaller(_installer);

        // Проброс событий из CleanInstaller наружу
        _cleanInstaller.ScreenWillFlicker += (_, e) => ScreenWillFlicker?.Invoke(this, e);
        _cleanInstaller.RebootRequired    += (_, e) => RebootRequired?.Invoke(this, e);
        _cleanInstaller.BackupCreated     += (_, e) => BackupCreated?.Invoke(this, e);
    }

    /// <summary> Читает текущий статус драйвера из реестра. </summary>
    public DriverStatusInfo DetectCurrentStatus()
        => _statusDetector.Detect(_snapshot);

    /// <summary>
    /// Проверяет наличие обновления по архитектуре GPU.
    /// </summary>
    /// <remarks>
    /// AMD не предоставляет официальный JSON API для проверки версий - URL
    /// обновлений хардкодятся в зависимости от архитектуры. Эта функция
    /// возвращает info с актуальной URL обновления AMD для соответствующей ветки
    /// (Legacy / Current). Пользователь сам решает, скачивать или нет.
    /// </remarks>
    public Task<DriverUpdateInfo> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        var status = DetectCurrentStatus();
        var branch = status.Branch;
        var arch   = status.DetectedArchitecture;

        // AMD не имеет стабильного public API - используем известные страницы
        // auto-detect AMD. Возвращает info с URL страницы загрузки.
        Uri updateUrl = branch switch
        {
            DriverBranch.Legacy  => new Uri("https://www.amd.com/en/support/legacy-display-drivers"),
            DriverBranch.Current => new Uri("https://www.amd.com/en/support"),
            _ => new Uri("https://www.amd.com/en/support"),
        };

        var info = new DriverUpdateInfo
        {
            Version = string.Empty,  // невозможно определить без HTML parsing
            ReleaseDate = DateTimeOffset.UtcNow,
            DownloadUrl = updateUrl,
            SizeBytes = 0,
            Branch = branch,
            ReleaseNotesUrl = branch == DriverBranch.Legacy
                ? "https://www.amd.com/en/support/legacy-display-drivers"
                : "https://www.amd.com/en/support/kb/release-notes",
            IsNewer = false,
        };
        return Task.FromResult(info);
    }

    /// <summary>
    /// Скачивает .exe пакет драйвера с прогрессом.
    /// </summary>
    public async Task<string> DownloadAsync(Uri url, CancellationToken ct = default)
    {
        string destDir = Path.Combine(Path.GetTempPath(), "AmdPanel-Downloads");
        Directory.CreateDirectory(destDir);
        string destPath = Path.Combine(destDir, "amd-driver.exe");

        RaiseProgress(DriverInstallStage.Downloading, 0, $"Downloading from {url}");
        await _downloader.DownloadAsync(url, destPath, (pct, received, total) =>
        {
            if (pct >= 0)
                RaiseProgress(DriverInstallStage.Downloading, pct,
                    $"Downloaded {received / 1024 / 1024} MB / {total / 1024 / 1024} MB");
        }, ct);
        RaiseProgress(DriverInstallStage.DownloadComplete, 100, $"Saved to {destPath}");
        return destPath;
    }

    /// <summary>
    /// Полный процесс установки из .exe или .inf файла.
    /// </summary>
    /// <param name="sourcePath"> Путь к .exe (AMD SFX) или готовому .inf. </param>
    /// <param name="cleanInstall"> true - перед установкой удалить старый драйвер. </param>
    /// <param name="ct"> CancellationToken. </param>
    public async Task InstallFromFileAsync(string sourcePath, bool cleanInstall = false,
        CancellationToken ct = default)
    {
        try
        {
            RaiseProgress(DriverInstallStage.Extracting7z, 0, $"Extracting {Path.GetFileName(sourcePath)}");
            string extractionDir = SevenZipExtractor.DefaultExtractionDirectory;
            // Очищаем старую распакованную папку
            if (Directory.Exists(extractionDir))
                try { Directory.Delete(extractionDir, recursive: true); } catch { }

            string infPath = await _extractor.ExtractAsync(sourcePath, extractionDir,
                (pct, msg) => RaiseProgress(DriverInstallStage.Extracting7z, pct, msg), ct);
            RaiseProgress(DriverInstallStage.ExtractionComplete, 80, $"Extraction complete. INF: {Path.GetFileName(infPath)}");

            RaiseProgress(DriverInstallStage.LocatingInf, 85, $"Located INF: {infPath}");

            if (cleanInstall)
            {
                RaiseProgress(DriverInstallStage.BackingUpProfiles, 90, "Backing up user profiles");
                // CleanInstaller создаёт backup, поднимет ScreenWillFlicker, удалит старый
                await _cleanInstaller.RemoveOldDriverAsync(
                    msg => RaiseProgress(DriverInstallStage.DeletingOldDriver, 92, msg), ct);
                RaiseProgress(DriverInstallStage.OldDriverDeleted, 93, "Old driver deleted");
            }

            RaiseProgress(DriverInstallStage.InstallingInf, 95, "Installing new driver via pnputil");
            await _installer.InstallInfAsync(infPath,
                msg => RaiseProgress(DriverInstallStage.InstallingInf, 96, msg), ct);

            RaiseProgress(DriverInstallStage.VerifyingAdlxLibrary, 98, "Verifying amdadlx64.dll in System32");
            RaiseProgress(DriverInstallStage.Completed, 100, "Installation completed successfully.");
        }
        catch (AdlxLibraryMissingException)
        {
            RaiseProgress(DriverInstallStage.Failed, 0,
                "FAILED: amdadlx64.dll missing in System32 after installation. " +
                "Panel cannot control GPU until ADLX-capable driver is installed.");
            throw;
        }
        catch (Exception ex)
        {
            RaiseProgress(DriverInstallStage.Failed, 0, $"FAILED: {ex.Message}");
            throw;
        }
    }

    private void RaiseProgress(DriverInstallStage stage, int percent, string message)
    {
        var progress = new DriverInstallProgress
        {
            Stage = stage,
            Percent = percent,
            Message = message,
        };
        ProgressChanged?.Invoke(this, progress);
    }
}
