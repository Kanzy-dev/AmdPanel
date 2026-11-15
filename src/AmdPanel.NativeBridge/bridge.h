//===========================================================================
// AmdPanel.NativeBridge - Flat-C экспорт над AMD ADLX SDK
//---------------------------------------------------------------------------
// Мост потребляет C++ COM-подобные интерфейсы ADLX (ISystemServices,
// IADLXGPUList, IADLXDisplayList, IADLXGPUTuning и др.) и отдаёт наружу
// плоские C-функции, которые C# слой (AmdPanel.Core.Hardware) вызывает
// через DllImport. Все строки возвращаются как const char* (UTF-8).
// Вся память под буферы, выделенная в bridge, освобождается через
// ADLXBridge_FreeBuffer - это позволяет C# стороне не думать о
// кросс-граничном освобождении CRT.
//===========================================================================
#ifndef AMDPANEL_NATIVE_BRIDGE_H
#define AMDPANEL_NATIVE_BRIDGE_H

#include <cstdint>
#include "ADLXHelper.h"

// Динамический экспорт: VC++ строит обёртку __declspec(dllexport) на основе
// макроса AMDADLXBRIDGE_EXPORTS (определён в .vcxproj для самой DLL).
#ifdef AMDADLXBRIDGE_EXPORTS
#define BRIDGE_API __declspec(dllexport)
#else
#define BRIDGE_API __declspec(dllimport)
#endif

#define BRIDGE_CALL __cdecl

