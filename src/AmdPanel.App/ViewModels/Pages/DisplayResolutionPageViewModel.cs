using System.Collections.ObjectModel;
using AmdPanel.App.Mvvm;
using AmdPanel.App.Services;
using AmdPanel.Core.Hardware;
using AmdPanel.Core.Hardware.Capability;

namespace AmdPanel.App.ViewModels.Pages;

/// <summary>
/// Страница "Изменение разрешения" (Requirement 6 — фильтрация по bandwidth).
/// </summary>
public sealed class DisplayResolutionPageViewModel : ViewModelBase, IPageViewModel
{
    public string PageKey => "page.resolution";

    private readonly UiHardwareHub _hub;
    private readonly DisplayInfo _display;

    public DisplayResolutionPageViewModel(UiHardwareHub hub, DisplayInfo display)
    {
        _hub = hub;
        _display = display;
        LoadAvailableModes();
        LoadCurrent();
    }

    public bool IsAvailable => _display.Connected;

    // --- Состояние UI ---
    public ObservableCollection<ResolutionItem> Resolutions { get; } = new();
    public ObservableCollection<int> RefreshRates { get; } = new();
    public ObservableCollection<int> ColorDepths { get; } = new();
    public ObservableCollection<PixelFormatItem> PixelFormats { get; } = new();

    private ResolutionItem? _selectedResolution;
    public ResolutionItem? SelectedResolution
    {
        get => _selectedResolution;
        set
        {
            if (SetField(ref _selectedResolution, value))
            {
                RefreshRefreshRatesAndBpc();
                RecomputeBandwidthWarning();
            }
        }
    }

    private int _selectedRefresh;
    public int SelectedRefresh
    {
        get => _selectedRefresh;
        set { if (SetField(ref _selectedRefresh, value)) RecomputeBandwidthWarning(); }
    }

    private int _selectedBpc = 8;
    public int SelectedBpc
    {
        get => _selectedBpc;
        set { if (SetField(ref _selectedBpc, value)) RecomputeBandwidthWarning(); }
    }

    private PixelFormatItem? _selectedPixelFormat;
    public PixelFormatItem? SelectedPixelFormat
    {
        get => _selectedPixelFormat;
        set { if (SetField(ref _selectedPixelFormat, value)) RecomputeBandwidthWarning(); }
    }

    /// <summary> True, если текущая комбинация превышает пропускную способность порта. </summary>
    private bool _bandwidthExceeded;
    public bool BandwidthExceeded
    {
        get => _bandwidthExceeded;
        private set => SetField(ref _bandwidthExceeded, value);
    }

    public string PortName => _display.PortType.ToString();
    public string MonitorName => _display.Name;

    // --- Apply / Cancel ---

    /// <summary>
    /// Применяет выбранный режим. Через DisplayModeManager с watchdog 15с.
    /// Возвращает результат для показа в строке статуса.
    /// </summary>
    public async Task<DisplayApplyResult> ApplyAsync()
    {
        if (SelectedResolution is null) return new DisplayApplyResult(-1, false, false, "Не выбрано разрешение");
        string device = _display.DisplayName;
        var mgr = _hub.DisplayModeManager;
        return await mgr.ApplyModeAsync(device,
            SelectedResolution.Width, SelectedResolution.Height,
            SelectedRefresh, SelectedBpc);
    }

    // --- Helpers ---

    private void LoadCurrent()
    {
        // Заполняем PixelFormats - всегда RGB 444, опционально YCbCr 444/422/420
        PixelFormats.Add(new PixelFormatItem(PixelFormat.Rgb444,   "RGB 4:4:4"));
        PixelFormats.Add(new PixelFormatItem(PixelFormat.YCbCr444, "YCbCr 4:4:4"));
        PixelFormats.Add(new PixelFormatItem(PixelFormat.YCbCr422, "YCbCr 4:2:2"));
        SelectedPixelFormat = PixelFormats[0];

        ColorDepths.Add(8); ColorDepths.Add(10);
        SelectedBpc = (int)_display.CurrentColorDepth == (int)ColorDepthBits.Bpp10 ? 10 : 8;
    }

