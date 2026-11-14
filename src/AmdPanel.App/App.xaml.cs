using System.IO;
using System.Windows;
using System.Windows.Threading;
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

    /// <summary> Путь к файлу crash-лога: %AppData%\AmdPanel\crash.log. </summary>
    private static string CrashLogPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AmdPanel", "crash.log");

    public MainViewModel MainViewModel => _mainViewModel!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // === v1.0.1: Глобальные обработчики необработанных исключений ===
        // Программа никогда не должна закрываться молча - любое исключение
        // логируется в %AppData%\AmdPanel\crash.log и показывается пользователю
        // через MessageBox. Это критично для диагностики багов на реальном железе.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

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
            WriteCrashLog("ADLX initialization failure", ex);
            MessageBox.Show(
                $"{FindResource("app.title")}\n\n{ex.Message}\n\n" +
                $"Crash log: {CrashLogPath}",
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
            WriteCrashLog("GameProfileWatcher failed to start (non-fatal)", ex);
        }

        _mainViewModel = new MainViewModel(_hub!, _watchdogService, _languageService, _profileService, globalProfile);
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
        _mainViewModel?.ActivePage = null;
        _adlxContext?.Dispose();
        base.OnExit(e);
    }

    // =================================================================
    //  v1.0.1: Global exception handlers
    // =================================================================

    /// <summary>
    /// DispatcherUnhandledException: ловит исключения из UI-потока WPF
    /// (binding errors, click handlers, ADLX calls из UI-потока и т.д.).
    /// e.Handled = true предотвращает crash приложения.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog("DispatcherUnhandledException", e.Exception);
        ShowCrashDialog(e.Exception);
        e.Handled = true;  // Не даём приложению закрыться
    }

    /// <summary>
    /// AppDomain.CurrentDomain.UnhandledException: ловит исключения из
    /// не-UI потоков (фоновые задачи, ADLX callbacks, WMI watcher и т.д.).
    /// Это "last resort" - приложение всё равно упадёт, но лог будет записан.
    /// </summary>
    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            WriteCrashLog($"AppDomain.UnhandledException (IsTerminating={e.IsTerminating})", ex);
            if (!e.IsTerminating)
                ShowCrashDialog(ex);
        }
    }

    /// <summary>
    /// TaskScheduler.UnobservedTaskException: ловит исключения из async Task,
    /// которые никто не наблюдал (забыли await, fire-and-forget).
    /// e.SetObserved() предотвращает crash.
    /// </summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog("TaskScheduler.UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    /// <summary>
    /// Записывает crash в %AppData%\AmdPanel\crash.log с timestamp и полным stack trace.
    /// Файл дозаписывается (append), чтобы сохранять историю всех крашей.
    /// </summary>
    private static void WriteCrashLog(string category, Exception ex)
    {
        try
        {
            string dir = Path.GetDirectoryName(CrashLogPath) ?? string.Empty;
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string entry =
                $"========== {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} =========={Environment.NewLine}" +
                $"Category: {category}{Environment.NewLine}" +
                $"App version: {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}{Environment.NewLine}" +
                $"OS: {Environment.OSVersion}{Environment.NewLine}" +
                $"Exception type: {ex.GetType().FullName}{Environment.NewLine}" +
                $"Message: {ex.Message}{Environment.NewLine}" +
                $"Source: {ex.Source}{Environment.NewLine}" +
                $"Stack trace:{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}";
            if (ex.InnerException is not null)
            {
                entry += $"--- Inner exception ---{Environment.NewLine}" +
                         $"Type: {ex.InnerException.GetType().FullName}{Environment.NewLine}" +
                         $"Message: {ex.InnerException.Message}{Environment.NewLine}" +
                         $"Stack: {ex.InnerException.StackTrace}{Environment.NewLine}";
            }
            entry += Environment.NewLine;
            File.AppendAllText(CrashLogPath, entry);
        }
        catch
        {
            // Никогда не пробрасываем исключение из логгера
        }
    }

    /// <summary> Показывает MessageBox с описанием краша. </summary>
    private static void ShowCrashDialog(Exception ex)
    {
        string msg =
            $"AmdPanel encountered an unexpected error and recovered.{Environment.NewLine}{Environment.NewLine}" +
            $"Error: {ex.GetType().Name}{Environment.NewLine}" +
            $"Message: {ex.Message}{Environment.NewLine}{Environment.NewLine}" +
            $"Details written to:{Environment.NewLine}{CrashLogPath}";
        try
        {
            MessageBox.Show(msg, "AmdPanel - Error recovered",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch
        {
            // Если MessageBox тоже упал - пишем хотя бы в Trace
            System.Diagnostics.Trace.WriteLine($"[AmdPanel crash] {ex}");
        }
    }
}
