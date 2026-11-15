using System.Windows;
using AmdPanel.App.Services;
using AmdPanel.App.ViewModels;
using AmdPanel.Core.Hardware;
using AmdPanel.Core.Profiles.CLI;
using AmdPanel.Core.Profiles.Services;
using AmdPanel.Core.Profiles.Storage;

namespace AmdPanel.App;

/// <summary>
/// Точка входа WPF приложения. Здесь создаётся DI-граф объектов:
/// AdlxContext → UiHardwareHub → ProfileService → MainViewModel → MainWindow.
/// Никаких IoC-контейнеров - graph маленький, явно собранный в code.
/// </summary>
public partial class App : Application
{
    private AdlxContext? _adlxContext;
    private UiHardwareHub? _hub;
    private MainViewModel? _mainViewModel;
    private GameProfileWatcher? _gameProfileWatcher;
    private ProfileService? _profileService;
    private readonly LanguageService _languageService = new();
    private readonly WatchdogDialogService _watchdogService = new();

    public MainViewModel MainViewModel => _mainViewModel!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // === CLI modes (no UI) ===

        // --apply-profile <path>: загрузить JSON и применить без UI (планировщик Windows)
        if (e.Args.Length > 0 && string.Equals(e.Args[0], "--apply-profile", StringComparison.OrdinalIgnoreCase))
        {
            int rc = await CliApplyProfileRunner.RunAsync(e.Args);
            Shutdown(rc);
            return;
        }

        // --apply-registry-only <path>: elevated runner для HKLM UMD-настроек
        if (e.Args.Length > 0 && string.Equals(e.Args[0], "--apply-registry-only", StringComparison.OrdinalIgnoreCase))
        {
            int rc = await CliApplyRegistryRunner.RunAsync(e.Args);
            Shutdown(rc);
            return;
        }

        // --install-driver <path> [--clean true|false]: elevated runner для Core.DriverManager (Stage 4)
        if (e.Args.Length > 0 && string.Equals(e.Args[0], "--install-driver", StringComparison.OrdinalIgnoreCase))
        {
            int rc = await AmdPanel.Core.DriverManager.CLI.CliInstallDriverRunner.RunAsync(e.Args);
            Shutdown(rc);
            return;
        }

        // === Normal UI startup ===
        _languageService.Initialize();

        try
        {
            _adlxContext = new AdlxContext();
            _hub = new UiHardwareHub(_adlxContext);
            await _hub.ScanAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"{FindResource("app.title")}\n\n{ex.Message}",
                "AmdPanel",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // Profile services (Stage 3)
        var repository = new JsonProfileRepository();
        var registryApplier = new RegistryApplier();
        _profileService = new ProfileService(_hub!, repository, registryApplier);

        // Глобальный профиль: загружаем существующий или создаём дефолтный
        var globalProfile = _profileService.LoadOrCreateForCurrent();

        // GameProfileWatcher: мониторинг запуска игр без admin-прав
        var processMonitor = new ProcessMonitor(TimeSpan.FromSeconds(1.5));
        _gameProfileWatcher = new GameProfileWatcher(_profileService, processMonitor, globalProfile);
        try { _gameProfileWatcher.Start(); }
        catch (Exception ex)
        {
            // Мониторинг - не критичный для UI. Если не запустился - логируем и продолжаем.
            System.Diagnostics.Trace.WriteLine($"[App] GameProfileWatcher failed to start: {ex.Message}");
        }

        var _configService = new AppConfigService();
        var _updateService = new AppUpdateService(_configService);
        var contextMenuService = new ContextMenuService(_configService);
        _mainViewModel = new MainViewModel(_hub!, _watchdogService, _languageService, _profileService, globalProfile, contextMenuService, _updateService, repository);
        foreach (var g in _hub!.GetAmdGpus())
            _mainViewModel.Gpus.Add(new GpuSelectorItem(g));
        _mainViewModel.SelectedGpu = _mainViewModel.Gpus.FirstOrDefault();

        var window = new MainWindow { DataContext = _mainViewModel };
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _gameProfileWatcher?.Dispose();
        // _mainViewModel?.ActivePage = _mainViewModel; // Commented out - assignment error
        _adlxContext?.Dispose();
        base.OnExit(e);
    }
}
