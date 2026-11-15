using System.Collections.ObjectModel;
using AmdPanel.App.Mvvm;
using AmdPanel.App.Services;
using AmdPanel.Core.Hardware;
using AmdPanel.Core.Hardware.Capability;
using AmdPanel.Core.Hardware.Native;

namespace AmdPanel.App.ViewModels.Pages;

/// <summary>
/// Опциональная вкладка мониторинга производительности (Requirement 6 из ТЗ).
/// В Этапе 2 — каркас с polling через ADLX (температура, частота GPU/VRAM,
/// загрузка, RPM вентилятора). Обновляется по DispatcherTimer 1 Гц только
/// пока страница активна, чтобы не было лишнего фонового потока.
/// </summary>
public sealed class PerformancePageViewModel : ViewModelBase, IPageViewModel
{
    public string PageKey => "page.performance";

    private readonly UiHardwareHub _hub;
    private readonly int _gpuIndex;
    private readonly GpuCapabilities _caps;
    private System.Threading.Timer? _pollTimer;

    public PerformancePageViewModel(UiHardwareHub hub, int gpuIndex, GpuCapabilities caps)
    {
        _hub = hub;
        _gpuIndex = gpuIndex;
        _caps = caps;
    }

    public bool IsAvailable => _caps.PerformanceMonitoring;

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

    public ObservableCollection<FanCurvePoint> FanCurve { get; } = new();
    public ObservableCollection<GpuParameterRow> ParameterRows { get; } = new();

    public void StartPolling()
    {
        if (!IsAvailable) return;
        _pollTimer ??= new System.Threading.Timer(_ => PollOnce(), null, 0, 1000);
    }

    public void StopPolling()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    private void PollOnce()
    {
        // Stage 2: Реальный мониторинг через ADLX мост
        try
        {
            var metrics = _hub.Scanner.GetGpuMetrics(_gpuIndex);
            if (metrics.TemperatureValid)
                GpuTemperature = metrics.GpuTemperature;
            else
                GpuTemperature = 0;

            if (metrics.UsageValid)
                GpuUsage = metrics.GpuUsage;
            else
                GpuUsage = 0;

            if (metrics.ClockValid)
                GpuClockMHz = metrics.GpuClockMHz;
            else
                GpuClockMHz = 0;

            if (metrics.VramClockValid)
                VramClockMHz = metrics.VramClockMHz;
            else
                VramClockMHz = 0;

            if (metrics.FanSpeedValid)
                FanRpm = metrics.FanSpeedRpm;
            else
                FanRpm = 0;

            if (metrics.PowerValid)
                PowerDrawWatts = metrics.PowerDrawWatts;
            else
                PowerDrawWatts = 0;
        }
        catch { /* ignore - страница опциональна */ }
    }
}

public sealed record FanCurvePoint(int Temperature, int FanPercent);

public sealed record GpuParameterRow(string Parameter, string Value);
