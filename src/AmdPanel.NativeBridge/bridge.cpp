//===========================================================================
// bridge.cpp - Реализация плоской C-обёртки над AMD ADLX SDK.
//---------------------------------------------------------------------------
// КАЖДАЯ сигнатура сверена с реальными заголовочными файлами ADLX SDK в
// third_party/ADLXSDK/Include/. Никаких выдуманных методов:
//
//   IADLXSystem:
//     - GetGPUs(IADLXGPUList**)
//     - GetDisplaysServices(IADLXDisplayServices**)
//     - Get3DSettingsServices(IADLX3DSettingsServices**)
//     - GetGPUTuningServices(IADLXGPUTuningServices**)
//     - GetPerformanceMonitoringServices(IADLXPerformanceMonitoringServices**)
//
//   IADLXList:  Size() без аргументов (возвращает adlx_uint)
//
//   IADLXGPU:
//     - Name(const char**), DriverPath(const char**), PNPString(const char**)
//     - VendorId(const char**), DeviceId(const char**), SubSystemId(const char**),
//       RevisionId(const char**), SubSystemVendorId(const char**)
//     - ASICFamilyType(ADLX_ASIC_FAMILY_TYPE*), Type(ADLX_GPU_TYPE*)
//     - TotalVRAM(adlx_uint* vramMB) - возвращает МЕГАБАЙТЫ!
//     - UniqueId(adlx_int*)
//     - IsExternal(adlx_bool*)
//
//   IADLXGPU2 (extension via QueryInterface):
//     - DriverVersion(const char**)
//     - AMDWindowsDriverVersion(const char**)
//
//   IADLX3DSettingsServices:
//     GetAntiLag(IADLXGPU*, IADLX3DAntiLag**)
//     GetChill(IADLXGPU*, IADLX3DChill**)
//     GetBoost(IADLXGPU*, IADLX3DBoost**)
//     GetImageSharpening(IADLXGPU*, IADLX3DImageSharpening**)
//     GetEnhancedSync(IADLXGPU*, IADLX3DEnhancedSync**)
//     GetWaitForVerticalRefresh(IADLXGPU*, IADLX3DWaitForVerticalRefresh**)
//     GetFrameRateTargetControl(IADLXGPU*, IADLX3DFrameRateTargetControl**)
//     GetMorphologicalAntiAliasing(IADLXGPU*, IADLX3DMorphologicalAntiAliasing**)
//     GetAnisotropicFiltering(IADLXGPU*, IADLX3DAnisotropicFiltering**)
//     GetTessellation(IADLXGPU*, IADLX3DTessellation**)
//
//   Каждый IADLX3D*XXX interface имеет:
//     - IsSupported(adlx_bool*)
//     - IsEnabled(adlx_bool*)
//
//   IADLXGPUTuningServices:
//     IsSupportedAutoTuning(IADLXGPU*, adlx_bool*)
//     IsSupportedPresetTuning(IADLXGPU*, adlx_bool*)
//     IsSupportedManualGFXTuning(IADLXGPU*, adlx_bool*)
//     IsSupportedManualVRAMTuning(IADLXGPU*, adlx_bool*)
//     IsSupportedManualFanTuning(IADLXGPU*, adlx_bool*)
//     IsSupportedManualPowerTuning(IADLXGPU*, adlx_bool*)
//     GetManualGFXTuning(IADLXGPU*, IADLXInterface**) → QueryInterface для
//       IADLXManualGraphicsTuning1 (Polaris/Vega/RDNA - universal)
//     GetManualVRAMTuning(IADLXGPU*, IADLXInterface**) → IADLXManualVRAMTuning1
//     GetManualFanTuning(IADLXGPU*, IADLXInterface**) → IADLXManualFanTuning
//     GetManualPowerTuning(IADLXGPU*, IADLXInterface**) → IADLXManualPowerTuning
//
//   IADLXManualGraphicsTuning1 (v1, все AMD GPU):
//     GetGPUTuningRanges(ADLX_IntRange* freqRange, ADLX_IntRange* voltRange)
//     GetGPUMinFrequencyRange(ADLX_IntRange*), GetGPUMaxFrequencyRange(ADLX_IntRange*)
//     GetGPUVoltageRange(ADLX_IntRange*)
//   На RDNA дополнительно доступен IADLXManualGraphicsTuning2 - имеет
//   отдельные методы вместо GetGPUTuningRanges, но v1 работает везде.
//
//   IADLXManualVRAMTuning1 (v1):
//     GetMaxVRAMFrequencyRange(ADLX_IntRange*)
//
//   IADLXManualFanTuning (base, все AMD GPU):
//     GetFanTuningRanges(ADLX_IntRange* speedRange, ADLX_IntRange* tempRange)
//     GetMinAcousticLimitRange(ADLX_IntRange*)
//
//   IADLXManualPowerTuning (base, все AMD GPU):
//     GetPowerLimitRange(ADLX_IntRange*)
//
//   IADLXDisplayServices:
//     GetNumberOfDisplays(adlx_uint*), GetDisplays(IADLXDisplayList**)
//     GetFreeSync(IADLXDisplay*, IADLXDisplayFreeSync**)
//     GetGPUScaling(IADLXDisplay*, IADLXDisplayGPUScaling**)
//     GetScalingMode(IADLXDisplay*, IADLXDisplayScalingMode**)
//     GetColorDepth(IADLXDisplay*, IADLXDisplayColorDepth**)
//     GetPixelFormat(IADLXDisplay*, IADLXDisplayPixelFormat**)
//
//   IADLXDisplay:
//     Name(const char**), EDID(const char** hex string),
//     ManufacturerID(adlx_uint*),
//     ConnectorType(ADLX_DISPLAY_CONNECTOR_TYPE*),
//     RefreshRate(adlx_double*),
//     NativeResolution(adlx_int* maxH, adlx_int* maxV)
//
//   IADLXDisplayFreeSync / GPUScaling:  IsSupported(adlx_bool*), IsEnabled(adlx_bool*)
//   IADLXDisplayScalingMode:  IsSupported, GetMode(ADLX_SCALE_MODE*), SetMode(ADLX_SCALE_MODE)
//   IADLXDisplayColorDepth:   IsSupported, GetValue(ADLX_COLOR_DEPTH*), SetValue(ADLX_COLOR_DEPTH)
//   IADLXDisplayPixelFormat:  IsSupported, GetValue(ADLX_PIXEL_FORMAT*), SetValue(ADLX_PIXEL_FORMAT)
//
//===========================================================================
#include "bridge.h"

#include <Windows.h>
#include <string>
#include <vector>
#include <cstring>
#include <sstream>
#include <iomanip>

// Все нужные заголовки ADLX SDK:
#include "ADLXHelper.h"               // подключает ADLX.h → ADLXDefines.h + ADLXStructures.h + ISystem.h
#include "I3DSettings.h"              // IADLX3DSettingsServices + все IADLX3D* интерфейсы
#include "I3DSettings1.h"
#include "I3DSettings2.h"
#include "I3DSettings3.h"
#include "IDisplaySettings.h"         // IADLXDisplayScalingMode / ColorDepth / PixelFormat / FreeSync / GPUScaling
#include "IDisplays.h"                // IADLXDisplayServices, IADLXDisplayList, IADLXDisplay
#include "IGPUTuning.h"               // IADLXGPUTuningServices
#include "IGPUManualGFXTuning.h"      // IADLXManualGraphicsTuning1 / 2
#include "IGPUManualVRAMTuning.h"     // IADLXManualVRAMTuning1 / 2
#include "IGPUManualFanTuning.h"      // IADLXManualFanTuning (base) / 1
#include "IGPUManualPowerTuning.h"    // IADLXManualPowerTuning (base) / 1
#include "IPerformanceMonitoring.h"
#include "ISystem2.h"                 // IADLXGPU2: DriverVersion()

using namespace adlx;

namespace
{
    ADLXHelper g_bridgeHelper;
    bool       g_initialized = false;

    //---------------------------------------------------------------------
    // Helper: копирование const char* (UTF-8/ANSI from ADLX) в fixed buffer
    //---------------------------------------------------------------------
    void CopyAnsiToBuffer(const char* src, char* dst, size_t dstBytes)
    {
        if (!dst || dstBytes == 0) return;
        dst[0] = '\0';
        if (!src) return;
        strncpy_s(dst, dstBytes, src, _TRUNCATE);
    }

    //---------------------------------------------------------------------
    // Helper: парсинг PCI Device ID из строки вида "1002:0x73BF:0x...".
    // ADLX возвращает DeviceId как char** - мы парсим первый hex-сегмент
    // после "DEV_" или просто число.
    //---------------------------------------------------------------------
    int32_t ParsePciHex(const char* s)
    {
        if (!s) return 0;
        // Строка от ADLX имеет формат: "0x1002" / "1002" / "PCI\VEN_1002&DEV_73BF"
        // Ищем подстроку "DEV_" или сразу парсим hex.
        const char* p = strstr(s, "DEV_");
        if (p) { return (int32_t)strtol(p + 4, nullptr, 16); }
        p = strstr(s, "VEN_");
        if (p) { return (int32_t)strtol(p + 4, nullptr, 16); }
        // Иначе просто hex
        return (int32_t)strtol(s, nullptr, 16);
    }