    private void LoadAvailableModes()
    {
        // Получаем полный список resolutions + refresh rates от ADLX
        var modes = _hub.Scanner.GetValidResolutions(_display.Index);
        // Фильтруем по bandwidth порта
        var maxBpc = (int)_display.CurrentColorDepth;
        if (maxBpc < 8) maxBpc = 8;
        var filtered = _hub.Scanner.FilterByBandwidth(modes, _display.PortType, maxBpc).ToList();

        var distinctResolutions = filtered
            .GroupBy(m => (m.Width, m.Height))
            .OrderByDescending(g => g.Key.Width)
            .Select(g => new ResolutionItem(g.Key.Width, g.Key.Height))
            .ToList();
        foreach (var r in distinctResolutions) Resolutions.Add(r);

        // Подсветка текущего режима
        SelectedResolution = Resolutions.FirstOrDefault(r =>
            r.Width == _display.CurrentWidth && r.Height == _display.CurrentHeight)
            ?? Resolutions.FirstOrDefault();
    }

    private void RefreshRefreshRatesAndBpc()
    {
        RefreshRates.Clear();
        if (SelectedResolution is null) return;
        var hz = _hub.Scanner.GetRefreshRates(_display.Index, SelectedResolution.Width, SelectedResolution.Height);
        foreach (var v in hz.OrderByDescending(x => x)) RefreshRates.Add(v);
        SelectedRefresh = RefreshRates.FirstOrDefault(x => x == _display.CurrentRefreshHz)
                          ?? RefreshRates.FirstOrDefault();

        // Bpc 10 доступен только для DisplayPort / HDMI 2.0+
        ColorDepths.Clear();
        ColorDepths.Add(8);
        if (_display.PortType is DisplayPortType.DisplayPort or DisplayPortType.HDMI)
            ColorDepths.Add(10);
        SelectedBpc = ColorDepths.Contains(10) && (int)_display.CurrentColorDepth >= (int)ColorDepthBits.Bpp10
            ? 10 : 8;
    }

    /// <summary>
    /// Перерасчёт флага превышения bandwidth (Requirement 6).
    /// Использует ту же формулу, что и HardwareScanner.FilterByBandwidth,
    /// но даёт пользователю возможность выбрать опасную комбинацию вручную
    /// (с предупреждением), вместо того чтобы полностью её скрывать.
    /// </summary>
    private void RecomputeBandwidthWarning()
    {
        if (SelectedResolution is null) { BandwidthExceeded = false; return; }
        double maxGbps = _display.PortType switch
        {
            DisplayPortType.DisplayPort => 25.92,
            DisplayPortType.HDMI        => 18.0,
            DisplayPortType.DVI         => 9.95,
            DisplayPortType.VGA         => double.MaxValue,
            _                           => 18.0
        };
        // Для YCbCr 4:2:2 эффективный bpp уменьшается вдвое
        double effectiveBpp = SelectedPixelFormat?.Format switch
        {
            PixelFormat.YCbCr422 => SelectedBpc * 1.5, // Y=8, Cb=4, Cr=4 → effective 12 bit per 2 pixels
            PixelFormat.YCbCr420 => SelectedBpc * 1.5,
            _                    => SelectedBpc * 3.0
        };
        double bandwidthGbps = SelectedResolution.Width * SelectedResolution.Height
                               * SelectedRefresh * effectiveBpp / 8.0 / 1e9;
        BandwidthExceeded = bandwidthGbps * 1.2 > maxGbps;
    }
}

public sealed record ResolutionItem(int Width, int Height)
{
    public override string ToString() => $"{Width} × {Height}";
}

public sealed record PixelFormatItem(PixelFormat Format, string DisplayLabel)
{
    public override string ToString() => DisplayLabel;
}