#ifdef __cplusplus
extern "C" {
#endif

//-------------------------------------------------------------------------
// Коды возврата моста: наследуют семантику ADLX_RESULT, но используют
// стабильные значения, не зависящие от ADLXDefines.h.
//-------------------------------------------------------------------------
enum BridgeResult : int32_t
{
    BRIDGE_OK                    = 0,
    BRIDGE_FAIL                  = 1,
    BRIDGE_NOT_INITIALIZED       = 2,
    BRIDGE_NO_AMD_GPU            = 3,
    BRIDGE_INVALID_ARGS          = 4,
    BRIDGE_OUT_OF_MEMORY         = 5,
    BRIDGE_INTERFACE_UNSUPPORTED = 6,
};

//-------------------------------------------------------------------------
// GPU: базовая информация
//-------------------------------------------------------------------------
#pragma pack(push, 8)
struct BridgeGpuInfo
{
    int32_t     index;              // Порядковый номер в ADLX GPUList
    char        name[256];          // marketing name ("Radeon RX 6700 XT")
    char        driverVersion[64];  // e.g. "31.0.12015.1001"
    char        adapterId[64];      // внутренний ADLX unique id (как строка)
    int32_t     pciVendorId;        // 0x1002 для AMD
    int32_t     pciDeviceId;        // DEV_XXXX
    int32_t     pciSubSystemId;     // SUBSYS
    int32_t     pciRevisionId;
    int64_t     dedicatedVramBytes; // общий объём VRAM
    int32_t     asicFamily;         // ADLX_ASIC_FAMILY (Polaris=2, RDNA1=3...)
    int32_t     apiSupported;       // ADLX_API_SUPPORTED bit-mask
    int32_t     type;               // ADLX_GPU_TYPE (dGPU/iGPU/APU)
};

//-------------------------------------------------------------------------
// GPU: набор IsSupported для каждой функции панели
//-------------------------------------------------------------------------
struct BridgeGpuCapabilities
{
    int32_t index;                  //对应GPU索引
    int32_t antiLagSupported;
    int32_t chillSupported;
    int32_t imageSharpeningSupported;
    int32_t boostSupported;
    int32_t tessellationSupported;
    int32_t frameRateTargetSupported;
    int32_t enhancedSyncSupported;
    int32_t radeonSuperResolutionSupported;
    int32_t fsr1Supported;
    int32_t morphologicalAntiAliasingSupported;
    int32_t anisotropicFilteringSupported;
    int32_t waitForVerticalRefreshSupported;
    int32_t gpuTuningSupported;          // IADLXGPUTuning
    int32_t gpuAutoTuningSupported;      // IADLXGPUAutoTuning
    int32_t manualFanTuningSupported;    // IADLXGPUManualFanTuning
    int32_t manualGfxTuningSupported;
    int32_t manualVramTuningSupported;
    int32_t manualPowerTuningSupported;
    int32_t presetTuningSupported;
    int32_t smartAccessMemorySupported;
    int32_t performanceMonitoringSupported;
};

//-------------------------------------------------------------------------
// GPU: динамические диапазоны для слайдеров (из ADLX_IntRange)
//     значения < 0 означают "функция не поддерживается".
//-------------------------------------------------------------------------
struct BridgeTuningRanges
{
    int32_t index;

    // Manual GFX (core clock)
    int32_t gfxMinMHz;       int32_t gfxMaxMHz;     int32_t gfxStepMHz;
    // Manual VRAM (memory clock)
    int32_t vramMinMHz;      int32_t vramMaxMHz;    int32_t vramStepMHz;
    // Manual voltage (mV)
    int32_t voltageMinMv;    int32_t voltageMaxMv;  int32_t voltageStepMv;
    // Manual fan (RPM)
    int32_t fanMinRpm;       int32_t fanMaxRpm;     int32_t fanStepRpm;
    // Fan acoustic limit
    int32_t fanAcousticMinRpm; int32_t fanAcousticMaxRpm; int32_t fanAcousticStepRpm;
    // Power limit (%)
    int32_t powerMinPct;     int32_t powerMaxPct;   int32_t powerStepPct;
    // Tuning caps
    int32_t autoTuneSupported;       // IADLXGPUAutoTuning::IsSupportedAutoTune
};

//-------------------------------------------------------------------------
// Display: базовая информация + EDID
//-------------------------------------------------------------------------
struct BridgeDisplayInfo
{
    int32_t     index;                 // Порядковый в IADLXDisplayList
    char        name[256];             // имя монитора из EDID
    char        displayName[64];       // внутреннее имя ADLX ("\\Display1")
    char        manufacturerId[16];    // 3-буквенный PNP ID из EDID
    char        productCode[16];
    char        serialNumber[64];
    char        edidHex[8192];         // hex-строка полного EDID (256 или 128 байт)
    int32_t     edidLength;            // реальная длина EDID в байтах
    int32_t     portType;              // ADLX_DISPLAY_CONNECTOR_TYPE (HDMI/DP/DVI/VGA...)
    int32_t     connectionState;       // ADLX_DISPLAY_CONNECTION_TYPE
    int32_t     isPrimary;
    int32_t     currentWidth;
    int32_t     currentHeight;
    int32_t     currentRefreshHz;
    int32_t     currentColorDepth;     // ADLX_COLOR_DEPTH (8/10/12/16 bpc)
    int32_t     currentPixelFormat;    // ADLX_PIXEL_FORMAT
    int32_t     currentScalingMode;    // ADLX_SCALING_MODE
    int32_t     freeSyncSupported;
    int32_t     freeSyncEnabled;
    int32_t     freeSyncMinHz;
    int32_t     freeSyncMaxHz;
    int32_t     gpuScalingSupported;
    int32_t     gpuScalingEnabled;
};

//-------------------------------------------------------------------------
// Display: один режим разрешения + герцовка (валидный по EDID)
//-------------------------------------------------------------------------
struct BridgeDisplayMode
{
    int32_t width;
    int32_t height;
    int32_t refreshHz;
    int32_t bitsPerPixel;        // 8 / 10
    int32_t interlaced;
};

// Контейнер, передаваемый из моста в C#
struct BridgeDisplayModeList
{
    int32_t             count;
    BridgeDisplayMode*  items;   // выделен в мосте, освобождается через ADLXBridge_FreeBuffer
};
#pragma pack(pop)

//-------------------------------------------------------------------------
// API
//-------------------------------------------------------------------------

// Инициализация ADLX (без Adrenalin - работает на "Driver Only")
BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_Initialize();
// Тоже самое, но InitializeWithIncompatibleDriver - для legacy драйвера Polaris
BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_InitializeWithIncompatibleDriver();
// Завершение работы и выгрузка amdadlx64.dll
BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_Terminate();
// Возвращает версию ADLX SDK как const char* (без освобождения - статический буфер)
BRIDGE_API const char* BRIDGE_CALL ADLXBridge_QueryVersion();

//--- GPU ---
BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetGpuCount(int32_t* outCount);
BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetGpuInfo(int32_t gpuIndex, BridgeGpuInfo* outInfo);
BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetGpuCapabilities(int32_t gpuIndex, BridgeGpuCapabilities* outCaps);
BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetGpuTuningRanges(int32_t gpuIndex, BridgeTuningRanges* outRanges);

//--- Display ---
BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetDisplayCount(int32_t* outCount);
BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetDisplayInfo(int32_t displayIndex, BridgeDisplayInfo* outInfo);
BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetDisplayResolutionList(int32_t displayIndex, BridgeDisplayModeList** outList);
BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetDisplayRefreshRateList(int32_t displayIndex, int32_t width, int32_t height, int32_t** outHzList, int32_t* outCount);
BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetDisplayFreeSyncSupport(int32_t displayIndex, int32_t* supported, int32_t* enabled, int32_t* minHz, int32_t* maxHz);
BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetDisplayScalingMode(int32_t displayIndex, int32_t* currentMode, int32_t* supportedMask);
BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_SetDisplayScalingMode(int32_t displayIndex, int32_t newMode);
BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetDisplayPixelFormat(int32_t displayIndex, int32_t* currentFormat, int32_t* supportedMask);
BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_SetDisplayPixelFormat(int32_t displayIndex, int32_t newFormat);
BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetDisplayColorDepth(int32_t displayIndex, int32_t* currentDepth, int32_t* supportedMask);
BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_SetDisplayColorDepth(int32_t displayIndex, int32_t newDepth);

// === v1.1.0: Display Custom Color setters (через IADLXDisplayCustomColor) ===
// Атомарные операции - не требуют watchdog (не меняют timing сигнала).
BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_SetDisplayBrightness(int32_t displayIndex, int32_t value);
BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_SetDisplayContrast(int32_t displayIndex, int32_t value);
BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_SetDisplaySaturation(int32_t displayIndex, int32_t value);
BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_SetDisplayHue(int32_t displayIndex, int32_t value);

/// <summary>
/// v1.1.0: Диапазон для CustomColor параметра (из ADLX_IntRange).
/// recommendedDefault = (min + max) / 2 — используется как "factory default" при Reset.
/// </summary>
struct BridgeCustomColorRange
{
    int32_t minValue;
    int32_t maxValue;
    int32_t step;
    int32_t recommendedDefault;
};

/// <summary>
/// v1.0.1: Метрики GPU в реальном времени (для вкладки Performance).
/// Заполняется через IADLXPerformanceMonitoringServices::GetCurrentGPUMetrics.
/// </summary>
struct BridgeGpuMetrics
{
    int32_t index;
    double  gpuTemperature;
    double  gpuUsage;
    int32_t gpuClockMHz;
    int32_t vramClockMHz;
    int32_t fanSpeedRpm;
    double  powerDrawWatts;
    double  totalBoardPower;
    int32_t gpuVoltage;
    int32_t temperatureValid;
    int32_t usageValid;
    int32_t clockValid;
    int32_t vramClockValid;
    int32_t fanSpeedValid;
    int32_t powerValid;
    int32_t totalBoardPowerValid;
    int32_t voltageValid;
};

// === v1.1.0: Получение диапазонов CustomColor для слайдеров ===
BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetDisplayBrightnessRange(int32_t displayIndex, BridgeCustomColorRange* outRange);
BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetDisplayContrastRange(int32_t displayIndex, BridgeCustomColorRange* outRange);
BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetDisplaySaturationRange(int32_t displayIndex, BridgeCustomColorRange* outRange);
BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetDisplayHueRange(int32_t displayIndex, BridgeCustomColorRange* outRange);

// Освобождение любого буфера, выделенного мостом
BRIDGE_API void BRIDGE_CALL ADLXBridge_FreeBuffer(void* buffer);

#ifdef __cplusplus
} // extern "C"
#endif

#endif // AMDPANEL_NATIVE_BRIDGE_H