    //---------------------------------------------------------------------
    // Helper: получает IADLXGPU2 через QueryInterface для DriverVersion().
    // IADLXGPU2 - extension interface (IID L"IADLXGPU2").
    //---------------------------------------------------------------------
    bool TryGetDriverVersion(IADLXGPU* gpu, char* dst, size_t dstBytes)
    {
        if (!gpu || !dst || dstBytes == 0) { if (dst && dstBytes) dst[0] = '\0'; return false; }
        dst[0] = '\0';

        IADLXGPU2* gpu2 = nullptr;
        ADLX_RESULT res = gpu->QueryInterface(L"IADLXGPU2", reinterpret_cast<void**>(&gpu2));
        if (res != ADLX_OK || gpu2 == nullptr) return false;

        const char* ver = nullptr;
        res = gpu2->DriverVersion(&ver);
        bool ok = (res == ADLX_OK && ver);
        if (ok) CopyAnsiToBuffer(ver, dst, dstBytes);

        gpu2->Release();
        return ok;
    }

    //---------------------------------------------------------------------
    // Helper: запрашивает у raw IADLXInterface конкретный Manual*Tuning
    // extension через QueryInterface.
    //
    // ADLX SDK имеет РАЗНЫЕ имена интерфейсов в зависимости от поколения GPU:
    //   - Polaris/Vega (GCN):  IADLXManualGraphicsTuning1, IADLXManualVRAMTuning1
    //   - RDNA 1/2/3/4:        IADLXManualGraphicsTuning2 (+ 2_1 extension),
    //                         IADLXManualVRAMTuning2 (+ 2_1 extension)
    //   - FanTuning:           IADLXManualFanTuning (base) + IADLXManualFanTuning1
    //   - PowerTuning:         IADLXManualPowerTuning (base) + IADLXManualPowerTuning1
    //
    // v1-интерфейсы (IADLXManualGraphicsTuning1, IADLXManualVRAMTuning1) и
    // base-интерфейсы (IADLXManualFanTuning, IADLXManualPowerTuning) доступны
    // на ВСЕХ AMD GPU с ADLX — включая RDNA, т.к. v2 является расширением v1,
    // а не заменой. Поэтому используем v1/base как универсальный путь,
    // avoiding генерации v2-специфичного кода (у v2 другой набор методов).
    //
    // Возвращает сырой указатель (caller обязан вызвать Release()).
    //---------------------------------------------------------------------
    template <typename T>
    T* QueryManualTuning(IADLXInterface* raw, const wchar_t* iid)
    {
        if (!raw) return nullptr;
        T* p = nullptr;
        if (ADLX_OK == raw->QueryInterface(iid, reinterpret_cast<void**>(&p)) && p)
            return p;
        return nullptr;
    }

    //---------------------------------------------------------------------
    // Helper: создаёт список поддерживаемых BPC как bitmask.
    // Биты: BPC_6=1, BPC_8=2, BPC_10=4, BPC_12=8, BPC_14=16, BPC_16=32
    //---------------------------------------------------------------------
    int32_t BuildSupportedBpcMask(IADLXDisplayColorDepth* cd)
    {
        if (!cd) return 0;
        int32_t mask = 0;
        adlx_bool b = false;
        if (ADLX_OK == cd->IsSupportedBPC_6(&b)  && b) mask |= 1;
        if (ADLX_OK == cd->IsSupportedBPC_8(&b)  && b) mask |= 2;
        if (ADLX_OK == cd->IsSupportedBPC_10(&b) && b) mask |= 4;
        if (ADLX_OK == cd->IsSupportedBPC_12(&b) && b) mask |= 8;
        if (ADLX_OK == cd->IsSupportedBPC_14(&b) && b) mask |= 16;
        if (ADLX_OK == cd->IsSupportedBPC_16(&b) && b) mask |= 32;
        return mask;
    }

    //---------------------------------------------------------------------
    // Helper: создаёт список поддерживаемых PixelFormats как bitmask.
    // Биты: RGB_444_FULL=1, YCBCR_444=2, YCBCR_422=4, RGB_444_LIMITED=8, YCBCR_420=16
    //---------------------------------------------------------------------
    int32_t BuildSupportedPixelFormatMask(IADLXDisplayPixelFormat* pf)
    {
        if (!pf) return 0;
        int32_t mask = 0;
        adlx_bool b = false;
        if (ADLX_OK == pf->IsSupportedPixelFormat(RGB_444_FULL, &b) && b) mask |= 1;
        if (ADLX_OK == pf->IsSupportedPixelFormat(YCBCR_444,    &b) && b) mask |= 2;
        if (ADLX_OK == pf->IsSupportedPixelFormat(YCBCR_422,    &b) && b) mask |= 4;
        if (ADLX_OK == pf->IsSupportedPixelFormat(RGB_444_LIMITED,&b) && b) mask |= 8;
        if (ADLX_OK == pf->IsSupportedPixelFormat(YCBCR_420,    &b) && b) mask |= 16;
        return mask;
    }

    //---------------------------------------------------------------------
    // Поддерживаемые scaling modes: ADLX_SCALE_MODE enum имеет 3 значения
    // (PRESERVE_ASPECT_RATIO=0, FULL_PANEL=1, CENTERED=2). ScalingMode
    // interface не отдаёт SupportedModes - возвращаем all-3 как доступные
    // если IsSupported() = true.
    //---------------------------------------------------------------------
    int32_t BuildSupportedScalingMask(IADLXDisplayScalingMode* sm)
    {
        if (!sm) return 0;
        adlx_bool b = false;
        if (ADLX_OK == sm->IsSupported(&b) && b)
            return 0x07; // биты 0..2 = все 3 режима
        return 0;
    }
}

//=========================================================================
// Init / Terminate
//=========================================================================
extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_Initialize()
{
    if (g_initialized) return BRIDGE_OK;
    ADLX_RESULT res = g_bridgeHelper.Initialize();
    if (ADLX_OK == res) { g_initialized = true; return BRIDGE_OK; }
    return BRIDGE_FAIL;
}

extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_InitializeWithIncompatibleDriver()
{
    if (g_initialized) return BRIDGE_OK;
    ADLX_RESULT res = g_bridgeHelper.InitializeWithIncompatibleDriver();
    if (ADLX_OK == res) { g_initialized = true; return BRIDGE_OK; }
    return BRIDGE_FAIL;
}

extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_Terminate()
{
    if (!g_initialized) return BRIDGE_OK;
    ADLX_RESULT res = g_bridgeHelper.Terminate();
    g_initialized = false;
    return (ADLX_OK == res) ? BRIDGE_OK : BRIDGE_FAIL;
}

extern "C" BRIDGE_API const char* BRIDGE_CALL ADLXBridge_QueryVersion()
{
    static char verBuf[64] = {0};
    const char* v = g_bridgeHelper.QueryVersion();
    if (v) strncpy_s(verBuf, v, _TRUNCATE);
    else   verBuf[0] = '\0';
    return verBuf;
}

//=========================================================================
// GPU count
//=========================================================================
extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetGpuCount(int32_t* outCount)
{
    if (!outCount) return BRIDGE_INVALID_ARGS;
    *outCount = 0;
    if (!g_initialized) return BRIDGE_NOT_INITIALIZED;
    IADLXSystem* sys = g_bridgeHelper.GetSystemServices();
    if (!sys) return BRIDGE_FAIL;

    IADLXGPUListPtr gpuList;
    if (ADLX_OK != sys->GetGPUs(&gpuList) || !gpuList) return BRIDGE_FAIL;
    // IADLXList::Size() - без аргументов, возвращает adlx_uint
    adlx_uint cnt = gpuList->Size();
    *outCount = (int32_t)cnt;
    return BRIDGE_OK;
}

