using System.Collections.ObjectModel;
using AmdPanel.App.Mvvm;
using AmdPanel.App.Services;
using AmdPanel.Core.DriverManager.Models;
using AmdPanel.Core.DriverManager.Services;
using AmdPanel.Core.Hardware;

namespace AmdPanel.App.ViewModels.Pages;

/// <summary>
/// Страница "Управление драйверами" (модуль 5 из ТЗ).
/// </summary>
/// <remarks>
/// Полная реализация Stage 4: использует DriverManagerService из Core.DriverManager
/// для определения статуса, проверки обновлений, скачивания и установки.
/// Все длительные операции асинхронные; прогресс через ProgressChanged event
/// маршалируется в UI-поток через Dispatcher.
/// </remarks>
public sealed class DriverManagerPageViewModel : ViewModelBase, IPageViewModel
{
    public string PageKey => "page.driverManager";

    private readonly UiHardwareHub _hub;
    private DriverManagerService? _service;

    public DriverManagerPageViewModel(UiHardwareHub hub)
    {
        _hub = hub;
        try
        {
            RefreshStatus();
        }
        catch (Exception ex)
        {
            // v1.1.0: Никогда не роняем приложение при открытии вкладки драйверов.
            // AmdDriverDownloader ctor может выбросить FormatException если
            // UserAgent парсер упадёт (теперь пофикшено через TryParseAdd, но
            // defensive try/catch остаётся для надёжности).
            InstallLog.Add($"[ERROR] Driver manager initialization failed: {ex.Message}");
            System.Diagnostics.Trace.WriteLine($"[DriverManagerPageViewModel] init failed: {ex}");
        }
    }

    /// <summary> Подключает реальный DriverManagerService. Вызывается из MainViewModel. </summary>
    public void BindService(DriverManagerService service)
    {
        try
        {
            _service = service;
            _service.ProgressChanged += OnProgressChanged;
            _service.ScreenWillFlicker += OnScreenWillFlicker;
            _service.RebootRequired += OnRebootRequired;
            _service.BackupCreated += OnBackupCreated;
        }
        catch (Exception ex)
        {
            InstallLog.Add($"[ERROR] BindService failed: {ex.Message}");
            System.Diagnostics.Trace.WriteLine($"[DriverManagerPageViewModel] BindService failed: {ex}");
        }
    }

    public bool IsAvailable => true;

    // --- Bound properties ---
    public string GpuName => _hub.Snapshot.Gpus.FirstOrDefault()?.Name ?? "—";
    public string DriverVersion => _hub.Snapshot.InstalledDriverVersion ?? "—";
    public string AdlxVersion => _hub.Snapshot.AdlxVersion;
    public bool IsDriverOnly => _hub.Snapshot.IsDriverOnlyInstallation;
    public string RegistryPath => _hub.Snapshot.AmdDriverRegistryPath ?? "—";

    /// <summary> Архитектура GPU, определённая через ArchitectureDetector. </summary>
    public string ArchitectureName
    {
        get
        {
            var gpu = _hub.Snapshot.Gpus.FirstOrDefault();
            if (gpu is null) return "—";
            var arch = AmdPanel.Core.DriverManager.Services.ArchitectureDetector.Detect(gpu.PciDeviceId);
            return AmdPanel.Core.DriverManager.Services.ArchitectureDetector.GetDisplayName(arch);
        }
    }

    /// <summary> Ветка драйвера (Legacy / Current). </summary>
    public string DriverBranchName
    {
        get
        {
            var gpu = _hub.Snapshot.Gpus.FirstOrDefault();
            if (gpu is null) return "—";
            var arch = AmdPanel.Core.DriverManager.Services.ArchitectureDetector.Detect(gpu.PciDeviceId);
            var branch = AmdPanel.Core.DriverManager.Services.ArchitectureDetector.GetBranch(arch);
            return branch switch
            {
                AmdPanel.Core.DriverManager.Models.DriverBranch.Legacy  => "Legacy (Polaris/Vega)",
                AmdPanel.Core.DriverManager.Models.DriverBranch.Current => "Current (RDNA)",
                _ => "—"
            };
        }
    }

    private bool _cleanInstallChecked;
    public bool CleanInstallChecked
    {
        get => _cleanInstallChecked;
        set => SetField(ref _cleanInstallChecked, value);
    }

    private int _installProgress;
    public int InstallProgress
    {
        get => _installProgress;
        set => SetField(ref _installProgress, value);
    }

