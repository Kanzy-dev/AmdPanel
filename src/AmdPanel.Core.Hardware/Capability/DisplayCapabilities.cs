namespace AmdPanel.Core.Hardware.Capability;

/// <summary> Описание монитора и текущего режима, найденные через ADLX. </summary>
public sealed class DisplayInfo
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ManufacturerId { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string SerialNumber { get; init; } = string.Empty;

    /// <summary> Полный EDID (raw bytes), если ADLX/Win32 его отдает. </summary>
    public byte[]? RawEdid { get; init; }
    public int EdidLength { get; init; }

    public DisplayPortType PortType { get; init; }
    public bool IsPrimary { get; init; }
    public bool Connected { get; init; }

    public int CurrentWidth { get; init; }
    public int CurrentHeight { get; init; }
    public int CurrentRefreshHz { get; init; }
    public ColorDepthBits CurrentColorDepth { get; init; }
    public PixelFormat CurrentPixelFormat { get; init; }
    public ScalingMode CurrentScalingMode { get; init; }

    public bool FreeSyncSupported { get; init; }
    public bool FreeSyncEnabled { get; init; }
    public int  FreeSyncMinHz { get; init; }
    public int  FreeSyncMaxHz { get; init; }

    public bool GpuScalingSupported { get; init; }
    public bool GpuScalingEnabled { get; init; }

    // === v1.1.0: Custom Color (Brightness / Contrast / Saturation / Hue) ===
    // Текущие значения из AMD Adrenalin через IADLXDisplayCustomColor -
    // используются DisplayColorPageViewModel для инициализации слайдеров
    // актуальными значениями драйвера вместо дефолтных 50/50/100/50.
    // -1 = не поддерживается / не удалось прочитать.
    public bool CustomColorSupported { get; init; }
    public int  CustomColorBrightness { get; init; } = -1;
    public int  CustomColorContrast { get; init; } = -1;
    public int  CustomColorSaturation { get; init; } = -1;
    public int  CustomColorHue { get; init; } = -1;
}

/// <summary> Один валидный режим: разрешение + герцовка + bpc. </summary>
public sealed record DisplayMode(int Width, int Height, int RefreshHz, int BitsPerPixel = 8, bool Interlaced = false);

public enum DisplayPortType
{
    Unknown = 0,
    VGA     = 1,
    DVI     = 2,
    HDMI    = 3,
    DisplayPort = 4,
    WirelessDisplay = 5,
    USBTypeC = 6
}

public enum ColorDepthBits
{
    Bpp6  = 0,
    Bpp8  = 1,
    Bpp10 = 2,
    Bpp12 = 3,
    Bpp14 = 4,
    Bpp16 = 5
}

public enum PixelFormat
{
    Unknown   = 0,
    Rgb444    = 1,
    YCbCr444  = 2,
    YCbCr422  = 3,
    YCbCr420  = 4
}

public enum ScalingMode
{
    Unknown        = 0,
    FullScreen     = 1, // "Во весь экран"
    AspectRatio    = 2, // "Формат изображения"
    NoScaling      = 3, // "Без масштабирования"
    Centered       = 4
}