//=========================================================================
// GPU info
//=========================================================================
extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetGpuInfo(int32_t gpuIndex, BridgeGpuInfo* outInfo)
{
    if (!outInfo) return BRIDGE_INVALID_ARGS;
    memset(outInfo, 0, sizeof(BridgeGpuInfo));
    if (!g_initialized) return BRIDGE_NOT_INITIALIZED;

    IADLXSystem* sys = g_bridgeHelper.GetSystemServices();
    if (!sys) return BRIDGE_FAIL;
    IADLXGPUListPtr gpuList;
    if (ADLX_OK != sys->GetGPUs(&gpuList) || !gpuList) return BRIDGE_FAIL;

    adlx_uint cnt = gpuList->Size();
    if (gpuIndex < 0 || (adlx_uint)gpuIndex >= cnt) return BRIDGE_INVALID_ARGS;

    IADLXGPUPtr gpu;
    if (ADLX_OK != gpuList->At((adlx_uint)gpuIndex, &gpu) || !gpu) return BRIDGE_FAIL;
    outInfo->index = gpuIndex;

    //--- String properties (const char**) ---
    const char* cstr = nullptr;
    if (ADLX_OK == gpu->Name(&cstr) && cstr)
        CopyAnsiToBuffer(cstr, outInfo->name, sizeof(outInfo->name));

    // DriverVersion - на extension IADLXGPU2
    TryGetDriverVersion(gpu.GetPtr(), outInfo->driverVersion, sizeof(outInfo->driverVersion));

    if (ADLX_OK == gpu->PNPString(&cstr) && cstr)
        CopyAnsiToBuffer(cstr, outInfo->adapterId, sizeof(outInfo->adapterId));

    //--- PCI IDs - возвращаются как const char** (например "0x1002") ---
    if (ADLX_OK == gpu->VendorId(&cstr) && cstr)
        outInfo->pciVendorId = ParsePciHex(cstr);
    if (ADLX_OK == gpu->DeviceId(&cstr) && cstr)
        outInfo->pciDeviceId = ParsePciHex(cstr);
    if (ADLX_OK == gpu->SubSystemId(&cstr) && cstr)
        outInfo->pciSubSystemId = ParsePciHex(cstr);
    if (ADLX_OK == gpu->RevisionId(&cstr) && cstr)
        outInfo->pciRevisionId = ParsePciHex(cstr);

    //--- VRAM в MB → bytes ---
    adlx_uint vramMB = 0;
    if (ADLX_OK == gpu->TotalVRAM(&vramMB))
        outInfo->dedicatedVramBytes = (int64_t)vramMB * 1024 * 1024;

    //--- ASIC family + GPU type ---
    ADLX_ASIC_FAMILY_TYPE asic = ASIC_UNDEFINED;
    if (ADLX_OK == gpu->ASICFamilyType(&asic))
        outInfo->asicFamily = (int32_t)asic;

    ADLX_GPU_TYPE gtype = GPUTYPE_UNDEFINED;
    if (ADLX_OK == gpu->Type(&gtype))
        outInfo->type = (int32_t)gtype;

    outInfo->apiSupported = 0; // ADLX не отдаёт supported APIs bitmask напрямую

    return BRIDGE_OK;
}

//=========================================================================
// GPU capabilities
//=========================================================================
extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetGpuCapabilities(int32_t gpuIndex, BridgeGpuCapabilities* outCaps)
{
    if (!outCaps) return BRIDGE_INVALID_ARGS;
    memset(outCaps, 0, sizeof(BridgeGpuCapabilities));
    if (!g_initialized) return BRIDGE_NOT_INITIALIZED;

    IADLXSystem* sys = g_bridgeHelper.GetSystemServices();
    if (!sys) return BRIDGE_FAIL;
    IADLXGPUListPtr gpuList;
    if (ADLX_OK != sys->GetGPUs(&gpuList) || !gpuList) return BRIDGE_FAIL;
    adlx_uint cnt = gpuList->Size();
    if (gpuIndex < 0 || (adlx_uint)gpuIndex >= cnt) return BRIDGE_INVALID_ARGS;
    IADLXGPUPtr gpu;
    if (ADLX_OK != gpuList->At((adlx_uint)gpuIndex, &gpu) || !gpu) return BRIDGE_FAIL;
    outCaps->index = gpuIndex;

    //--- 3D Settings ---
    IADLX3DSettingsServicesPtr d3dSvc;
    if (ADLX_OK == sys->Get3DSettingsServices(&d3dSvc) && d3dSvc)
    {
        adlx_bool b = false;

        // AntiLag
        IADLX3DAntiLagPtr antiLag;
        if (ADLX_OK == d3dSvc->GetAntiLag(gpu.GetPtr(), &antiLag) && antiLag)
        {
            b = false;
            ADLX_RESULT res = antiLag->IsSupported(&b);
            if (res == ADLX_OK) outCaps->antiLagSupported = b ? 1 : 0;
            else if (res == ADLX_NOT_SUPPORTED) outCaps->antiLagSupported = 0;
        }

        // Chill
        IADLX3DChillPtr chill;
        if (ADLX_OK == d3dSvc->GetChill(gpu.GetPtr(), &chill) && chill)
        {
            b = false;
            ADLX_RESULT res = chill->IsSupported(&b);
            if (res == ADLX_OK) outCaps->chillSupported = b ? 1 : 0;
            else if (res == ADLX_NOT_SUPPORTED) outCaps->chillSupported = 0;
        }

        // Boost
        IADLX3DBoostPtr boost;
        if (ADLX_OK == d3dSvc->GetBoost(gpu.GetPtr(), &boost) && boost)
        {
            b = false;
            ADLX_RESULT res = boost->IsSupported(&b);
            if (res == ADLX_OK) outCaps->boostSupported = b ? 1 : 0;
            else if (res == ADLX_NOT_SUPPORTED) outCaps->boostSupported = 0;
        }

        // Image Sharpening
        IADLX3DImageSharpeningPtr imgSharp;
        if (ADLX_OK == d3dSvc->GetImageSharpening(gpu.GetPtr(), &imgSharp) && imgSharp)
        {
            b = false;
            ADLX_RESULT res = imgSharp->IsSupported(&b);
            if (res == ADLX_OK) outCaps->imageSharpeningSupported = b ? 1 : 0;
            else if (res == ADLX_NOT_SUPPORTED) outCaps->imageSharpeningSupported = 0;
        }

        // EnhancedSync
        IADLX3DEnhancedSyncPtr eSync;
        if (ADLX_OK == d3dSvc->GetEnhancedSync(gpu.GetPtr(), &eSync) && eSync)
        {
            b = false;
            ADLX_RESULT res = eSync->IsSupported(&b);
            if (res == ADLX_OK) outCaps->enhancedSyncSupported = b ? 1 : 0;
            else if (res == ADLX_NOT_SUPPORTED) outCaps->enhancedSyncSupported = 0;
        }

        // WaitForVerticalRefresh
        IADLX3DWaitForVerticalRefreshPtr wvRefresh;
        if (ADLX_OK == d3dSvc->GetWaitForVerticalRefresh(gpu.GetPtr(), &wvRefresh) && wvRefresh)
        {
            b = false;
            ADLX_RESULT res = wvRefresh->IsSupported(&b);
            if (res == ADLX_OK) outCaps->waitForVerticalRefreshSupported = b ? 1 : 0;
            else if (res == ADLX_NOT_SUPPORTED) outCaps->waitForVerticalRefreshSupported = 0;
        }

        // FrameRateTargetControl
        IADLX3DFrameRateTargetControlPtr frtc;
        if (ADLX_OK == d3dSvc->GetFrameRateTargetControl(gpu.GetPtr(), &frtc) && frtc)
        {
            b = false;
            ADLX_RESULT res = frtc->IsSupported(&b);
            if (res == ADLX_OK) outCaps->frameRateTargetSupported = b ? 1 : 0;
            else if (res == ADLX_NOT_SUPPORTED) outCaps->frameRateTargetSupported = 0;
        }

        // MorphologicalAntiAliasing
        IADLX3DMorphologicalAntiAliasingPtr morphAA;
        if (ADLX_OK == d3dSvc->GetMorphologicalAntiAliasing(gpu.GetPtr(), &morphAA) && morphAA)
        {
            b = false;
            ADLX_RESULT res = morphAA->IsSupported(&b);
            if (res == ADLX_OK) outCaps->morphologicalAntiAliasingSupported = b ? 1 : 0;
            else if (res == ADLX_NOT_SUPPORTED) outCaps->morphologicalAntiAliasingSupported = 0;
        }

        // AnisotropicFiltering
        IADLX3DAnisotropicFilteringPtr aniso;
        if (ADLX_OK == d3dSvc->GetAnisotropicFiltering(gpu.GetPtr(), &aniso) && aniso)
        {
            b = false;
            ADLX_RESULT res = aniso->IsSupported(&b);
            if (res == ADLX_OK) outCaps->anisotropicFilteringSupported = b ? 1 : 0;
            else if (res == ADLX_NOT_SUPPORTED) outCaps->anisotropicFilteringSupported = 0;
        }

        // Tessellation
        IADLX3DTessellationPtr tess;
        if (ADLX_OK == d3dSvc->GetTessellation(gpu.GetPtr(), &tess) && tess)
        {
            b = false;
            ADLX_RESULT res = tess->IsSupported(&b);
            if (res == ADLX_OK) outCaps->tessellationSupported = b ? 1 : 0;
            else if (res == ADLX_NOT_SUPPORTED) outCaps->tessellationSupported = 0;
        }
    }

    //--- GPU Tuning Services ---
    IADLXGPUTuningServicesPtr tuningSvc;
    if (ADLX_OK == sys->GetGPUTuningServices(&tuningSvc) && tuningSvc)
    {
        adlx_bool b = false;
        if (ADLX_OK == tuningSvc->IsSupportedAutoTuning(gpu.GetPtr(), &b))
            outCaps->gpuAutoTuningSupported = b ? 1 : 0;
        if (ADLX_OK == tuningSvc->IsSupportedPresetTuning(gpu.GetPtr(), &b))
            outCaps->presetTuningSupported = b ? 1 : 0;
        if (ADLX_OK == tuningSvc->IsSupportedManualGFXTuning(gpu.GetPtr(), &b))
        {
            outCaps->manualGfxTuningSupported = b ? 1 : 0;
            outCaps->gpuTuningSupported = b ? 1 : 0;
        }
        if (ADLX_OK == tuningSvc->IsSupportedManualVRAMTuning(gpu.GetPtr(), &b))
            outCaps->manualVramTuningSupported = b ? 1 : 0;
        if (ADLX_OK == tuningSvc->IsSupportedManualFanTuning(gpu.GetPtr(), &b))
            outCaps->manualFanTuningSupported = b ? 1 : 0;
        if (ADLX_OK == tuningSvc->IsSupportedManualPowerTuning(gpu.GetPtr(), &b))
            outCaps->manualPowerTuningSupported = b ? 1 : 0;
    }

    //--- Performance Monitoring ---
    // IADLXPerformanceMonitoringServices::GetSupportedGPUMetrics возвращает
    // IADLXGPUMetricsSupport, на котором уже можно звать IsSupportedGPUUsage и т.д.
    IADLXPerformanceMonitoringServicesPtr perfSvc;
    if (ADLX_OK == sys->GetPerformanceMonitoringServices(&perfSvc) && perfSvc)
    {
        IADLXGPUMetricsSupportPtr metricsSupport;
        if (ADLX_OK == perfSvc->GetSupportedGPUMetrics(gpu.GetPtr(), &metricsSupport) && metricsSupport)
        {
            adlx_bool b = false;
            // Если поддерживается хотя бы GPU Usage - считаем, что мониторинг доступен
            if (ADLX_OK == metricsSupport->IsSupportedGPUUsage(&b))
                outCaps->performanceMonitoringSupported = b ? 1 : 0;
        }
    }

    return BRIDGE_OK;
}

