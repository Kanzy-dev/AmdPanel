namespace AmdPanel.Core.Hardware.Capability;

/// <summary> Параметры одного GPU, полученные через ADLX после сканирования. </summary>
public sealed class GpuInfo
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public string DriverVersion { get; init; } = string.Empty;
    public string AdapterId { get; init; } = string.Empty;

    public int PciVendorId { get; init; }
    public int PciDeviceId { get; init; }
    public int PciSubSystemId { get; init; }
    public int PciRevisionId { get; init; }

    public long DedicatedVramBytes { get; init; }
    public int AsicFamily { get; init; }
    public int ApiSupported { get; init; }
    public int Type { get; init; }

    /// <summary> Нормализованное имя архитектуры для UI ("Polaris", "RDNA2", ...). </summary>
    public string ArchitectureName => AsicFamily switch
    {
        0  => "Unknown",
        1  => "Sea Islands (GCN 2)",
        2  => "Polaris (GCN 4)",
        3  => "Vega (GCN 5)",
        4  => "RDNA 1",
        5  => "RDNA 2",
        6  => "RDNA 3",
        7  => "RDNA 4",
        _  => $"ASIC family {AsicFamily}"
    };
}

/// <summary>
/// Возможности GPU, найденные через ADLX IsSupported.
/// Никаких хардкоженных проверок по имени GPU - только то, что сообщил сам драйвер.
/// </summary>
public sealed class GpuCapabilities
{
    public int Index { get; init; }

    public bool AntiLag          { get; init; }
    public bool Chill            { get; init; }
    public bool ImageSharpening  { get; init; }
    public bool Boost            { get; init; }
    public bool Tessellation     { get; init; }
    public bool FrameRateTarget  { get; init; }
    public bool EnhancedSync     { get; init; }
    public bool RadeonSuperResolution { get; init; }
    public bool Fsr1             { get; init; }
    public bool MorphologicalAA  { get; init; }
    public bool Anisotropic      { get; init; }
    public bool WaitForVRefresh { get; init; }

    public bool GpuTuning        { get; init; }
    public bool GpuAutoTuning    { get; init; }
    public bool ManualFanTuning  { get; init; }
    public bool ManualGfxTuning  { get; init; }
    public bool ManualVramTuning { get; init; }
    public bool ManualPowerTuning{ get; init; }
    public bool PresetTuning     { get; init; }
    public bool SmartAccessMemory{ get; init; }
    public bool PerformanceMonitoring { get; init; }

    // === v1.0.1: Текущие значения (sync с AMD Adrenalin) ===
    // Эти поля заполняются при FromBridge() из значений, прочитанных через
    // IsEnabled() / GetSharpness() / GetMinFPS() / GetMaxFPS() в bridge.cpp.
    // Используются ViewModel'ями для инициализации UI фактическими значениями.
    public bool AntiLagEnabled                  { get; init; }
    public bool ChillEnabled                    { get; init; }
    public int  ChillMinFps                     { get; init; } = -1;
    public int  ChillMaxFps                     { get; init; } = -1;
    public bool ImageSharpeningEnabled          { get; init; }
    public int  ImageSharpeningSharpness        { get; init; } = -1;
    public bool BoostEnabled                    { get; init; }
    public bool EnhancedSyncEnabled             { get; init; }
    public bool WaitForVerticalRefreshEnabled   { get; init; }
    public bool FrameRateTargetEnabled          { get; init; }
    public bool MorphologicalAAEnabled          { get; init; }
    public bool AnisotropicEnabled              { get; init; }
    public bool TessellationEnabled             { get; init; }

    public static GpuCapabilities FromBridge(Native.BridgeGpuCapabilities c) => new()
    {
        Index                    = c.Index,
        AntiLag                  = c.AntiLagSupported          == 1,
        Chill                    = c.ChillSupported            == 1,
        ImageSharpening          = c.ImageSharpeningSupported  == 1,
        Boost                    = c.BoostSupported            == 1,
        Tessellation             = c.TessellationSupported     == 1,
        FrameRateTarget          = c.FrameRateTargetSupported  == 1,
        EnhancedSync             = c.EnhancedSyncSupported     == 1,
        RadeonSuperResolution    = c.RadeonSuperResolutionSupported == 1,
        Fsr1                     = c.Fsr1Supported             == 1,
        MorphologicalAA          = c.MorphologicalAntiAliasingSupported == 1,
        Anisotropic              = c.AnisotropicFilteringSupported == 1,
        WaitForVRefresh          = c.WaitForVerticalRefreshSupported == 1,
        GpuTuning                = c.GpuTuningSupported        == 1,
        GpuAutoTuning            = c.GpuAutoTuningSupported    == 1,
        ManualFanTuning          = c.ManualFanTuningSupported  == 1,
        ManualGfxTuning          = c.ManualGfxTuningSupported  == 1,
        ManualVramTuning         = c.ManualVramTuningSupported == 1,
        ManualPowerTuning        = c.ManualPowerTuningSupported== 1,
        PresetTuning             = c.PresetTuningSupported     == 1,
        SmartAccessMemory        = c.SmartAccessMemorySupported== 1,
        PerformanceMonitoring    = c.PerformanceMonitoringSupported == 1,

        // v1.0.1: текущие значения
        AntiLagEnabled                = c.AntiLagEnabled == 1,
        ChillEnabled                  = c.ChillEnabled == 1,
        ChillMinFps                   = c.ChillMinFps,
        ChillMaxFps                   = c.ChillMaxFps,
        ImageSharpeningEnabled        = c.ImageSharpeningEnabled == 1,
        ImageSharpeningSharpness      = c.ImageSharpeningSharpness,
        BoostEnabled                  = c.BoostEnabled == 1,
        EnhancedSyncEnabled           = c.EnhancedSyncEnabled == 1,
        WaitForVerticalRefreshEnabled = c.WaitForVerticalRefreshEnabled == 1,
        FrameRateTargetEnabled        = c.FrameRateTargetEnabled == 1,
        MorphologicalAAEnabled        = c.MorphologicalAntiAliasingEnabled == 1,
        AnisotropicEnabled            = c.AnisotropicFilteringEnabled == 1,
        TessellationEnabled           = c.TessellationEnabled == 1,
    };
}

