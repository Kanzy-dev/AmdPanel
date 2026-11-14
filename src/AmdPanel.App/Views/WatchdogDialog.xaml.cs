using System.Windows;
using System.Windows.Threading;
using AmdPanel.Core.Hardware;

namespace AmdPanel.App.Views;

/// <summary>
/// Модальное окно подтверждения изменений дисплея с watchdog-таймером.
/// Все UI-обновления прогресс-бара выполняются строго в UI-потоке через
/// DispatcherTimer (Requirement 5). Закрытие окна через Confirm/Revert
/// вызывает события, на которые подписан WatchdogDialogService — он в свою
/// очередь дёргает DisplayWatchdogController.Confirm()/Revert().
/// </summary>
public partial class WatchdogDialog : Window
{
    private DispatcherTimer? _timer;
    private DateTime _startedAt;
    private int _totalSeconds = DisplayModeManager.WatchdogSeconds;

    /// <summary> Имя устройства (\\.\DISPLAY1). </summary>
    public string DisplayDevice { get; set; } = string.Empty;

    /// <summary> Строка нового режима ("1920×1080 @ 60 Hz · 8 bpc"). </summary>
    public string NewModeDescription { get; set; } = string.Empty;

    /// <summary> Строка предыдущего режима. </summary>
    public string OldModeDescription { get; set; } = string.Empty;

    /// <summary> Полная длительность watchdog в секундах (по умолчанию 15). </summary>
    public int TotalSeconds
    {
        get => _totalSeconds;
        set { _totalSeconds = value; }
    }

    public event EventHandler? Confirmed;
    public event EventHandler? Reverted;

    public WatchdogDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Подстановка локализованной строки с плейсхолдером {0}
        string template = (string)Application.Current.TryFindResource("watchdog.message")
                          ?? "If you don't respond within {0} seconds, the previous settings will be restored automatically.";
        MessageText.Text = string.Format(template, _totalSeconds);

        PreviousText.Text = OldModeDescription;
        NewText.Text = NewModeDescription;

        WatchdogProgress.Minimum = 0;
        WatchdogProgress.Maximum = _totalSeconds * 1000;
        WatchdogProgress.Value = 0;

        _startedAt = DateTime.UtcNow;
        // DispatcherTimer гарантирует, что Tick срабатывает в UI-потоке
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _timer = null;
        }
    }

    /// <summary> Тик в UI-потоке: обновляем прогресс-бар; если время вышло - закрываем с Revert. </summary>
    private void OnTimerTick(object? sender, EventArgs e)
    {
        var elapsed = DateTime.UtcNow - _startedAt;
        WatchdogProgress.Value = elapsed.TotalMilliseconds;

        if (elapsed.TotalSeconds >= _totalSeconds)
        {
            _timer?.Stop();
            // Авто-откат: поднимаем Reverted и закрываем окно
            Reverted?.Invoke(this, EventArgs.Empty);
            Close();
        }
    }

    private void OnYesClick(object sender, RoutedEventArgs e)
    {
        _timer?.Stop();
        Confirmed?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void OnNoClick(object sender, RoutedEventArgs e)
    {
        _timer?.Stop();
        Reverted?.Invoke(this, EventArgs.Empty);
        Close();
    }
}