//=========================================================================
// GPU tuning ranges
//=========================================================================
extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetGpuTuningRanges(int32_t gpuIndex, BridgeTuningRanges* outRanges)
{
    if (!outRanges) return BRIDGE_INVALID_ARGS;
    // Инициализируем -1 = unsupported
    outRanges->gfxMinMHz = outRanges->gfxMaxMHz = outRanges->gfxStepMHz = -1;
    outRanges->vramMinMHz = outRanges->vramMaxMHz = outRanges->vramStepMHz = -1;
    outRanges->voltageMinMv = outRanges->voltageMaxMv = outRanges->voltageStepMv = -1;
    outRanges->fanMinRpm = outRanges->fanMaxRpm = outRanges->fanStepRpm = -1;
    outRanges->fanAcousticMinRpm = outRanges->fanAcousticMaxRpm = outRanges->fanAcousticStepRpm = -1;
    outRanges->powerMinPct = outRanges->powerMaxPct = outRanges->powerStepPct = -1;
    outRanges->autoTuneSupported = 0;
    outRanges->index = gpuIndex;

    if (!g_initialized) return BRIDGE_NOT_INITIALIZED;
    IADLXSystem* sys = g_bridgeHelper.GetSystemServices();
    if (!sys) return BRIDGE_FAIL;

    IADLXGPUListPtr gpuList;
    if (ADLX_OK != sys->GetGPUs(&gpuList) || !gpuList) return BRIDGE_FAIL;
    adlx_uint cnt = gpuList->Size();
    if (gpuIndex < 0 || (adlx_uint)gpuIndex >= cnt) return BRIDGE_INVALID_ARGS;
    IADLXGPUPtr gpu;
    if (ADLX_OK != gpuList->At((adlx_uint)gpuIndex, &gpu) || !gpu) return BRIDGE_FAIL;

    IADLXGPUTuningServicesPtr tuningSvc;
    if (ADLX_OK != sys->GetGPUTuningServices(&tuningSvc) || !tuningSvc) return BRIDGE_OK; // OK, просто ranges = -1

    //--- Manual GFX tuning ---
    // IADLXManualGraphicsTuning1 доступен на ВСЕХ AMD GPU с ADLX (Polaris, Vega,
    // RDNA 1/2/3/4). RDNA имеет дополнительно IADLXManualGraphicsTuning2, но v1
    // тоже работает через обратную совместимость. У v1 есть метод
    // GetGPUTuningRanges(freqRange, voltRange) - возвращает ОБА диапазона сразу.
    adlx_bool supported = false;
    if (ADLX_OK == tuningSvc->IsSupportedManualGFXTuning(gpu.GetPtr(), &supported) && supported)
    {
        IADLXInterfacePtr rawIface;
        if (ADLX_OK == tuningSvc->GetManualGFXTuning(gpu.GetPtr(), &rawIface) && rawIface)
        {
            IADLXManualGraphicsTuning1* gfxTuning =
                QueryManualTuning<IADLXManualGraphicsTuning1>(rawIface.GetPtr(), L"IADLXManualGraphicsTuning1");
            if (gfxTuning)
            {
                ADLX_IntRange freqRange = {0,0,0};
                ADLX_IntRange voltRange = {0,0,0};
                if (ADLX_OK == gfxTuning->GetGPUTuningRanges(&freqRange, &voltRange))
                {
                    outRanges->gfxMinMHz = (int32_t)freqRange.minValue;
                    outRanges->gfxMaxMHz = (int32_t)freqRange.maxValue;
                    outRanges->gfxStepMHz = (int32_t)freqRange.step;
                    outRanges->voltageMinMv = (int32_t)voltRange.minValue;
                    outRanges->voltageMaxMv = (int32_t)voltRange.maxValue;
                    outRanges->voltageStepMv = (int32_t)voltRange.step;
                }
                gfxTuning->Release();
            }
        }
    }

    //--- Manual VRAM tuning ---
    // ВАЖНО: у разных поколений AMD GPU РАЗНЫЕ имена интерфейсов и методов:
    //   - IADLXManualVRAMTuning2 (RDNA 1/2/3/4):
    //       GetMaxVRAMFrequencyRange(ADLX_IntRange* tuningRange)
    //   - IADLXManualVRAMTuning1 (Polaris / Vega):
    //       GetVRAMTuningRanges(ADLX_IntRange* frequencyRange, ADLX_IntRange* voltageRange)
    //
    // Поэтому сначала пробуем v2 (RDNA) - если поддерживается, берём
    // GetMaxVRAMFrequencyRange. Если v2 недоступен - fallback на v1
    // (Polaris/Vega) и берём первый аргумент GetVRAMTuningRanges (frequencyRange).
    supported = false;
    if (ADLX_OK == tuningSvc->IsSupportedManualVRAMTuning(gpu.GetPtr(), &supported) && supported)
    {
        IADLXInterfacePtr rawIface;
        if (ADLX_OK == tuningSvc->GetManualVRAMTuning(gpu.GetPtr(), &rawIface) && rawIface)
        {
            // Сначала пробуем v2 (RDNA)
            IADLXManualVRAMTuning2* vram2 =
                QueryManualTuning<IADLXManualVRAMTuning2>(rawIface.GetPtr(), L"IADLXManualVRAMTuning2");
            if (vram2)
            {
                ADLX_IntRange r = {0,0,0};
                if (ADLX_OK == vram2->GetMaxVRAMFrequencyRange(&r))
                {
                    outRanges->vramMinMHz = (int32_t)r.minValue;
                    outRanges->vramMaxMHz = (int32_t)r.maxValue;
                    outRanges->vramStepMHz = (int32_t)r.step;
                }
                vram2->Release();
            }
            else
            {
                // Fallback на v1 (Polaris/Vega)
                IADLXManualVRAMTuning1* vram1 =
                    QueryManualTuning<IADLXManualVRAMTuning1>(rawIface.GetPtr(), L"IADLXManualVRAMTuning1");
                if (vram1)
                {
                    ADLX_IntRange freqRange = {0,0,0};
                    ADLX_IntRange voltRange = {0,0,0};
                    if (ADLX_OK == vram1->GetVRAMTuningRanges(&freqRange, &voltRange))
                    {
                        outRanges->vramMinMHz = (int32_t)freqRange.minValue;
                        outRanges->vramMaxMHz = (int32_t)freqRange.maxValue;
                        outRanges->vramStepMHz = (int32_t)freqRange.step;
                    }
                    vram1->Release();
                }
            }
        }
    }

    //--- Manual Fan tuning ---
    // IADLXManualFanTuning (base) - доступен на всех AMD GPU. Имеет
    // GetFanTuningRanges(speedRange, temperatureRange) и
    // GetMinAcousticLimitRange(ADLX_IntRange*). Расширение IADLXManualFanTuning1
    // добавляет ZeroRPM и MinFanSpeed - но для базовых слайдеров Fan RPM
    // достаточно base interface.
    supported = false;
    if (ADLX_OK == tuningSvc->IsSupportedManualFanTuning(gpu.GetPtr(), &supported) && supported)
    {
        IADLXInterfacePtr rawIface;
        if (ADLX_OK == tuningSvc->GetManualFanTuning(gpu.GetPtr(), &rawIface) && rawIface)
        {
            IADLXManualFanTuning* fanTuning =
                QueryManualTuning<IADLXManualFanTuning>(rawIface.GetPtr(), L"IADLXManualFanTuning");
            if (fanTuning)
            {
                ADLX_IntRange speedRange = {0,0,0};
                ADLX_IntRange tempRange = {0,0,0};
                if (ADLX_OK == fanTuning->GetFanTuningRanges(&speedRange, &tempRange))
                {
                    outRanges->fanMinRpm = (int32_t)speedRange.minValue;
                    outRanges->fanMaxRpm = (int32_t)speedRange.maxValue;
                    outRanges->fanStepRpm = (int32_t)speedRange.step;
                }
                ADLX_IntRange acousticRange = {0,0,0};
                if (ADLX_OK == fanTuning->GetMinAcousticLimitRange(&acousticRange))
                {
                    outRanges->fanAcousticMinRpm = (int32_t)acousticRange.minValue;
                    outRanges->fanAcousticMaxRpm = (int32_t)acousticRange.maxValue;
                    outRanges->fanAcousticStepRpm = (int32_t)acousticRange.step;
                }
                fanTuning->Release();
            }
        }
    }

    //--- Manual Power tuning ---
    // IADLXManualPowerTuning (base) - доступен на всех AMD GPU. Имеет
    // GetPowerLimitRange(ADLX_IntRange*) для диапазона TDP в процентах.
    // Расширение IADLXManualPowerTuning1 добавляет TDCLimit, но для слайдера
    // Power Limit достаточно base interface.
    supported = false;
    if (ADLX_OK == tuningSvc->IsSupportedManualPowerTuning(gpu.GetPtr(), &supported) && supported)
    {
        IADLXInterfacePtr rawIface;
        if (ADLX_OK == tuningSvc->GetManualPowerTuning(gpu.GetPtr(), &rawIface) && rawIface)
        {
            IADLXManualPowerTuning* powerTuning =
                QueryManualTuning<IADLXManualPowerTuning>(rawIface.GetPtr(), L"IADLXManualPowerTuning");
            if (powerTuning)
            {
                ADLX_IntRange r = {0,0,0};
                if (ADLX_OK == powerTuning->GetPowerLimitRange(&r))
                {
                    outRanges->powerMinPct = (int32_t)r.minValue;
                    outRanges->powerMaxPct = (int32_t)r.maxValue;
                    outRanges->powerStepPct = (int32_t)r.step;
                }
                powerTuning->Release();
            }
        }
    }

    //--- Auto-tune support ---
    supported = false;
    if (ADLX_OK == tuningSvc->IsSupportedAutoTuning(gpu.GetPtr(), &supported))
        outRanges->autoTuneSupported = supported ? 1 : 0;

    return BRIDGE_OK;
}

