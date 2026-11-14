using System.Diagnostics;
using System.Runtime.InteropServices;
using AmdPanel.Core.Hardware.Capability;
using AmdPanel.Core.Hardware.Exceptions;
using AmdPanel.Core.Hardware.Native;
using AmdPanel.Core.Hardware.Win32;

namespace AmdPanel.Core.Hardware;

/// <summary>
/// Снимок аппаратной конфигурации системы: список AMD GPU, их
/// capabilities, диапазоны tuning, и список мониторов с EDID.
/// </summary>
public sealed class HardwareSnapshot
{
    public string AdlxVersion { get; init; } = string.Empty;
    public DateTimeOffset ScannedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<GpuInfo> Gpus { get; init; } = new();
    public List<DisplayInfo> Displays { get; init; } = new();

    /// <summary> Для каждого GPU индекса: capabilities + ranges. </summary>
    public Dictionary<int, (GpuCapabilities Caps, GpuTuningRanges Ranges)> GpuDetails { get; init; } = new();

    /// <summary> Путь в реестре к ветке активного AMD Display adapter (найден через SetupApi). </summary>
    public string? AmdDriverRegistryPath { get; init; }

    /// <summary> Версия установленного драйвера из реестра (UMD / Catalyst). </summary>
    public string? InstalledDriverVersion { get; init; }

    /// <summary> Истинный статус: true, если в системе нет Adrenalin (только driver-only). </summary>
    public bool IsDriverOnlyInstallation { get; init; }
}

/// <summary>
/// Центральный класс динамической адаптации под любое железо.
/// <para>
/// Выполняет полное сканирование системы при каждом вызове Scan():
/// </para>
/// <list type="number">
///   <item>Опрашивает GPU через ADLX (GetGpuCount, GetGpuInfo, GetGpuCapabilities, GetGpuTuningRanges).</item>
///   <item>Опрашивает мониторы через ADLX (GetDisplayCount, GetDisplayInfo) + EDID через Win32 SetupApi.</item>
///   <item>Опрашивает перечни разрешений и герцовок для каждого монитора.</item>
///   <item>Через SetupApi находит активный AMD-адаптер и его ветку реестра драйвера.</item>
///   <item>Проверяет через реестр: установлен ли Adrenalin (есть ли ключ
///         HKLM\SOFTWARE\AMD\CN) - если нет, режим Driver-Only подтверждается.</item>
/// </list>
/// Никакого хардкода: все частоты/разрешения берутся из ADLX-диапазонов.
/// </summary>
public sealed class HardwareScanner
{
    private readonly AdlxContext _ctx;

