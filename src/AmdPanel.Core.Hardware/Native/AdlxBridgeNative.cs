using System.Runtime.InteropServices;

namespace AmdPanel.Core.Hardware.Native;

/// <summary>
/// Прямые P/Invoke декларации к C++ мосту AmdPanel.NativeBridge.dll.
/// Структуры расположены в том же порядке, что и в bridge.h - любое
/// изменение в bridge.h должно зеркально отражаться здесь.
/// </summary>
internal static class AdlxBridgeNative
{
    private const string DllName = "AmdPanel.NativeBridge.dll";
    private const CallingConvention Cc = CallingConvention.Cdecl;

    //--- Result codes (mirror of BridgeResult enum from bridge.h) ---
    public const int BRIDGE_OK                    = 0;
    public const int BRIDGE_FAIL                  = 1;
    public const int BRIDGE_NOT_INITIALIZED       = 2;
    public const int BRIDGE_NO_AMD_GPU            = 3;
    public const int BRIDGE_INVALID_ARGS          = 4;
    public const int BRIDGE_OUT_OF_MEMORY         = 5;
    public const int BRIDGE_INTERFACE_UNSUPPORTED = 6;

    [DllImport(DllName, CallingConvention = Cc)]
    public static extern int ADLXBridge_Initialize();

    [DllImport(DllName, CallingConvention = Cc)]
    public static extern int ADLXBridge_InitializeWithIncompatibleDriver();

    [DllImport(DllName, CallingConvention = Cc)]
    public static extern int ADLXBridge_Terminate();

    [DllImport(DllName, CallingConvention = Cc)]
    public static extern IntPtr ADLXBridge_QueryVersion();

    [DllImport(DllName, CallingConvention = Cc)]
    public static extern int ADLXBridge_GetGpuCount(out int outCount);

    [DllImport(DllName, CallingConvention = Cc)]
    public static extern int ADLXBridge_GetGpuInfo(int gpuIndex, out BridgeGpuInfo outInfo);

    [DllImport(DllName, CallingConvention = Cc)]
    public static extern int ADLXBridge_GetGpuCapabilities(int gpuIndex, out BridgeGpuCapabilities outCaps);

    [DllImport(DllName, CallingConvention = Cc)]
    public static extern int ADLXBridge_GetGpuTuningRanges(int gpuIndex, out BridgeTuningRanges outRanges);

    [DllImport(DllName, CallingConvention = Cc)]
    public static extern int ADLXBridge_GetDisplayCount(out int outCount);

    [DllImport(DllName, CallingConvention = Cc)]
    public static extern int ADLXBridge_GetDisplayInfo(int displayIndex, out BridgeDisplayInfo outInfo);

    [DllImport(DllName, CallingConvention = Cc)]
    public static extern int ADLXBridge_GetDisplayResolutionList(int displayIndex, out IntPtr outList);

    [DllImport(DllName, CallingConvention = Cc)]
    public static extern int ADLXBridge_GetDisplayRefreshRateList(int displayIndex, int width, int height,
        out IntPtr outHzList, out int outCount);

    [DllImport(DllName, CallingConvention = Cc)]
    public static extern int ADLXBridge_GetDisplayFreeSyncSupport(int displayIndex,
        out int supported, out int enabled, out int minHz, out int maxHz);

    [DllImport(DllName, CallingConvention = Cc)]
    public static extern int ADLXBridge_GetDisplayScalingMode(int displayIndex,
        out int currentMode, out int supportedMask);

    [DllImport(DllName, CallingConvention = Cc)]
    public static extern int ADLXBridge_SetDisplayScalingMode(int displayIndex, int newMode);

    [DllImport(DllName, CallingConvention = Cc)]
    public static extern int ADLXBridge_GetDisplayPixelFormat(int displayIndex,
        out int currentFormat, out int supportedMask);

    [DllImport(DllName, CallingConvention = Cc)]
    public static extern int ADLXBridge_SetDisplayPixelFormat(int displayIndex, int newFormat);

    [DllImport(DllName, CallingConvention = Cc)]
    public static extern int ADLXBridge_GetDisplayColorDepth(int displayIndex,
        out int currentDepth, out int supportedMask);

    [DllImport(DllName, CallingConvention = Cc)]
    public static extern int ADLXBridge_SetDisplayColorDepth(int displayIndex, int newDepth);

    // v1.0.1: Performance metrics
    [DllImport(DllName, CallingConvention = Cc)]
    public static extern int ADLXBridge_GetGpuMetrics(int gpuIndex, out BridgeGpuMetrics outMetrics);

    [DllImport(DllName, CallingConvention = Cc)]
    public static extern void ADLXBridge_FreeBuffer(IntPtr buffer);

    /// <summary> Преобразование кода моста в человекочитаемое сообщение. </summary>
    public static string Describe(int code) => code switch
    {
        BRIDGE_OK                    => "OK",
        BRIDGE_FAIL                  => "ADLX вызов завершился ошибкой (ADLX_FAIL)",
        BRIDGE_NOT_INITIALIZED       => "ADLX не инициализирован",
        BRIDGE_NO_AMD_GPU            => "AMD GPU не найден",
        BRIDGE_INVALID_ARGS          => "Некорректные аргументы вызова моста",
        BRIDGE_OUT_OF_MEMORY         => "Недостаточно памяти в C++ мосте",
        BRIDGE_INTERFACE_UNSUPPORTED => "Интерфейс ADLX не поддерживается данной версией драйвера",
        _ => $"Неизвестная ошибка моста: код {code}"
    };
}