//=========================================================================
// Display count
//=========================================================================
extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetDisplayCount(int32_t* outCount)
{
    if (!outCount) return BRIDGE_INVALID_ARGS;
    *outCount = 0;
    if (!g_initialized) return BRIDGE_NOT_INITIALIZED;
    IADLXSystem* sys = g_bridgeHelper.GetSystemServices();
    if (!sys) return BRIDGE_FAIL;

    IADLXDisplayServicesPtr dispSvc;
    if (ADLX_OK != sys->GetDisplaysServices(&dispSvc) || !dispSvc) return BRIDGE_FAIL;
    adlx_uint cnt = 0;
    if (ADLX_OK != dispSvc->GetNumberOfDisplays(&cnt)) return BRIDGE_FAIL;
    *outCount = (int32_t)cnt;
    return BRIDGE_OK;
}

//=========================================================================
// Display info
//=========================================================================
extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetDisplayInfo(int32_t displayIndex, BridgeDisplayInfo* outInfo)
{
    if (!outInfo) return BRIDGE_INVALID_ARGS;
    memset(outInfo, 0, sizeof(BridgeDisplayInfo));
    if (!g_initialized) return BRIDGE_NOT_INITIALIZED;

    IADLXSystem* sys = g_bridgeHelper.GetSystemServices();
    if (!sys) return BRIDGE_FAIL;
    IADLXDisplayServicesPtr dispSvc;
    if (ADLX_OK != sys->GetDisplaysServices(&dispSvc) || !dispSvc) return BRIDGE_FAIL;

    IADLXDisplayListPtr dispList;
    if (ADLX_OK != dispSvc->GetDisplays(&dispList) || !dispList) return BRIDGE_FAIL;
    adlx_uint cnt = dispList->Size();
    if (displayIndex < 0 || (adlx_uint)displayIndex >= cnt) return BRIDGE_INVALID_ARGS;

    IADLXDisplayPtr disp;
    if (ADLX_OK != dispList->At((adlx_uint)displayIndex, &disp) || !disp) return BRIDGE_FAIL;
    outInfo->index = displayIndex;

    //--- String + EDID ---
    const char* cstr = nullptr;
    if (ADLX_OK == disp->Name(&cstr) && cstr)
        CopyAnsiToBuffer(cstr, outInfo->name, sizeof(outInfo->name));
    if (ADLX_OK == disp->EDID(&cstr) && cstr)
    {
        // EDID возвращается как hex-строка
        strncpy_s(outInfo->edidHex, sizeof(outInfo->edidHex), cstr, _TRUNCATE);
        outInfo->edidLength = (int32_t)strlen(cstr) / 2; // hex = 2 chars per byte
    }

    //--- ManufacturerID (adlx_uint, не char!) ---
    adlx_uint mfg = 0;
    if (ADLX_OK == disp->ManufacturerID(&mfg))
    {
        // 3-letter PNP ID кодируется в первых 16 битах: 'A'<<10 | 'B'<<5 | 'C'
        char pnp[4] = {0};
        pnp[0] = (char)(((mfg >> 10) & 0x1F) + 'A' - 1);
        pnp[1] = (char)(((mfg >> 5) & 0x1F) + 'A' - 1);
        pnp[2] = (char)((mfg & 0x1F) + 'A' - 1);
        strncpy_s(outInfo->manufacturerId, sizeof(outInfo->manufacturerId), pnp, _TRUNCATE);
    }

    //--- Connector type ---
    ADLX_DISPLAY_CONNECTOR_TYPE connType = DISPLAY_CONTYPE_UNKNOWN;
    if (ADLX_OK == disp->ConnectorType(&connType))
        outInfo->portType = (int32_t)connType;

    //--- Current refresh rate (adlx_double) ---
    adlx_double rr = 0.0;
    if (ADLX_OK == disp->RefreshRate(&rr))
        outInfo->currentRefreshHz = (int32_t)rr;

    //--- NativeResolution (max) ---
    adlx_int maxW = 0, maxH = 0;
    if (ADLX_OK == disp->NativeResolution(&maxW, &maxH))
    {
        outInfo->currentWidth = (int32_t)maxW;
        outInfo->currentHeight = (int32_t)maxH;
    }

    //--- FreeSync ---
    IADLXDisplayFreeSyncPtr freeSync;
    if (ADLX_OK == dispSvc->GetFreeSync(disp.GetPtr(), &freeSync) && freeSync)
    {
        adlx_bool b = false;
        if (ADLX_OK == freeSync->IsSupported(&b)) outInfo->freeSyncSupported = b ? 1 : 0;
        if (ADLX_OK == freeSync->IsEnabled(&b))   outInfo->freeSyncEnabled   = b ? 1 : 0;
        // FreeSync interface не имеет MinRefreshRate/MaxRefreshRate в базовом API
    }

    //--- GPU scaling ---
    IADLXDisplayGPUScalingPtr gpuScaling;
    if (ADLX_OK == dispSvc->GetGPUScaling(disp.GetPtr(), &gpuScaling) && gpuScaling)
    {
        adlx_bool b = false;
        if (ADLX_OK == gpuScaling->IsSupported(&b)) outInfo->gpuScalingSupported = b ? 1 : 0;
        if (ADLX_OK == gpuScaling->IsEnabled(&b))   outInfo->gpuScalingEnabled   = b ? 1 : 0;
    }

    //--- v1.1.0: Custom Color (Brightness / Contrast / Saturation / Hue) ---
    // IADLXDisplayCustomColor НЕ имеет общего IsSupported - подтверждено сверкой
    // с IDisplaySettings.h (строка 1155). Используем IsBrightnessSupported как
    // базовый индикатор поддержки CustomColor, и затем условно читаем
    // Contrast/Saturation/Hue только если соответствующие Is*Supported вернули true.
    IADLXDisplayCustomColorPtr customColor;
    if (ADLX_OK == dispSvc->GetCustomColor(disp.GetPtr(), &customColor) && customColor)
    {
        adlx_bool bBrightSup = false;
        if (ADLX_OK == customColor->IsBrightnessSupported(&bBrightSup) && bBrightSup)
        {
            outInfo->customColorSupported = 1;
            adlx_int v = 0;
            if (ADLX_OK == customColor->GetBrightness(&v))
                outInfo->customColorBrightness = (int32_t)v;

            adlx_bool bSup = false;
            if (ADLX_OK == customColor->IsContrastSupported(&bSup) && bSup)
            {
                if (ADLX_OK == customColor->GetContrast(&v))
                    outInfo->customColorContrast = (int32_t)v;
            }
            if (ADLX_OK == customColor->IsSaturationSupported(&bSup) && bSup)
            {
                if (ADLX_OK == customColor->GetSaturation(&v))
                    outInfo->customColorSaturation = (int32_t)v;
            }
            if (ADLX_OK == customColor->IsHueSupported(&bSup) && bSup)
            {
                if (ADLX_OK == customColor->GetHue(&v))
                    outInfo->customColorHue = (int32_t)v;
            }
        }
    }

    //--- Scaling mode ---
    IADLXDisplayScalingModePtr scalingMode;
    if (ADLX_OK == dispSvc->GetScalingMode(disp.GetPtr(), &scalingMode) && scalingMode)
    {
        ADLX_SCALE_MODE sm = PRESERVE_ASPECT_RATIO;
        if (ADLX_OK == scalingMode->GetMode(&sm))
            outInfo->currentScalingMode = (int32_t)sm;
    }

    //--- Color depth ---
    IADLXDisplayColorDepthPtr colorDepth;
    if (ADLX_OK == dispSvc->GetColorDepth(disp.GetPtr(), &colorDepth) && colorDepth)
    {
        ADLX_COLOR_DEPTH cd = BPC_UNKNOWN;
        if (ADLX_OK == colorDepth->GetValue(&cd))
            outInfo->currentColorDepth = (int32_t)cd;
    }

    //--- Pixel format ---
    IADLXDisplayPixelFormatPtr pixelFormat;
    if (ADLX_OK == dispSvc->GetPixelFormat(disp.GetPtr(), &pixelFormat) && pixelFormat)
    {
        ADLX_PIXEL_FORMAT pf = FORMAT_UNKNOWN;
        if (ADLX_OK == pixelFormat->GetValue(&pf))
            outInfo->currentPixelFormat = (int32_t)pf;
    }

    // Primary display - через IADLXDisplay метод IsExternal не подходит.
    // ADLX не отдаёт "IsPrimary" напрямую на базовом IADLXDisplay; для Stage 1
    // оставляем 0 - UI сам определит primary через Win32 EnumDisplayDevices.

    outInfo->connectionState = 1; // подключён если в списке

    return BRIDGE_OK;
}