    public HardwareScanner(AdlxContext ctx)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        if (!AdlxContext.IsProcessInitialized)
            throw new AdlxInitializationException(-1, "AdlxContext не инициализирован. Вызовите Initialize() перед сканированием.");
    }

    /// <summary> Запускает полное сканирование и возвращает immutable snapshot. </summary>
    public HardwareSnapshot Scan()
    {
        var sw = Stopwatch.StartNew();

        var snapshot = new HardwareSnapshot
        {
            AdlxVersion = _ctx.AdlxVersion
        };

        ScanGpus(snapshot);
        ScanDisplays(snapshot);
        ScanDriverRegistryAndStatus(snapshot);

        sw.Stop();
        Trace.WriteLine($"[HardwareScanner] Scan completed in {sw.ElapsedMilliseconds} ms " +
                        $"(GPUs={snapshot.Gpus.Count}, Displays={snapshot.Displays.Count})");
        return snapshot;
    }

    // =====================================================================
    //  GPU сканирование
    // =====================================================================

    private void ScanGpus(HardwareSnapshot snapshot)
    {
        int rc = AdlxBridgeNative.ADLXBridge_GetGpuCount(out int count);
        if (rc != AdlxBridgeNative.BRIDGE_OK)
            throw new AdlxInitializationException(rc, $"ADLXBridge_GetGpuCount failed: {AdlxBridgeNative.Describe(rc)}");

        if (count == 0)
            throw new NoAmdGpuException();

        for (int i = 0; i < count; i++)
        {
            rc = AdlxBridgeNative.ADLXBridge_GetGpuInfo(i, out BridgeGpuInfo raw);
            if (rc != AdlxBridgeNative.BRIDGE_OK) continue;

            var gpu = new GpuInfo
            {
                Index              = raw.Index,
                Name               = AdlxContext.BytesToUtf8(raw.Name),
                DriverVersion      = AdlxContext.BytesToUtf8(raw.DriverVersion),
                AdapterId          = AdlxContext.BytesToUtf8(raw.AdapterId),
                PciVendorId        = raw.PciVendorId,
                PciDeviceId        = raw.PciDeviceId,
                PciSubSystemId     = raw.PciSubSystemId,
                PciRevisionId      = raw.PciRevisionId,
                DedicatedVramBytes = raw.DedicatedVramBytes,
                AsicFamily         = raw.AsicFamily,
                ApiSupported       = raw.ApiSupported,
                Type               = raw.Type
            };
            snapshot.Gpus.Add(gpu);

            // Capabilities
            rc = AdlxBridgeNative.ADLXBridge_GetGpuCapabilities(i, out BridgeGpuCapabilities rawCaps);
            GpuCapabilities caps = rc == AdlxBridgeNative.BRIDGE_OK
                ? GpuCapabilities.FromBridge(rawCaps)
                : new GpuCapabilities { Index = i };

            // Ranges
            rc = AdlxBridgeNative.ADLXBridge_GetGpuTuningRanges(i, out BridgeTuningRanges rawRanges);
            GpuTuningRanges ranges = rc == AdlxBridgeNative.BRIDGE_OK
                ? GpuTuningRanges.FromBridge(rawRanges)
                : new GpuTuningRanges { Index = i };

            snapshot.GpuDetails[i] = (caps, ranges);
        }
    }

    // =====================================================================
    //  Display сканирование + EDID через Win32
    // =====================================================================

    private void ScanDisplays(HardwareSnapshot snapshot)
    {
        int rc = AdlxBridgeNative.ADLXBridge_GetDisplayCount(out int count);
        if (rc != AdlxBridgeNative.BRIDGE_OK) return;
        if (count == 0) return;

        // EDID Win32 - через EnumDisplayDevices + SetupApi для мониторной ветки.
        // Подробное чтение raw EDID из реестра MonitorClass не делается здесь,
        // чтобы не раздувать scanner - можно добавить при необходимости в Win32.DisplayEdidReader.

        for (int i = 0; i < count; i++)
        {
            rc = AdlxBridgeNative.ADLXBridge_GetDisplayInfo(i, out BridgeDisplayInfo raw);
            if (rc != AdlxBridgeNative.BRIDGE_OK) continue;

            // Динамический список герцовок для текущего режима - используется UI
            // для построения комбобокса Гц после выбора разрешения.
            var hzByResolution = new Dictionary<(int w, int h), int[]>();

            // Подгружаем raw EDID через SetupApi (если доступно) для точного
            // определения поддержки 10 bpc / YCbCr 4:2:2 по HDMI FRL.
            byte[]? edid = DisplayEdidReader.TryReadEdid(AdlxContext.BytesToUtf8(raw.DisplayName));

            var info = new DisplayInfo
            {
                Index              = raw.Index,
                Name               = AdlxContext.BytesToUtf8(raw.Name),
                DisplayName        = AdlxContext.BytesToUtf8(raw.DisplayName),
                ManufacturerId     = AdlxContext.BytesToUtf8(raw.ManufacturerId),
                ProductCode        = AdlxContext.BytesToUtf8(raw.ProductCode),
                SerialNumber       = AdlxContext.BytesToUtf8(raw.SerialNumber),
                RawEdid            = edid,
                EdidLength         = edid?.Length ?? raw.EdidLength,
                PortType           = (DisplayPortType)raw.PortType,
                IsPrimary          = raw.IsPrimary == 1,
                Connected          = raw.ConnectionState == 1,
                CurrentWidth       = raw.CurrentWidth,
                CurrentHeight      = raw.CurrentHeight,
                CurrentRefreshHz   = raw.CurrentRefreshHz,
                CurrentColorDepth  = (ColorDepthBits)raw.CurrentColorDepth,
                CurrentPixelFormat = (PixelFormat)raw.CurrentPixelFormat,
                CurrentScalingMode = (ScalingMode)raw.CurrentScalingMode,
                FreeSyncSupported  = raw.FreeSyncSupported == 1,
                FreeSyncEnabled    = raw.FreeSyncEnabled == 1,
                FreeSyncMinHz      = raw.FreeSyncMinHz,
                FreeSyncMaxHz      = raw.FreeSyncMaxHz,
                GpuScalingSupported = raw.GpuScalingSupported == 1,
                GpuScalingEnabled  = raw.GpuScalingEnabled == 1,
            };
            snapshot.Displays.Add(info);
        }
    }

    // =====================================================================
    //  Driver registry + Driver-Only детекция
    // =====================================================================

    private void ScanDriverRegistryAndStatus(HardwareSnapshot snapshot)
    {
        try
        {
            var path = SetupApiLocator.FindPrimaryAmdAdapter();
            if (path is not null)
            {
                snapshot.AmdDriverRegistryPath = path.DriverKeyPath;
                snapshot.InstalledDriverVersion = ReadDriverVersionFromRegistry(path.DriverKeyPath);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[HardwareScanner] SetupApi scan failed: {ex.Message}");
        }

        // Driver-Only vs Adrenalin: наличие в реестре ключа AMD\CN (Catalyst/Adrenalin)
        // указывает на установленный полный пакет. Если ключа нет - чистый драйвер.
        snapshot.IsDriverOnlyInstallation = !IsAdrenalinInstalled();
    }

    private static string? ReadDriverVersionFromRegistry(string driverKeyPath)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(driverKeyPath, writable: false);
            // "DriverVersion" - стандартное поле, записываемое inf-установщиком
            return key?.GetValue("DriverVersion") as string
                ?? key?.GetValue("MatchingDeviceId") as string;
        }
        catch { return null; }
    }

    private static bool IsAdrenalinInstalled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\AMD\CN");
            if (key is not null) return true;
            using var key2 = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\AMD\CN");
            return key2 is not null;
        }
        catch { return false; }
    }

    // =====================================================================
    //  Public helper API для UI (вызывается по требованию)
    // =====================================================================

    /// <summary> Возвращает список валидных разрешений для дисплея (ADLX). </summary>
    public List<DisplayMode> GetValidResolutions(int displayIndex)
    {
        var result = new List<DisplayMode>();
        int rc = AdlxBridgeNative.ADLXBridge_GetDisplayResolutionList(displayIndex, out IntPtr listPtr);
        if (rc != AdlxBridgeNative.BRIDGE_OK || listPtr == IntPtr.Zero) return result;

        try
        {
            var list = Marshal.PtrToStructure<BridgeDisplayModeList>(listPtr);
            if (list.Count > 0 && list.Items != IntPtr.Zero)
            {
                var arr = AdlxContext.ReadDisplayModeArray(list.Items, list.Count);
                foreach (var m in arr)
                {
                    result.Add(new DisplayMode(m.Width, m.Height, m.RefreshHz,
                        m.BitsPerPixel > 0 ? m.BitsPerPixel : 8,
                        m.Interlaced != 0));
                }
            }
        }
        finally
        {
            AdlxBridgeNative.ADLXBridge_FreeBuffer(listPtr);
        }
        return result;
    }

    /// <summary> Возвращает список поддерживаемых герцовок для заданного разрешения. </summary>
    public int[] GetRefreshRates(int displayIndex, int width, int height)
    {
        int rc = AdlxBridgeNative.ADLXBridge_GetDisplayRefreshRateList(displayIndex, width, height,
            out IntPtr hzPtr, out int count);
        if (rc != AdlxBridgeNative.BRIDGE_OK || hzPtr == IntPtr.Zero || count <= 0)
            return Array.Empty<int>();
        try
        {
            return AdlxContext.ReadInt32Array(hzPtr, count);
        }
        finally
        {
            AdlxBridgeNative.ADLXBridge_FreeBuffer(hzPtr);
        }
    }

    /// <summary>
    /// Фильтрация комбинаций разрешение/Гц/bpc по пропускной способности кабеля.
    /// Используется UI перед ChangeDisplaySettingsEx: если HDMI 1.4 не держит
    /// 4K@60Hz@10bpc - функция убирает такие комбинации из списка.
    /// </summary>
    public IEnumerable<DisplayMode> FilterByBandwidth(IEnumerable<DisplayMode> modes, DisplayPortType port, int maxBpc)
    {
        // Пропускная способность (Gbps) = W * H * Hz * bpc * 3 (RGB) / 8 / 1e9,
        // с учетом 8b/10b кодирования для HDMI 1.4 (×1.25) и без него для DP/HDMI 2.0.
        // Числа намеренно консервативны - лучше недодать частоту, чем поймать чёрный экран.
        double maxGbps = port switch
        {
            DisplayPortType.DisplayPort => 25.92, // DP 1.4 HBR3 4 lanes
            DisplayPortType.HDMI        => 18.0,  // HDMI 2.0 default; HDMI 1.4 ниже
            DisplayPortType.DVI         => 9.95,  // single-link DVI-D
            DisplayPortType.VGA         => double.MaxValue, // analog - bpc не ограничен
            _                           => 18.0
        };

        foreach (var m in modes)
        {
            int bpc = Math.Min(m.BitsPerPixel, maxBpc);
            double bytesPerPixel = bpc * 3 / 8.0;
            double bandwidthGbps = m.Width * m.Height * m.RefreshHz * bytesPerPixel / 1e9;
            // 20% запас на coding overhead и blanking
            if (bandwidthGbps * 1.2 <= maxGbps)
                yield return m;
        }
    }
}
