using AmdPanel.App.Mvvm;
using AmdPanel.App.Services;
using AmdPanel.Core.Hardware;
using AmdPanel.Core.Hardware.Capability;
using AmdPanel.Core.Hardware.Native;
using AmdPanel.Core.Hardware.Win32;

namespace AmdPanel.App.ViewModels.Pages;

/// <summary>
/// Страница "Регулировка параметров цвета рабочего стола".
/// </summary>
/// <remarks>
/// <para>
/// <b>v1.1.0 fix (#3)</b>: Полностью отключено двойное наложение гаммы через
/// Win32 SetDeviceGammaRamp для регулировок яркости/контраста/насыщенности.
/// Теперь используется ТОЛЬКО официальный ADLX
/// <c>IADLXDisplayCustomColor::SetBrightness/SetContrast/SetSaturation/SetHue</c>
/// через bridge.cpp — это гарантирует совместимость с AMD Adrenalin (изменения
/// сразу видны в Adrenalin и наоборот).
/// </para>
/// <para>
/// <b>Gamma</b>: остаётся как отдельный слайдер, который всё ещё использует
/// <c>SetDeviceGammaRamp</c> — это корректно, т.к. ADLX CustomColor не имеет
/// gamma-параметра. Гамма применяется отдельно от Brightness/Contrast/Saturation.
/// </para>
/// <para>
/// <b>Reset</b>: 1) применяет середину диапазона из ADLX GetBrightnessRange/
/// GetContrastRange/GetSaturationRange через Set*; 2) принудительно
/// восстанавливает линейную таблицу гаммы Windows (0..65535) через
/// SetDeviceGammaRamp — это гарантирует возврат экрана в норму.
/// </para>
/// </remarks>
public sealed class DisplayColorPageViewModel : ViewModelBase, IPageViewModel
{
    public string PageKey => "page.color";

    private readonly UiHardwareHub _hub;
    private readonly DisplayInfo _display;

    public DisplayColorPageViewModel(UiHardwareHub hub, DisplayInfo display)
    {
        _hub = hub;
        _display = display;

        // v1.1.0: Инициализация текущими значениями из AMD Adrenalin.
        // Если CustomColor не поддержан (значения -1), берём середину диапазона.
        int bright = display.CustomColorSupported && display.CustomColorBrightness >= 0
                     ? display.CustomColorBrightness : 50;
        int contr = display.CustomColorSupported && display.CustomColorContrast >= 0
                    ? display.CustomColorContrast : 50;
        int sat   = display.CustomColorSupported && display.CustomColorSaturation >= 0
                    ? display.CustomColorSaturation : 50;

        // Прямое присваивание полям (не SetField) — не помечаем VM как Dirty
        _brightness = bright;
        _contrast   = contr;
        _gamma      = 100;
        _saturation = sat;
    }

    public bool IsAvailable => _display.Connected;

    private int _brightness;
    public int Brightness { get => _brightness; set => SetField(ref _brightness, value); }

    private int _contrast;
    public int Contrast { get => _contrast; set => SetField(ref _contrast, value); }

    private int _gamma;
    public int Gamma { get => _gamma; set => SetField(ref _gamma, value); }

    private int _saturation;
    public int Saturation { get => _saturation; set => SetField(ref _saturation, value); }

    public string MonitorName => _display.Name;

    /// <summary>
    /// v1.1.0: Применяет Brightness/Contrast/Saturation через ADLX Set*
    /// (без SetDeviceGammaRamp для этих параметров — избегаем двойного гамма-наложения).
    /// Gamma применяется отдельно через SetDeviceGammaRamp, т.к. в ADLX CustomColor
    /// нет gamma-параметра.
    /// </summary>
    public bool ApplyColor()
    {
        bool ok = true;

        // 1. ADLX Set* для Brightness / Contrast / Saturation
        // Вызываем только если значение изменилось от загруженного — иначе
        // лишние вызовы в драйвер. Здесь упрощённо вызываем всегда при Apply.
        try
        {
            int idx = _display.Index;
            int r1 = AdlxBridgeNative.ADLXBridge_SetDisplayBrightness(idx, _brightness);
            int r2 = AdlxBridgeNative.ADLXBridge_SetDisplayContrast(idx, _contrast);
            int r3 = AdlxBridgeNative.ADLXBridge_SetDisplaySaturation(idx, _saturation);
            ok = (r1 == AdlxBridgeNative.BRIDGE_OK) &&
                 (r2 == AdlxBridgeNative.BRIDGE_OK) &&
                 (r3 == AdlxBridgeNative.BRIDGE_OK);
        }
        catch { ok = false; }

        // 2. Gamma через SetDeviceGammaRamp — отдельно, т.к. ADLX CustomColor
        // не имеет параметра Gamma. Это НЕ двойное наложение, т.к. гамма
        // действует на LUT монитора отдельно от Brightness/Contrast драйвера.
        try
        {
            IntPtr hdc = User32Display.GetDC(IntPtr.Zero);
            try
            {
                var ramp = BuildGammaRamp();
                User32Display.SetDeviceGammaRamp(hdc, ref ramp);
            }
            finally { User32Display.ReleaseDC(IntPtr.Zero, hdc); }
        }
        catch { /* gamma - опциональная, не роняем ApplyColor если она не сработала */ }

        return ok;
    }

