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
//       IADLXGPUManualGFXTuning
//     GetManualVRAMTuning(IADLXGPU*, IADLXInterface**)
//     GetManualFanTuning(IADLXGPU*, IADLXInterface**)
//     GetManualPowerTuning(IADLXGPU*, IADLXInterface**)
//
//   IADLXGPUManualGFXTuning:
//     GetGPUTuningRanges(ADLX_IntRange* freqRange, ADLX_IntRange* voltRange)
//     GetGPUMinFrequencyRange(ADLX_IntRange*), GetGPUMaxFrequencyRange(ADLX_IntRange*)
//     GetGPUVoltageRange(ADLX_IntRange*)
//
//   IADLXGPUManualVRAMTuning:
//     GetMaxVRAMFrequencyRange(ADLX_IntRange*)
//
//   IADLXGPUManualFanTuning:
//     GetFanTuningRanges(ADLX_IntRange* speedRange, ADLX_IntRange* tempRange)
//     GetMinAcousticLimitRange(ADLX_IntRange*)
//
//   IADLXGPUManualPowerTuning:
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
#include "IGPUManualGFXTuning.h"      // IADLXGPUManualGFXTuning
#include "IGPUManualVRAMTuning.h"
#include "IGPUManualFanTuning.h"
#include "IGPUManualPowerTuning.h"
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
            if (ADLX_OK == antiLag->IsSupported(&b)) outCaps->antiLagSupported = b ? 1 : 0;

        // Chill
        IADLX3DChillPtr chill;
        if (ADLX_OK == d3dSvc->GetChill(gpu.GetPtr(), &chill) && chill)
            if (ADLX_OK == chill->IsSupported(&b)) outCaps->chillSupported = b ? 1 : 0;

        // Boost
        IADLX3DBoostPtr boost;
        if (ADLX_OK == d3dSvc->GetBoost(gpu.GetPtr(), &boost) && boost)
            if (ADLX_OK == boost->IsSupported(&b)) outCaps->boostSupported = b ? 1 : 0;

        // Image Sharpening
        IADLX3DImageSharpeningPtr imgSharp;
        if (ADLX_OK == d3dSvc->GetImageSharpening(gpu.GetPtr(), &imgSharp) && imgSharp)
            if (ADLX_OK == imgSharp->IsSupported(&b)) outCaps->imageSharpeningSupported = b ? 1 : 0;

        // EnhancedSync
        IADLX3DEnhancedSyncPtr eSync;
        if (ADLX_OK == d3dSvc->GetEnhancedSync(gpu.GetPtr(), &eSync) && eSync)
            if (ADLX_OK == eSync->IsSupported(&b)) outCaps->enhancedSyncSupported = b ? 1 : 0;

        // WaitForVerticalRefresh
        IADLX3DWaitForVerticalRefreshPtr wvRefresh;
        if (ADLX_OK == d3dSvc->GetWaitForVerticalRefresh(gpu.GetPtr(), &wvRefresh) && wvRefresh)
            if (ADLX_OK == wvRefresh->IsSupported(&b)) outCaps->waitForVerticalRefreshSupported = b ? 1 : 0;

        // FrameRateTargetControl
        IADLX3DFrameRateTargetControlPtr frtc;
        if (ADLX_OK == d3dSvc->GetFrameRateTargetControl(gpu.GetPtr(), &frtc) && frtc)
            if (ADLX_OK == frtc->IsSupported(&b)) outCaps->frameRateTargetSupported = b ? 1 : 0;

        // MorphologicalAntiAliasing
        IADLX3DMorphologicalAntiAliasingPtr morphAA;
        if (ADLX_OK == d3dSvc->GetMorphologicalAntiAliasing(gpu.GetPtr(), &morphAA) && morphAA)
            if (ADLX_OK == morphAA->IsSupported(&b)) outCaps->morphologicalAntiAliasingSupported = b ? 1 : 0;

        // AnisotropicFiltering
        IADLX3DAnisotropicFilteringPtr aniso;
        if (ADLX_OK == d3dSvc->GetAnisotropicFiltering(gpu.GetPtr(), &aniso) && aniso)
            if (ADLX_OK == aniso->IsSupported(&b)) outCaps->anisotropicFilteringSupported = b ? 1 : 0;

        // Tessellation
        IADLX3DTessellationPtr tess;
        if (ADLX_OK == d3dSvc->GetTessellation(gpu.GetPtr(), &tess) && tess)
            if (ADLX_OK == tess->IsSupported(&b)) outCaps->tessellationSupported = b ? 1 : 0;
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
    adlx_bool supported = false;
    if (ADLX_OK == tuningSvc->IsSupportedManualGFXTuning(gpu.GetPtr(), &supported) && supported)
    {
        IADLXInterfacePtr rawIface;
        if (ADLX_OK == tuningSvc->GetManualGFXTuning(gpu.GetPtr(), &rawIface) && rawIface)
        {
            IADLXGPUManualGFXTuning* gfxTuning = nullptr;
            if (ADLX_OK == rawIface->QueryInterface(L"IADLXGPUManualGFXTuning", reinterpret_cast<void**>(&gfxTuning)) && gfxTuning)
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
    supported = false;
    if (ADLX_OK == tuningSvc->IsSupportedManualVRAMTuning(gpu.GetPtr(), &supported) && supported)
    {
        IADLXInterfacePtr rawIface;
        if (ADLX_OK == tuningSvc->GetManualVRAMTuning(gpu.GetPtr(), &rawIface) && rawIface)
        {
            IADLXGPUManualVRAMTuning* vramTuning = nullptr;
            if (ADLX_OK == rawIface->QueryInterface(L"IADLXGPUManualVRAMTuning", reinterpret_cast<void**>(&vramTuning)) && vramTuning)
            {
                ADLX_IntRange r = {0,0,0};
                if (ADLX_OK == vramTuning->GetMaxVRAMFrequencyRange(&r))
                {
                    outRanges->vramMinMHz = (int32_t)r.minValue;
                    outRanges->vramMaxMHz = (int32_t)r.maxValue;
                    outRanges->vramStepMHz = (int32_t)r.step;
                }
                vramTuning->Release();
            }
        }
    }

    //--- Manual Fan tuning ---
    supported = false;
    if (ADLX_OK == tuningSvc->IsSupportedManualFanTuning(gpu.GetPtr(), &supported) && supported)
    {
        IADLXInterfacePtr rawIface;
        if (ADLX_OK == tuningSvc->GetManualFanTuning(gpu.GetPtr(), &rawIface) && rawIface)
        {
            IADLXGPUManualFanTuning* fanTuning = nullptr;
            if (ADLX_OK == rawIface->QueryInterface(L"IADLXGPUManualFanTuning", reinterpret_cast<void**>(&fanTuning)) && fanTuning)
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
    supported = false;
    if (ADLX_OK == tuningSvc->IsSupportedManualPowerTuning(gpu.GetPtr(), &supported) && supported)
    {
        IADLXInterfacePtr rawIface;
        if (ADLX_OK == tuningSvc->GetManualPowerTuning(gpu.GetPtr(), &rawIface) && rawIface)
        {
            IADLXGPUManualPowerTuning* powerTuning = nullptr;
            if (ADLX_OK == rawIface->QueryInterface(L"IADLXGPUManualPowerTuning", reinterpret_cast<void**>(&powerTuning)) && powerTuning)
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
// Free buffer
//=========================================================================
extern "C" BRIDGE_API void BRIDGE_CALL ADLXBridge_FreeBuffer(void* buffer)
{
    if (buffer) CoTaskMemFree(buffer);
}