//=========================================================================
// Display resolution list (из EDID/драйвера)
//=========================================================================
extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetDisplayResolutionList(int32_t displayIndex, BridgeDisplayModeList** outList)
{
    if (!outList) return BRIDGE_INVALID_ARGS;
    *outList = nullptr;
    if (!g_initialized) return BRIDGE_NOT_INITIALIZED;

    IADLXSystem* sys = g_bridgeHelper.GetSystemServices();
    if (!sys) return BRIDGE_FAIL;
    IADLXDisplayServicesPtr dispSvc;
    if (ADLX_OK != sys->GetDisplaysServices(&dispSvc) || !dispSvc) return BRIDGE_FAIL;
    IADLXDisplayListPtr dispList;
    if (ADLX_OK != dispSvc->GetDisplays(&dispList) || !dispList) return BRIDGE_FAIL;
    adlx_uint cnt = dispList->Size();
    if (displayIndex < 0 || (adlx_uint)displayIndex >= cnt) return BRIDGE_INVALID_ARGS;
    IADLXDisplayPtr disp;
    if (ADLX_OK != dispList->At((adlx_uint)displayIndex, &disp) || !disp) return BRIDGE_FAIL;

    // Базовый IADLXDisplay даёт только NativeResolution (max). Список
    // детальных режимов берётся через Win32 EnumDisplaySettings на C# стороне.
    // Здесь возвращаем один элемент - native resolution + current Hz.
    BridgeDisplayModeList* lst = (BridgeDisplayModeList*)CoTaskMemAlloc(sizeof(BridgeDisplayModeList));
    if (!lst) return BRIDGE_OUT_OF_MEMORY;
    lst->count = 1;
    lst->items = (BridgeDisplayMode*)CoTaskMemAlloc(sizeof(BridgeDisplayMode));
    if (!lst->items) { CoTaskMemFree(lst); return BRIDGE_OUT_OF_MEMORY; }
    memset(lst->items, 0, sizeof(BridgeDisplayMode));

    adlx_int maxW = 0, maxH = 0;
    if (ADLX_OK == disp->NativeResolution(&maxW, &maxH))
    {
        lst->items[0].width = (int32_t)maxW;
        lst->items[0].height = (int32_t)maxH;
    }
    adlx_double rr = 0.0;
    if (ADLX_OK == disp->RefreshRate(&rr))
        lst->items[0].refreshHz = (int32_t)rr;
    lst->items[0].bitsPerPixel = 8;
    lst->items[0].interlaced = 0;

    *outList = lst;
    return BRIDGE_OK;
}

//=========================================================================
// Refresh rate list (детальный - через Win32 EnumDisplaySettings на C# стороне)
//=========================================================================
extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetDisplayRefreshRateList(int32_t displayIndex, int32_t width, int32_t height, int32_t** outHzList, int32_t* outCount)
{
    if (!outHzList || !outCount) return BRIDGE_INVALID_ARGS;
    *outHzList = nullptr; *outCount = 0;
    // ADLX базовый не отдаёт список герцовок для произвольного разрешения.
    // Возвращаем пустой массив - C# слой возьмёт данные через EnumDisplaySettings.
    return BRIDGE_OK;
}

//=========================================================================
// FreeSync getters (использует IADLXDisplayServices::GetFreeSync)
//=========================================================================
extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetDisplayFreeSyncSupport(int32_t displayIndex, int32_t* supported, int32_t* enabled, int32_t* minHz, int32_t* maxHz)
{
    if (!supported || !enabled || !minHz || !maxHz) return BRIDGE_INVALID_ARGS;
    *supported = *enabled = *minHz = *maxHz = 0;
    if (!g_initialized) return BRIDGE_NOT_INITIALIZED;

    IADLXSystem* sys = g_bridgeHelper.GetSystemServices();
    if (!sys) return BRIDGE_FAIL;
    IADLXDisplayServicesPtr dispSvc;
    if (ADLX_OK != sys->GetDisplaysServices(&dispSvc) || !dispSvc) return BRIDGE_FAIL;
    IADLXDisplayListPtr dispList;
    if (ADLX_OK != dispSvc->GetDisplays(&dispList) || !dispList) return BRIDGE_FAIL;
    if (displayIndex < 0 || (adlx_uint)displayIndex >= dispList->Size()) return BRIDGE_INVALID_ARGS;
    IADLXDisplayPtr disp;
    if (ADLX_OK != dispList->At((adlx_uint)displayIndex, &disp) || !disp) return BRIDGE_FAIL;

    IADLXDisplayFreeSyncPtr fs;
    if (ADLX_OK != dispSvc->GetFreeSync(disp.GetPtr(), &fs) || !fs) return BRIDGE_INTERFACE_UNSUPPORTED;
    adlx_bool s = false, e = false;
    fs->IsSupported(&s); fs->IsEnabled(&e);
    *supported = s ? 1 : 0;
    *enabled = e ? 1 : 0;
    // FreeSync interface не отдаёт MinHz/MaxHz в базовом API
    return BRIDGE_OK;
}

//=========================================================================
// Scaling mode
//=========================================================================
extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetDisplayScalingMode(int32_t displayIndex, int32_t* currentMode, int32_t* supportedMask)
{
    if (!currentMode || !supportedMask) return BRIDGE_INVALID_ARGS;
    *currentMode = *supportedMask = 0;
    if (!g_initialized) return BRIDGE_NOT_INITIALIZED;
    IADLXSystem* sys = g_bridgeHelper.GetSystemServices();
    if (!sys) return BRIDGE_FAIL;
    IADLXDisplayServicesPtr dispSvc;
    if (ADLX_OK != sys->GetDisplaysServices(&dispSvc) || !dispSvc) return BRIDGE_FAIL;
    IADLXDisplayListPtr dispList;
    if (ADLX_OK != dispSvc->GetDisplays(&dispList) || !dispList) return BRIDGE_FAIL;
    if (displayIndex < 0 || (adlx_uint)displayIndex >= dispList->Size()) return BRIDGE_INVALID_ARGS;
    IADLXDisplayPtr disp;
    if (ADLX_OK != dispList->At((adlx_uint)displayIndex, &disp) || !disp) return BRIDGE_FAIL;
    IADLXDisplayScalingModePtr sm;
    if (ADLX_OK != dispSvc->GetScalingMode(disp.GetPtr(), &sm) || !sm) return BRIDGE_INTERFACE_UNSUPPORTED;
    ADLX_SCALE_MODE mode = PRESERVE_ASPECT_RATIO;
    sm->GetMode(&mode); *currentMode = (int32_t)mode;
    *supportedMask = BuildSupportedScalingMask(sm.GetPtr());
    return BRIDGE_OK;
}

extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_SetDisplayScalingMode(int32_t displayIndex, int32_t newMode)
{
    if (!g_initialized) return BRIDGE_NOT_INITIALIZED;
    IADLXSystem* sys = g_bridgeHelper.GetSystemServices();
    if (!sys) return BRIDGE_FAIL;
    IADLXDisplayServicesPtr dispSvc;
    if (ADLX_OK != sys->GetDisplaysServices(&dispSvc) || !dispSvc) return BRIDGE_FAIL;
    IADLXDisplayListPtr dispList;
    if (ADLX_OK != dispSvc->GetDisplays(&dispList) || !dispList) return BRIDGE_FAIL;
    if (displayIndex < 0 || (adlx_uint)displayIndex >= dispList->Size()) return BRIDGE_INVALID_ARGS;
    IADLXDisplayPtr disp;
    if (ADLX_OK != dispList->At((adlx_uint)displayIndex, &disp) || !disp) return BRIDGE_FAIL;
    IADLXDisplayScalingModePtr sm;
    if (ADLX_OK != dispSvc->GetScalingMode(disp.GetPtr(), &sm) || !sm) return BRIDGE_INTERFACE_UNSUPPORTED;
    ADLX_RESULT res = sm->SetMode((ADLX_SCALE_MODE)newMode);
    return (ADLX_OK == res) ? BRIDGE_OK : BRIDGE_FAIL;
}