    /// <summary>
    /// v1.1.0: Надёжный сброс (Requirement 3):
    /// 1) Запрашиваем recommendedDefault из ADLX GetBrightnessRange / GetContrastRange /
    ///    GetSaturationRange (середина диапазона как "factory default");
    /// 2) Применяем эти дефолты через ADLX SetBrightness/SetContrast/SetSaturation;
    /// 3) Принудительно восстанавливаем линейную гамма-таблицу Windows (0..65535)
    ///    через SetDeviceGammaRamp — гарантирует, что экран вернётся в норму и
    ///    корректно дружит с кнопкой Reset в Adrenalin.
    /// </summary>
    public bool ResetColor()
    {
        // 1. Получаем recommendedDefault из ADLX диапазонов (середина min..max)
        int defBright = 50, defContr = 50, defSat = 50;
        try
        {
            int idx = _display.Index;
            if (AdlxBridgeNative.ADLXBridge_GetDisplayBrightnessRange(idx, out var brRange) == AdlxBridgeNative.BRIDGE_OK)
                defBright = brRange.RecommendedDefault > 0 ? brRange.RecommendedDefault : 50;
            if (AdlxBridgeNative.ADLXBridge_GetDisplayContrastRange(idx, out var coRange) == AdlxBridgeNative.BRIDGE_OK)
                defContr = coRange.RecommendedDefault > 0 ? coRange.RecommendedDefault : 50;
            if (AdlxBridgeNative.ADLXBridge_GetDisplaySaturationRange(idx, out var saRange) == AdlxBridgeNative.BRIDGE_OK)
                defSat = saRange.RecommendedDefault > 0 ? saRange.RecommendedDefault : 50;
        }
        catch { /* fallback to 50/50/50 */ }

        // 2. Применяем дефолты через ADLX Set*
        try
        {
            int idx = _display.Index;
            AdlxBridgeNative.ADLXBridge_SetDisplayBrightness(idx, defBright);
            AdlxBridgeNative.ADLXBridge_SetDisplayContrast(idx, defContr);
            AdlxBridgeNative.ADLXBridge_SetDisplaySaturation(idx, defSat);
        }
        catch { /* ignore — продолжаем сброс гаммы в любом случае */ }

        // 3. Принудительная линейная гамма (Gamma=100 = linear ramp 0..65535)
        Brightness = defBright;
        Contrast   = defContr;
        Gamma      = 100;
        Saturation = defSat;

        try
        {
            IntPtr hdc = User32Display.GetDC(IntPtr.Zero);
            try
            {
                var linearRamp = BuildLinearRamp();
                User32Display.SetDeviceGammaRamp(hdc, ref linearRamp);
            }
            finally { User32Display.ReleaseDC(IntPtr.Zero, hdc); }
        }
        catch { /* ignore */ }

        ClearDirty();
        return true;
    }

    /// <summary>
    /// Gamma ramp только для параметра Gamma (не Brightness/Contrast/Saturation).
    /// Изменяется только кривая гамма, без сдвига/растяжения — избегаем
    /// двойного наложения с ADLX CustomColor.
    /// </summary>
    private User32Display.RAMP BuildGammaRamp()
    {
        var ramp = new User32Display.RAMP
        {
            Red = new ushort[256], Green = new ushort[256], Blue = new ushort[256]
        };
        double gamma = Math.Max(0.2, (Gamma / 100.0) * 1.5);
        for (int i = 0; i < 256; i++)
        {
            double t = i / 255.0;
            double g = Math.Pow(t, 1.0 / gamma);
            ushort v = (ushort)(Math.Clamp(g, 0.0, 1.0) * 65535);
            ramp.Red[i] = v; ramp.Green[i] = v; ramp.Blue[i] = v;
        }
        return ramp;
    }

    /// <summary>
    /// Линейная гамма-таблица Windows (identity ramp): каждый канал i → i*257.
    /// Используется при Reset для гарантированного возврата экрана в норму.
    /// </summary>
    private User32Display.RAMP BuildLinearRamp()
    {
        var ramp = new User32Display.RAMP
        {
            Red = new ushort[256], Green = new ushort[256], Blue = new ushort[256]
        };
        for (int i = 0; i < 256; i++)
        {
            // i * 257 = i * 0x101 — стандартная identity ramp (0→0, 255→65535)
            ushort v = (ushort)(i * 257);
            ramp.Red[i] = v; ramp.Green[i] = v; ramp.Blue[i] = v;
        }
        return ramp;
    }
}