    private string _currentStage = string.Empty;
    public string CurrentStage
    {
        get => _currentStage;
        set => SetField(ref _currentStage, value);
    }

    public ObservableCollection<string> InstallLog { get; } = new();

    private string _selectedFilePath = string.Empty;
    public string SelectedFilePath
    {
        get => _selectedFilePath;
        set => SetField(ref _selectedFilePath, value);
    }

    private string _updateInfo = string.Empty;
    public string UpdateInfo
    {
        get => _updateInfo;
        set => SetField(ref _updateInfo, value);
    }

    public void RefreshStatus()
    {
        OnPropertyChanged(nameof(GpuName));
        OnPropertyChanged(nameof(DriverVersion));
        OnPropertyChanged(nameof(AdlxVersion));
        OnPropertyChanged(nameof(IsDriverOnly));
        OnPropertyChanged(nameof(RegistryPath));
        OnPropertyChanged(nameof(ArchitectureName));
        OnPropertyChanged(nameof(DriverBranchName));
    }

    /// <summary> Проверка обновлений: открывает AMD support страницу в браузере. </summary>
    public async Task CheckUpdatesAsync()
    {
        if (_service is null)
        {
            InstallLog.Add("DriverManagerService не инициализирован.");
            return;
        }
        InstallLog.Clear();
        InstallLog.Add("Checking for updates...");
        var info = await _service.CheckForUpdatesAsync();
        UpdateInfo = $"Branch: {info.Branch}\nURL: {info.DownloadUrl}";
        InstallLog.Add($"Update URL: {info.DownloadUrl}");
        InstallLog.Add($"Release notes: {info.ReleaseNotesUrl}");
        InstallLog.Add("Click URL above to download the latest AMD driver package manually.");
        // Открываем браузер с URL
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = info.DownloadUrl.ToString(),
                UseShellExecute = true,
            });
        }
        catch { /* ignore */ }
    }

    /// <summary> Запуск установки. Если требуется UAC — вызывает elevated runner. </summary>
    public async Task InstallFromFileAsync(string path)
    {
        SelectedFilePath = path;
        if (_service is null)
        {
            InstallLog.Add("DriverManagerService не инициализирован.");
            return;
        }

        // Установка драйвера требует UAC (pnputil /add-driver). Если текущий
        // процесс не elevated, запускаем отдельный runner через ShellExecute "runas".
        if (!Services.UacElevationService.IsRunningElevated)
        {
            InstallLog.Add("Requesting UAC elevation to install driver...");
            bool launched = Services.UacElevationService.LaunchElevated(
                $"--install-driver \"{path}\" --clean {CleanInstallChecked}");
            InstallLog.Add(launched
                ? "Elevated runner launched. See its console window for progress."
                : "UAC elevation cancelled by user.");
            return;
        }

        // Уже elevated - выполняем установку прямо здесь
        InstallLog.Clear();
        InstallLog.Add($"Installing from: {path}");
        InstallLog.Add($"Clean install: {CleanInstallChecked}");
        InstallProgress = 0;
        try
        {
            await _service.InstallFromFileAsync(path, CleanInstallChecked);
            InstallLog.Add("Installation completed.");
        }
        catch (Exception ex)
        {
            InstallLog.Add($"ERROR: {ex.Message}");
        }
    }

    // === Event handlers from DriverManagerService ===

    private void OnProgressChanged(object? sender, DriverInstallProgress p)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            InstallProgress = p.Percent;
            CurrentStage = p.Stage.ToString();
            InstallLog.Add($"[{p.Stage}] {p.Percent}% — {p.Message}");
        });
    }

    private void OnScreenWillFlicker(object? sender, EventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            InstallLog.Add("WARNING: screen will flicker during driver removal.");
            System.Windows.MessageBox.Show(
                System.Windows.Application.Current?.MainWindow,
                "Screen will flicker and temporarily switch to the basic Windows display adapter during driver removal.\n\nThis is normal. Do not turn off your computer.",
                "AmdPanel - Driver Installation",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        });
    }

    private void OnRebootRequired(object? sender, EventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            InstallLog.Add("Reboot required to complete installation.");
            System.Windows.MessageBox.Show(
                System.Windows.Application.Current?.MainWindow,
                "A reboot is required to complete the driver installation.",
                "AmdPanel - Reboot Required",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        });
    }

    private void OnBackupCreated(object? sender, BackupCreatedEventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            InstallLog.Add($"Profiles backup: {e.ArchivePath} ({e.SizeBytes / 1024} KB)");
        });
    }
}
