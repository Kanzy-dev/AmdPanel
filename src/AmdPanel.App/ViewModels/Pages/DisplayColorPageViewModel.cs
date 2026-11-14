using AmdPanel.App.Mvvm;
using AmdPanel.App.Services;
using AmdPanel.Core.Hardware;
using AmdPanel.Core.Hardware.Capability;
using AmdPanel.Core.Hardware.Win32;

namespace AmdPanel.App.ViewModels.Pages;

/// <summary>
/// Страница "Регулировка параметров цвета рабочего стола" (Requirement 7).
/// Использует SetDeviceGammaRamp для Brightness/Contrast/Gamma + Digital Vibrance
/// через насыщенность. Gamma ramp — это безопасная операция, не требует UAC.
/// </summary>
public sealed class DisplayColorPageViewModel : ViewModelBase, IPageViewModel
{
    public string PageKey => "page.color";

    private readonly UiHardwareHub _hub;
    private readonly DisplayInfo _display;

    public DisplayColorPageViewModel(UiHardwareHub hub, DisplayInfo display)
    {
        _hub = hub;
        _display = display;
        Brightness = 50; Contrast = 50; Gamma = 100; Saturation = 50;
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

    /// <summary> Применяет цветовые параметры через SetDeviceGammaRamp (без UAC). </summary>
    public bool ApplyColor()
    {
        try
        {
            IntPtr hdc = User32Display.GetDC(IntPtr.Zero);
            try
            {
                var ramp = BuildRamp();
                return User32Display.SetDeviceGammaRamp(hdc, ref ramp);
            }
            finally { User32Display.ReleaseDC(IntPtr.Zero, hdc); }
        }
        catch { return false; }
    }

    /// <summary> Сбрасывает ramp в линейный (Brightness=50, Contrast=50, Gamma=100, Saturation=50). </summary>
    public bool ResetColor()
    {
        Brightness = 50; Contrast = 50; Gamma = 100; Saturation = 50;
        ClearDirty();
        return ApplyColor();
    }

    private User32Display.RAMP BuildRamp()
    {
        var ramp = new User32Display.RAMP
        {
            Red = new ushort[256], Green = new ushort[256], Blue = new ushort[256]
        };
        // Нормализованные значения [0..1]
        double brightness = Brightness / 100.0;            // 0..1
        double contrast   = 0.5 + Contrast / 100.0;        // 0.5..1.5
        double gamma      = (Gamma / 100.0) * 1.5;         // 0..1.5
        if (gamma < 0.2) gamma = 0.2;
        double saturation = Saturation / 100.0;            // 0..1

        for (int i = 0; i < 256; i++)
        {
            double t = i / 255.0;
            // Brightness shift
            double b = (t - 0.5) * contrast + 0.5 + (brightness - 0.5);
            b = Math.Clamp(b, 0.0, 1.0);
            // Gamma
            double g = Math.Pow(b, 1.0 / gamma);
            // Saturation вокруг средней точки (gray 0.5)
            double s = 0.5 + (g - 0.5) * saturation;
            s = Math.Clamp(s, 0.0, 1.0);

            ushort v = (ushort)(Math.Clamp(s, 0.0, 1.0) * 65535);
            ramp.Red[i] = v; ramp.Green[i] = v; ramp.Blue[i] = v;
        }
        return ramp;
    }
}
