using System.Collections.ObjectModel;
using System.Windows.Threading;
using AmdPanel.App.Mvvm;
using AmdPanel.App.Services;
using AmdPanel.Core.Hardware;
using AmdPanel.Core.Hardware.Capability;

namespace AmdPanel.App.ViewModels.Pages;

/// <summary>
/// Опциональная вкладка мониторинга производительности (Requirement 6 из ТЗ).
/// </summary>
/// <remarks>
/// <para>
/// <b>v1.0.1 fix</b>: теперь использует реальный вызов
/// <c>ADLXBridge_GetGpuMetrics</c> → <c>IADLXPerformanceMonitoringServices::GetCurrentGPUMetrics</c>
/// вместо возврата 0. На Polaris (RX 580) доступны: GPUTemperature, GPUUsage,
/// GPUClockSpeed, GPUVRAMClockSpeed, GPUFanSpeed, GPUPower. GPUTotalBoardPower
/// и GPUVoltage могут быть недоступны - в этом случае соответствующее поле
/// остаётся 0 и *Valid = false.
/// </para>
/// <para>
/// Polling выполняется через <see cref="DispatcherTimer"/> с интервалом 1 секунда.
/// DispatcherTimer гарантирует, что обновления свойств происходят в UI-потоке
/// (нужно для WPF binding). Запускается только пока страница активна -
/// <see cref="StopPolling"/> вызывается из MainViewModel при переключении вкладки.
/// </para>
/// </remarks>
public sealed class PerformancePageViewModel : ViewModelBase, IPageViewModel
{
    public string PageKey => "page.performance";

    private readonly UiHardwareHub _hub;
    private readonly int _gpuIndex;
    private readonly GpuCapabilities _caps;
    private DispatcherTimer? _pollTimer;

    public PerformancePageViewModel(UiHardwareHub hub, int gpuIndex, GpuCapabilities caps)
    {
        _hub = hub;
        _gpuIndex = gpuIndex;
        _caps = caps;
    }

    public bool IsAvailable => _caps.PerformanceMonitoring;

    // --- Метрики (обновляются polling'ом через SetField, что поднимает PropertyChanged) ---
    private double _gpuTemperature;
    public double GpuTemperature { get => _gpuTemperature; private set => SetField(ref _gpuTemperature, value); }

    private double _gpuUsage;
    public double GpuUsage { get => _gpuUsage; private set => SetField(ref _gpuUsage, value); }

    private int _gpuClockMHz;
    public int GpuClockMHz { get => _gpuClockMHz; private set => SetField(ref _gpuClockMHz, value); }

    private int _vramClockMHz;
    public int VramClockMHz { get => _vramClockMHz; private set => SetField(ref _vramClockMHz, value); }

    private int _fanRpm;
    public int FanRpm { get => _fanRpm; private set => SetField(ref _fanRpm, value); }

    private double _powerDrawWatts;
    public double PowerDrawWatts { get => _powerDrawWatts; private set => SetField(ref _powerDrawWatts, value); }

    // --- Валидность (для скрытия полей, не поддерживаемых конкретным GPU) ---
    private bool _temperatureValid; public bool TemperatureValid { get => _temperatureValid; private set => SetField(ref _temperatureValid, value); }
    private bool _usageValid;       public bool UsageValid       { get => _usageValid;       private set => SetField(ref _usageValid, value); }
    private bool _clockValid;       public bool ClockValid       { get => _clockValid;       private set => SetField(ref _clockValid, value); }
    private bool _vramClockValid;   public bool VramClockValid   { get => _vramClockValid;   private set => SetField(ref _vramClockValid, value); }
    private bool _fanSpeedValid;    public bool FanSpeedValid    { get => _fanSpeedValid;    private set => SetField(ref _fanSpeedValid, value); }
    private bool _powerValid;       public bool PowerValid       { get => _powerValid;       private set => SetField(ref _powerValid, value); }

    public ObservableCollection<FanCurvePoint> FanCurve { get; } = new();
    public ObservableCollection<GpuParameterRow> ParameterRows { get; } = new();

    /// <summary>
    /// Запускает polling метрик через DispatcherTimer 1 Гц.
    /// DispatcherTimer (вместо System.Threading.Timer) гарантирует, что
    /// PropertyChanged события поднимаются в UI-потоке - иначе WPF binding
    /// мог бы падать с InvalidOperationException.
    /// </summary>
    public void StartPolling()
    {
        if (!IsAvailable) return;
        if (_pollTimer is not null) return;  // уже запущен

        _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _pollTimer.Tick += (_, _) => PollOnce();
        _pollTimer.Start();

        // Сразу делаем первый замер, чтобы UI не показывал 0 первую секунду
        PollOnce();
    }

    public void StopPolling()
    {
        if (_pollTimer is null) return;
        _pollTimer.Stop();
        _pollTimer = null;
    }

    /// <summary>
    /// Один замер метрик через ADLXBridge_GetGpuMetrics.
    /// Вызывается из DispatcherTimer.Tick - уже в UI-потоке, можно напрямую
    /// обновлять свойства.
    /// </summary>
    private void PollOnce()
    {
        try
        {
            var metrics = _hub.Scanner.GetGpuMetrics(_gpuIndex);

            // Обновляем только валидные метрики - невалидные остаются 0
            // (или прежним значением). *Valid используется XAML для скрытия
            // неподдерживаемых полей.
            if (metrics.TemperatureValid)  GpuTemperature = metrics.GpuTemperature;
            else                           TemperatureValid = false;
            if (metrics.UsageValid)        GpuUsage = metrics.GpuUsage;
            else                           UsageValid = false;
            if (metrics.ClockValid)        GpuClockMHz = metrics.GpuClockMHz;
            else                           ClockValid = false;
            if (metrics.VramClockValid)    VramClockMHz = metrics.VramClockMHz;
            else                           VramClockValid = false;
            if (metrics.FanSpeedValid)     FanRpm = metrics.FanSpeedRpm;
            else                           FanSpeedValid = false;
            if (metrics.PowerValid)        PowerDrawWatts = metrics.PowerDrawWatts;
            else                           PowerValid = false;

            // Обновляем Valid-флаги (после первого успешного замера)
            TemperatureValid = metrics.TemperatureValid;
            UsageValid       = metrics.UsageValid;
            ClockValid       = metrics.ClockValid;
            VramClockValid   = metrics.VramClockValid;
            FanSpeedValid    = metrics.FanSpeedValid;
            PowerValid       = metrics.PowerValid;
        }
        catch
        {
            // Никогда не пробрасываем - polling не должен ронять UI.
            // При ошибке просто оставляем предыдущие значения; следующий
            // тик попробует снова.
        }
    }
}

public sealed record FanCurvePoint(int Temperature, int FanPercent);

public sealed record GpuParameterRow(string Parameter, string Value);
