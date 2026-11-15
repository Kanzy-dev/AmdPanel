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
        // На Stage 2 делаем безопасный non-ADLX запрос через реестр,
        // чтобы не раздувать мост. На Stage 4 добавим IADLXPerformanceMonitoring.
        try
        {
            // Затык-placeholder с реальным интерфейсом: читаем температуры из реестра
            // HKLM\SYSTEM\...\Class\{GUID}\XXXX\0006\UMD - температура пишется драйвером
            // в некоторых версиях. В противном случае показываем 0.
            // Реализация через PerformanceMonitoring API будет в Stage 4.
            GpuTemperature = ReadRegistryTemperature();
        }
        catch { /* ignore - страница опциональна */ }
    }

    private double ReadRegistryTemperature()
    {
        // Безопасный fallback: вернуть 0 до Stage 4
        return 0;
    }
}

public sealed record FanCurvePoint(int Temperature, int FanPercent);

public sealed record GpuParameterRow(string Parameter, string Value);