/// <summary>
/// v1.0.1: Метрики GPU в реальном времени для вкладки Performance.
/// </summary>
public sealed class GpuMetrics
{
    public int     Index { get; init; }
    public double  GpuTemperature { get; init; }
    public double  GpuUsage { get; init; }
    public int     GpuClockMHz { get; init; }
    public int     VramClockMHz { get; init; }
    public int     FanSpeedRpm { get; init; }
    public double  PowerDrawWatts { get; init; }
    public double  TotalBoardPower { get; init; }
    public int     GpuVoltage { get; init; }
    public bool TemperatureValid { get; init; }
    public bool UsageValid { get; init; }
    public bool ClockValid { get; init; }
    public bool VramClockValid { get; init; }
    public bool FanSpeedValid { get; init; }
    public bool PowerValid { get; init; }
    public bool TotalBoardPowerValid { get; init; }
    public bool VoltageValid { get; init; }

    public static GpuMetrics FromBridge(Native.BridgeGpuMetrics m) => new()
    {
        Index = m.Index,
        GpuTemperature = m.GpuTemperature,
        GpuUsage = m.GpuUsage,
        GpuClockMHz = m.GpuClockMHz,
        VramClockMHz = m.VramClockMHz,
        FanSpeedRpm = m.FanSpeedRpm,
        PowerDrawWatts = m.PowerDrawWatts,
        TotalBoardPower = m.TotalBoardPower,
        GpuVoltage = m.GpuVoltage,
        TemperatureValid = m.TemperatureValid == 1,
        UsageValid = m.UsageValid == 1,
        ClockValid = m.ClockValid == 1,
        VramClockValid = m.VramClockValid == 1,
        FanSpeedValid = m.FanSpeedValid == 1,
        PowerValid = m.PowerValid == 1,
        TotalBoardPowerValid = m.TotalBoardPowerValid == 1,
        VoltageValid = m.VoltageValid == 1,
    };
}

/// <summary>
/// Динамические диапазоны частот/вольтажа/вентиляторов для слайдеров.
/// Поля могут быть 0/невалидны, если функция не поддерживается - проверяется через GpuCapabilities.
/// </summary>
public sealed class GpuTuningRanges
{
    public int Index { get; init; }
    public Range? GfxMHz      { get; init; }
    public Range? VramMHz     { get; init; }
    public Range? VoltageMv   { get; init; }
    public Range? FanRpm      { get; init; }
    public Range? FanAcousticRpm { get; init; }
    public Range? PowerPct    { get; init; }
    public bool  AutoTuneSupported { get; init; }

    public sealed record Range(int Min, int Max, int Step);

    public static GpuTuningRanges FromBridge(Native.BridgeTuningRanges r) => new()
    {
        Index = r.Index,
        GfxMHz      = Native.BridgeTuningRanges.IsValidRange(r.GfxMinMHz,    r.GfxMaxMHz,    r.GfxStepMHz)    ? new Range(r.GfxMinMHz,    r.GfxMaxMHz,    r.GfxStepMHz)    : null,
        VramMHz     = Native.BridgeTuningRanges.IsValidRange(r.VramMinMHz,   r.VramMaxMHz,   r.VramStepMHz)   ? new Range(r.VramMinMHz,   r.VramMaxMHz,   r.VramStepMHz)   : null,
        VoltageMv   = Native.BridgeTuningRanges.IsValidRange(r.VoltageMinMv, r.VoltageMaxMv, r.VoltageStepMv) ? new Range(r.VoltageMinMv, r.VoltageMaxMv, r.VoltageStepMv) : null,
        FanRpm      = Native.BridgeTuningRanges.IsValidRange(r.FanMinRpm,    r.FanMaxRpm,    r.FanStepRpm)    ? new Range(r.FanMinRpm,    r.FanMaxRpm,    r.FanStepRpm)    : null,
        FanAcousticRpm = Native.BridgeTuningRanges.IsValidRange(r.FanAcousticMinRpm, r.FanAcousticMaxRpm, r.FanAcousticStepRpm) ? new Range(r.FanAcousticMinRpm, r.FanAcousticMaxRpm, r.FanAcousticStepRpm) : null,
        PowerPct    = Native.BridgeTuningRanges.IsValidRange(r.PowerMinPct,  r.PowerMaxPct,  r.PowerStepPct)  ? new Range(r.PowerMinPct,  r.PowerMaxPct,  r.PowerStepPct)  : null,
        AutoTuneSupported = r.AutoTuneSupported == 1,
    };
}
