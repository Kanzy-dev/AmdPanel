using System.Runtime.InteropServices;

namespace AmdPanel.Core.Hardware.Native;

// Внимание: StructLayout(LayoutKind.Sequential) обязателен - C++ сторона
// использует #pragma pack(push,8) и обычное выравнивание. Поля char[256] в C++
// превращаются в byte-буферы в C#; конвертация в строку UTF-8 выполняется
// вручную через Marshal.PtrToStringAnsi(...) в AdlxContext.CopyToManagedString.

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct BridgeGpuInfo
{
    public int Index;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public byte[] Name;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]  public byte[] DriverVersion;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]  public byte[] AdapterId;
    public int PciVendorId;
    public int PciDeviceId;
    public int PciSubSystemId;
    public int PciRevisionId;
    public long DedicatedVramBytes;
    public int AsicFamily;
    public int ApiSupported;
    public int Type;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct BridgeGpuCapabilities
{
    public int Index;
    public int AntiLagSupported;
    public int ChillSupported;
    public int ImageSharpeningSupported;
    public int BoostSupported;
    public int TessellationSupported;
    public int FrameRateTargetSupported;
    public int EnhancedSyncSupported;
    public int RadeonSuperResolutionSupported;
    public int Fsr1Supported;
    public int MorphologicalAntiAliasingSupported;
    public int AnisotropicFilteringSupported;
    public int WaitForVerticalRefreshSupported;
    public int GpuTuningSupported;
    public int GpuAutoTuningSupported;
    public int ManualFanTuningSupported;
    public int ManualGfxTuningSupported;
    public int ManualVramTuningSupported;
    public int ManualPowerTuningSupported;
    public int PresetTuningSupported;
    public int SmartAccessMemorySupported;
    public int PerformanceMonitoringSupported;

    public bool IsSupported(string feature) => feature switch
    {
        "AntiLag"          => AntiLagSupported          == 1,
        "Chill"            => ChillSupported            == 1,
        "ImageSharpening"  => ImageSharpeningSupported  == 1,
        "Boost"            => BoostSupported            == 1,
        "Tessellation"     => TessellationSupported     == 1,
        "FrameRateTarget"  => FrameRateTargetSupported  == 1,
        "EnhancedSync"     => EnhancedSyncSupported     == 1,
        "RSR"              => RadeonSuperResolutionSupported == 1,
        "FSR1"             => Fsr1Supported             == 1,
        "MorphAA"          => MorphologicalAntiAliasingSupported == 1,
        "Anisotropic"      => AnisotropicFilteringSupported == 1,
        "WaitForVRefresh"  => WaitForVerticalRefreshSupported == 1,
        "GpuTuning"        => GpuTuningSupported        == 1,
        "AutoTune"         => GpuAutoTuningSupported    == 1,
        "ManualFanTuning"  => ManualFanTuningSupported  == 1,
        "ManualGfxTuning"  => ManualGfxTuningSupported  == 1,
        "ManualVramTuning" => ManualVramTuningSupported == 1,
        "ManualPowerTuning"=> ManualPowerTuningSupported== 1,
        "PresetTuning"     => PresetTuningSupported     == 1,
        "SmartAccessMemory"=> SmartAccessMemorySupported== 1,
        "PerfMonitoring"   => PerformanceMonitoringSupported == 1,
        _ => false
    };
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct BridgeTuningRanges
{
    public int Index;
    public int GfxMinMHz,       GfxMaxMHz,       GfxStepMHz;
    public int VramMinMHz,      VramMaxMHz,      VramStepMHz;
    public int VoltageMinMv,    VoltageMaxMv,    VoltageStepMv;
    public int FanMinRpm,       FanMaxRpm,       FanStepRpm;
    public int FanAcousticMinRpm, FanAcousticMaxRpm, FanAcousticStepRpm;
    public int PowerMinPct,     PowerMaxPct,     PowerStepPct;
    public int AutoTuneSupported;

    /// <summary> Возвращает true, если диапазон валиден (не -1 на всех полях). </summary>
    public static bool IsValidRange(int mn, int mx, int st) => mn >= 0 && mx > mn && st > 0;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct BridgeDisplayInfo
{
    public int Index;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public byte[] Name;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]  public byte[] DisplayName;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]  public byte[] ManufacturerId;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]  public byte[] ProductCode;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]  public byte[] SerialNumber;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8192)] public byte[] EdidHex;
    public int EdidLength;
    public int PortType;
    public int ConnectionState;
    public int IsPrimary;
    public int CurrentWidth;
    public int CurrentHeight;
    public int CurrentRefreshHz;
    public int CurrentColorDepth;
    public int CurrentPixelFormat;
    public int CurrentScalingMode;
    public int FreeSyncSupported;
    public int FreeSyncEnabled;
    public int FreeSyncMinHz;
    public int FreeSyncMaxHz;
    public int GpuScalingSupported;
    public int GpuScalingEnabled;

    // v1.1.0: Custom Color (из IADLXDisplayCustomColor)
    public int CustomColorSupported;
    public int CustomColorBrightness;
    public int CustomColorContrast;
    public int CustomColorSaturation;
    public int CustomColorHue;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct BridgeDisplayMode
{
    public int Width;
    public int Height;
    public int RefreshHz;
    public int BitsPerPixel;
    public int Interlaced;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct BridgeDisplayModeList
{
    public int Count;
    public IntPtr Items; // pointer to BridgeDisplayMode[Count]
}

/// <summary>
/// v1.1.0: Диапазон CustomColor параметра (Brightness/Contrast/Saturation/Hue).
/// recommendedDefault = (min + max) / 2 — используется как "factory default" при Reset.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct BridgeCustomColorRange
{
    public int MinValue;
    public int MaxValue;
    public int Step;
    public int RecommendedDefault;
}

/// <summary>
/// v1.1.0: Текущее состояние 3D настроек GPU (для двусторонней синхронизации)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct BridgeGpuCurrentState
{
    public int Index;
    
    // AntiLag
    public int AntiLagEnabled;
    
    // Chill
    public int ChillEnabled;
    public int ChillMinFps;
    public int ChillMaxFps;
    
    // Image Sharpening
    public int ImageSharpeningEnabled;
    public int Sharpness;
    
    // Boost
    public int BoostEnabled;
    
    // V-Sync (WaitForVerticalRefresh)
    public int WaitForVRefreshEnabled;
    
    // Tessellation
    public int TessellationEnabled;
    public int TessellationLevel;
    
    // Anisotropic Filtering
    public int AnisotropicEnabled;
    public int AnisotropicLevel;
}

/// <summary>
/// v1.0.1: Метрики GPU в реальном времени.
/// Каждое поле имеет *Valid флаг (0 = не поддерживается на этом GPU).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct BridgeGpuMetrics
{
    public int    Index;
    public double GpuTemperature;
    public double GpuUsage;
    public int    GpuClockMHz;
    public int    VramClockMHz;
    public int    FanSpeedRpm;
    public double PowerDrawWatts;
    public double TotalBoardPower;
    public int    GpuVoltage;
    public int TemperatureValid;
    public int UsageValid;
    public int ClockValid;
    public int VramClockValid;
    public int FanSpeedValid;
    public int PowerValid;
    public int TotalBoardPowerValid;
    public int VoltageValid;
}