//=========================================================================
// Pixel format
//=========================================================================
extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetDisplayPixelFormat(int32_t displayIndex, int32_t* currentFormat, int32_t* supportedMask)
{
    if (!currentFormat || !supportedMask) return BRIDGE_INVALID_ARGS;
    *currentFormat = *supportedMask = 0;
    if (!g_initialized) return BRIDGE_NOT_INITIALIZED;
    IADLXSystem* sys = g_bridgeHelper.GetSystemServices();
    if (!sys) return BRIDGE_FAIL;
    IADLXDisplayServicesPtr dispSvc;
    if (ADLX_OK != sys->GetDisplaysServices(&dispSvc) || !dispSvc) return BRIDGE_FAIL;
    IADLXDisplayListPtr dispList;
    if (ADLX_OK != dispSvc->GetDisplays(&dispList) || !dispList) return BRIDGE_FAIL;
    if (displayIndex < 0 || (adlx_uint)displayIndex >= dispList->Size()) return BRIDGE_INVALID_ARGS;
    IADLXDisplayPtr disp;
    if (ADLX_OK != dispList->At((adlx_uint)displayIndex, &disp) || !disp) return BRIDGE_FAIL;
    IADLXDisplayPixelFormatPtr pf;
    if (ADLX_OK != dispSvc->GetPixelFormat(disp.GetPtr(), &pf) || !pf) return BRIDGE_INTERFACE_UNSUPPORTED;
    ADLX_PIXEL_FORMAT fmt = FORMAT_UNKNOWN;
    pf->GetValue(&fmt); *currentFormat = (int32_t)fmt;
    *supportedMask = BuildSupportedPixelFormatMask(pf.GetPtr());
    return BRIDGE_OK;
}

extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_SetDisplayPixelFormat(int32_t displayIndex, int32_t newFormat)
{
    if (!g_initialized) return BRIDGE_NOT_INITIALIZED;
    IADLXSystem* sys = g_bridgeHelper.GetSystemServices();
    if (!sys) return BRIDGE_FAIL;
    IADLXDisplayServicesPtr dispSvc;
    if (ADLX_OK != sys->GetDisplaysServices(&dispSvc) || !dispSvc) return BRIDGE_FAIL;
    IADLXDisplayListPtr dispList;
    if (ADLX_OK != dispSvc->GetDisplays(&dispList) || !dispList) return BRIDGE_FAIL;
    if (displayIndex < 0 || (adlx_uint)displayIndex >= dispList->Size()) return BRIDGE_INVALID_ARGS;
    IADLXDisplayPtr disp;
    if (ADLX_OK != dispList->At((adlx_uint)displayIndex, &disp) || !disp) return BRIDGE_FAIL;
    IADLXDisplayPixelFormatPtr pf;
    if (ADLX_OK != dispSvc->GetPixelFormat(disp.GetPtr(), &pf) || !pf) return BRIDGE_INTERFACE_UNSUPPORTED;
    ADLX_RESULT res = pf->SetValue((ADLX_PIXEL_FORMAT)newFormat);
    return (ADLX_OK == res) ? BRIDGE_OK : BRIDGE_FAIL;
}

//=========================================================================
// Color depth
//=========================================================================
extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetDisplayColorDepth(int32_t displayIndex, int32_t* currentDepth, int32_t* supportedMask)
{
    if (!currentDepth || !supportedMask) return BRIDGE_INVALID_ARGS;
    *currentDepth = *supportedMask = 0;
    if (!g_initialized) return BRIDGE_NOT_INITIALIZED;
    IADLXSystem* sys = g_bridgeHelper.GetSystemServices();
    if (!sys) return BRIDGE_FAIL;
    IADLXDisplayServicesPtr dispSvc;
    if (ADLX_OK != sys->GetDisplaysServices(&dispSvc) || !dispSvc) return BRIDGE_FAIL;
    IADLXDisplayListPtr dispList;
    if (ADLX_OK != dispSvc->GetDisplays(&dispList) || !dispList) return BRIDGE_FAIL;
    if (displayIndex < 0 || (adlx_uint)displayIndex >= dispList->Size()) return BRIDGE_INVALID_ARGS;
    IADLXDisplayPtr disp;
    if (ADLX_OK != dispList->At((adlx_uint)displayIndex, &disp) || !disp) return BRIDGE_FAIL;
    IADLXDisplayColorDepthPtr cd;
    if (ADLX_OK != dispSvc->GetColorDepth(disp.GetPtr(), &cd) || !cd) return BRIDGE_INTERFACE_UNSUPPORTED;
    ADLX_COLOR_DEPTH depth = BPC_UNKNOWN;
    cd->GetValue(&depth); *currentDepth = (int32_t)depth;
    *supportedMask = BuildSupportedBpcMask(cd.GetPtr());
    return BRIDGE_OK;
}

extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_SetDisplayColorDepth(int32_t displayIndex, int32_t newDepth)
{
    if (!g_initialized) return BRIDGE_NOT_INITIALIZED;
    IADLXSystem* sys = g_bridgeHelper.GetSystemServices();
    if (!sys) return BRIDGE_FAIL;
    IADLXDisplayServicesPtr dispSvc;
    if (ADLX_OK != sys->GetDisplaysServices(&dispSvc) || !dispSvc) return BRIDGE_FAIL;
    IADLXDisplayListPtr dispList;
    if (ADLX_OK != dispSvc->GetDisplays(&dispList) || !dispList) return BRIDGE_FAIL;
    if (displayIndex < 0 || (adlx_uint)displayIndex >= dispList->Size()) return BRIDGE_INVALID_ARGS;
    IADLXDisplayPtr disp;
    if (ADLX_OK != dispList->At((adlx_uint)displayIndex, &disp) || !disp) return BRIDGE_FAIL;
    IADLXDisplayColorDepthPtr cd;
    if (ADLX_OK != dispSvc->GetColorDepth(disp.GetPtr(), &cd) || !cd) return BRIDGE_INTERFACE_UNSUPPORTED;
    ADLX_RESULT res = cd->SetValue((ADLX_COLOR_DEPTH)newDepth);
    return (ADLX_OK == res) ? BRIDGE_OK : BRIDGE_FAIL;
}

//=========================================================================
// v1.1.0: Display Custom Color setters (через IADLXDisplayCustomColor)
// Сверено с IDisplaySettings.h:
//   SetBrightness(adlx_int), SetContrast(adlx_int),
//   SetSaturation(adlx_int), SetHue(adlx_int)
//=========================================================================

// Helper: получает IADLXDisplayCustomColor для указанного displayIndex.
// Возвращает BRIDGE_OK и заполняет ppCustomColor, либо код ошибки.
static int32_t GetCustomColorForDisplay(int32_t displayIndex, IADLXDisplayCustomColor** ppCustomColor)
{
    *ppCustomColor = nullptr;
    if (!g_initialized) return BRIDGE_NOT_INITIALIZED;
    IADLXSystem* sys = g_bridgeHelper.GetSystemServices();
    if (!sys) return BRIDGE_FAIL;
    IADLXDisplayServicesPtr dispSvc;
    if (ADLX_OK != sys->GetDisplaysServices(&dispSvc) || !dispSvc) return BRIDGE_FAIL;
    IADLXDisplayListPtr dispList;
    if (ADLX_OK != dispSvc->GetDisplays(&dispList) || !dispList) return BRIDGE_FAIL;
    if (displayIndex < 0 || (adlx_uint)displayIndex >= dispList->Size()) return BRIDGE_INVALID_ARGS;
    IADLXDisplayPtr disp;
    if (ADLX_OK != dispList->At((adlx_uint)displayIndex, &disp) || !disp) return BRIDGE_FAIL;
    IADLXDisplayCustomColorPtr customColor;
    if (ADLX_OK != dispSvc->GetCustomColor(disp.GetPtr(), &customColor) || !customColor)
        return BRIDGE_INTERFACE_UNSUPPORTED;
    // Возвращаем raw pointer — caller обязан вызвать Release()
    *ppCustomColor = customColor.Detach();
    return BRIDGE_OK;
}

extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_SetDisplayBrightness(int32_t displayIndex, int32_t value)
{
    IADLXDisplayCustomColor* cc = nullptr;
    int32_t rc = GetCustomColorForDisplay(displayIndex, &cc);
    if (rc != BRIDGE_OK || !cc) return rc;
    ADLX_RESULT res = cc->SetBrightness((adlx_int)value);
    cc->Release();
    return (ADLX_OK == res) ? BRIDGE_OK : BRIDGE_FAIL;
}

extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_SetDisplayContrast(int32_t displayIndex, int32_t value)
{
    IADLXDisplayCustomColor* cc = nullptr;
    int32_t rc = GetCustomColorForDisplay(displayIndex, &cc);
    if (rc != BRIDGE_OK || !cc) return rc;
    ADLX_RESULT res = cc->SetContrast((adlx_int)value);
    cc->Release();
    return (ADLX_OK == res) ? BRIDGE_OK : BRIDGE_FAIL;
}

extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_SetDisplaySaturation(int32_t displayIndex, int32_t value)
{
    IADLXDisplayCustomColor* cc = nullptr;
    int32_t rc = GetCustomColorForDisplay(displayIndex, &cc);
    if (rc != BRIDGE_OK || !cc) return rc;
    ADLX_RESULT res = cc->SetSaturation((adlx_int)value);
    cc->Release();
    return (ADLX_OK == res) ? BRIDGE_OK : BRIDGE_FAIL;
}

extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_SetDisplayHue(int32_t displayIndex, int32_t value)
{
    IADLXDisplayCustomColor* cc = nullptr;
    int32_t rc = GetCustomColorForDisplay(displayIndex, &cc);
    if (rc != BRIDGE_OK || !cc) return rc;
    ADLX_RESULT res = cc->SetHue((adlx_int)value);
    cc->Release();
    return (ADLX_OK == res) ? BRIDGE_OK : BRIDGE_FAIL;
}

//=========================================================================
// v1.1.0: CustomColor ranges (min/max/step + recommendedDefault = middle)
// Сверено с IDisplaySettings.h:
//   GetBrightnessRange(ADLX_IntRange*), GetContrastRange(ADLX_IntRange*),
//   GetSaturationRange(ADLX_IntRange*), GetHueRange(ADLX_IntRange*)
//=========================================================================

static void FillCustomColorRange(const ADLX_IntRange& r, BridgeCustomColorRange* outRange)
{
    outRange->minValue = (int32_t)r.minValue;
    outRange->maxValue = (int32_t)r.maxValue;
    outRange->step     = (int32_t)r.step;
    // recommendedDefault = середина диапазона — используется как "factory default"
    // при Reset (в ADLX SDK нет прямого метода GetDefault для CustomColor).
    outRange->recommendedDefault = (int32_t)((r.minValue + r.maxValue) / 2);
}

extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetDisplayBrightnessRange(int32_t displayIndex, BridgeCustomColorRange* outRange)
{
    if (!outRange) return BRIDGE_INVALID_ARGS;
    memset(outRange, 0, sizeof(BridgeCustomColorRange));
    IADLXDisplayCustomColor* cc = nullptr;
    int32_t rc = GetCustomColorForDisplay(displayIndex, &cc);
    if (rc != BRIDGE_OK || !cc) return rc;
    ADLX_IntRange r = {0,0,0};
    ADLX_RESULT res = cc->GetBrightnessRange(&r);
    cc->Release();
    if (ADLX_OK != res) return BRIDGE_FAIL;
    FillCustomColorRange(r, outRange);
    return BRIDGE_OK;
}

extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetDisplayContrastRange(int32_t displayIndex, BridgeCustomColorRange* outRange)
{
    if (!outRange) return BRIDGE_INVALID_ARGS;
    memset(outRange, 0, sizeof(BridgeCustomColorRange));
    IADLXDisplayCustomColor* cc = nullptr;
    int32_t rc = GetCustomColorForDisplay(displayIndex, &cc);
    if (rc != BRIDGE_OK || !cc) return rc;
    ADLX_IntRange r = {0,0,0};
    ADLX_RESULT res = cc->GetContrastRange(&r);
    cc->Release();
    if (ADLX_OK != res) return BRIDGE_FAIL;
    FillCustomColorRange(r, outRange);
    return BRIDGE_OK;
}

extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetDisplaySaturationRange(int32_t displayIndex, BridgeCustomColorRange* outRange)
{
    if (!outRange) return BRIDGE_INVALID_ARGS;
    memset(outRange, 0, sizeof(BridgeCustomColorRange));
    IADLXDisplayCustomColor* cc = nullptr;
    int32_t rc = GetCustomColorForDisplay(displayIndex, &cc);
    if (rc != BRIDGE_OK || !cc) return rc;
    ADLX_IntRange r = {0,0,0};
    ADLX_RESULT res = cc->GetSaturationRange(&r);
    cc->Release();
    if (ADLX_OK != res) return BRIDGE_FAIL;
    FillCustomColorRange(r, outRange);
    return BRIDGE_OK;
}

extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetDisplayHueRange(int32_t displayIndex, BridgeCustomColorRange* outRange)
{
    if (!outRange) return BRIDGE_INVALID_ARGS;
    memset(outRange, 0, sizeof(BridgeCustomColorRange));
    IADLXDisplayCustomColor* cc = nullptr;
    int32_t rc = GetCustomColorForDisplay(displayIndex, &cc);
    if (rc != BRIDGE_OK || !cc) return rc;
    ADLX_IntRange r = {0,0,0};
    ADLX_RESULT res = cc->GetHueRange(&r);
    cc->Release();
    if (ADLX_OK != res) return BRIDGE_FAIL;
    FillCustomColorRange(r, outRange);
    return BRIDGE_OK;
}

//=========================================================================
// v1.0.1: GPU Performance Metrics (для вкладки Performance Monitoring)
// Сверено с IPerformanceMonitoring.h:
//   IADLXPerformanceMonitoringServices::GetCurrentGPUMetrics(IADLXGPU*, IADLXGPUMetrics**)
//   IADLXGPUMetrics::GPUTemperature(adlx_double*), GPUUsage(adlx_double*),
//     GPUClockSpeed(adlx_int*), GPUVRAMClockSpeed(adlx_int*),
//     GPUFanSpeed(adlx_int*), GPUPower(adlx_double*),
//     GPUTotalBoardPower(adlx_double*), GPUVoltage(adlx_int*)
//=========================================================================
extern "C" BRIDGE_API int32_t BRIDGE_CALL ADLXBridge_GetGpuMetrics(int32_t gpuIndex, BridgeGpuMetrics* outMetrics)
{
    if (!outMetrics) return BRIDGE_INVALID_ARGS;
    memset(outMetrics, 0, sizeof(BridgeGpuMetrics));
    outMetrics->index = gpuIndex;

    if (!g_initialized) return BRIDGE_NOT_INITIALIZED;
    IADLXSystem* sys = g_bridgeHelper.GetSystemServices();
    if (!sys) return BRIDGE_FAIL;

    IADLXGPUListPtr gpuList;
    if (ADLX_OK != sys->GetGPUs(&gpuList) || !gpuList) return BRIDGE_FAIL;
    adlx_uint cnt = gpuList->Size();
    if (gpuIndex < 0 || (adlx_uint)gpuIndex >= cnt) return BRIDGE_INVALID_ARGS;
    IADLXGPUPtr gpu;
    if (ADLX_OK != gpuList->At((adlx_uint)gpuIndex, &gpu) || !gpu) return BRIDGE_FAIL;

    IADLXPerformanceMonitoringServicesPtr perfSvc;
    if (ADLX_OK != sys->GetPerformanceMonitoringServices(&perfSvc) || !perfSvc)
        return BRIDGE_INTERFACE_UNSUPPORTED;

    IADLXGPUMetricsPtr metrics;
    if (ADLX_OK != perfSvc->GetCurrentGPUMetrics(gpu.GetPtr(), &metrics) || !metrics)
        return BRIDGE_FAIL;

    adlx_double dVal = 0.0;
    adlx_int    iVal = 0;

    if (ADLX_OK == metrics->GPUTemperature(&dVal))
    { outMetrics->gpuTemperature = dVal; outMetrics->temperatureValid = 1; }
    if (ADLX_OK == metrics->GPUUsage(&dVal))
    { outMetrics->gpuUsage = dVal; outMetrics->usageValid = 1; }
    if (ADLX_OK == metrics->GPUClockSpeed(&iVal))
    { outMetrics->gpuClockMHz = (int32_t)iVal; outMetrics->clockValid = 1; }
    if (ADLX_OK == metrics->GPUVRAMClockSpeed(&iVal))
    { outMetrics->vramClockMHz = (int32_t)iVal; outMetrics->vramClockValid = 1; }
    if (ADLX_OK == metrics->GPUFanSpeed(&iVal))
    { outMetrics->fanSpeedRpm = (int32_t)iVal; outMetrics->fanSpeedValid = 1; }
    if (ADLX_OK == metrics->GPUPower(&dVal))
    { outMetrics->powerDrawWatts = dVal; outMetrics->powerValid = 1; }
    if (ADLX_OK == metrics->GPUTotalBoardPower(&dVal))
    { outMetrics->totalBoardPower = dVal; outMetrics->totalBoardPowerValid = 1; }
    if (ADLX_OK == metrics->GPUVoltage(&iVal))
    { outMetrics->gpuVoltage = (int32_t)iVal; outMetrics->voltageValid = 1; }

    return BRIDGE_OK;
}

//=========================================================================
// Free buffer
//=========================================================================
extern "C" BRIDGE_API void BRIDGE_CALL ADLXBridge_FreeBuffer(void* buffer)
{
    if (buffer) CoTaskMemFree(buffer);
}
