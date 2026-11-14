using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AmdPanel.Core.Hardware.Win32;

/// <summary>
/// Win32 User32 + Gdi32: ChangeDisplaySettingsEx, SetDeviceGammaRamp,
/// DEVMODE структуры и флаги. Используется для применения разрешения /
/// герцовки / bpc с возможностью отката, и для прямой установки gamma ramp.
/// </summary>
internal static class User32Display
{
    // === Constants ===
    public const int ENUM_CURRENT_SETTINGS  = -1;
    public const int ENUM_REGISTRY_SETTINGS = -2;

    public const uint CDS_FULLSCREEN        = 0x00000004;
    public const uint CDS_GLOBAL            = 0x00000008;
    public const uint CDS_NORESET           = 0x10000000;
    public const uint CDS_SET_PRIMARY       = 0x00000010;
    public const uint CDS_UPDATEREGISTRY    = 0x00000001;
    public const uint CDS_TEST              = 0x00000002;
    public const uint CDS_RESET             = 0x40000000;
    public const uint CDS_FORCE             = 0x80000000;

    public const int DISP_CHANGE_SUCCESSFUL      = 0;
    public const int DISP_CHANGE_RESTART         = 1;
    public const int DISP_CHANGE_BADMODE        = -2;
    public const int DISP_CHANGE_FAILED         = -1;
    public const int DISP_CHANGE_BADFLAGS       = -4;
    public const int DISP_CHANGE_NOTUPDATED      = -3;
    public const int DISP_CHANGE_BADPARAM       = -5;

    public const int DM_BITSPERPEL           = 0x00040000;
    public const int DM_PELSWIDTH            = 0x00080000;
    public const int DM_PELSHEIGHT           = 0x00100000;
    public const int DM_DISPLAYFREQUENCY     = 0x00400000;
    public const int DM_DISPLAYFLAGS          = 0x00200000;

    // === DEVMODE ===
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    // === Gamma Ramp ===
    [StructLayout(LayoutKind.Sequential)]
    public struct RAMP
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Red;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Green;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Blue;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int ChangeDisplaySettingsEx(
        string lpszDeviceName,
        ref DEVMODE lpDevMode,
        IntPtr hwnd,
        uint dwflags,
        IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int ChangeDisplaySettingsEx(
        string lpszDeviceName,
        IntPtr lpDevMode,
        IntPtr hwnd,
        uint dwflags,
        IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplaySettings(
        string lpszDeviceName,
        int iModeNum,
        ref DEVMODE lpDevMode);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplayDevices(
        IntPtr lpDevice,
        int iDevNum,
        ref DISPLAY_DEVICE lpDisplayDevice,
        uint dwFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern bool SetDeviceGammaRamp(IntPtr hdc, ref RAMP lpRamp);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern bool GetDeviceGammaRamp(IntPtr hdc, ref RAMP lpRamp);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SendMessageTimeout(
        IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    public const uint HWND_BROADCAST = 0xFFFF;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    /// <summary> Читает текущие настройки дисплея в виде DEVMODE. </summary>
    public static DEVMODE QueryCurrentMode(string deviceName)
    {
        var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "EnumDisplaySettings failed");
        return dm;
    }

    /// <summary>
    /// Применяет новый режим дисплея. Использует CDS_UPDATEREGISTRY | CDS_NORESET
    /// для накопления изменений в реестре, затем отдельный вызов с пустым
    /// DEVMODE и флагом 0 для действительного переключения (паттерн AMD/NVIDIA).
    /// </summary>
    public static int ApplyMode(string deviceName, DEVMODE newMode,
        bool persistToRegistry = true)
    {
        uint flags = persistToRegistry ? CDS_UPDATEREGISTRY | CDS_NORESET : CDS_NORESET;
        int rc = ChangeDisplaySettingsEx(deviceName, ref newMode, IntPtr.Zero, flags | CDS_TEST, IntPtr.Zero);
        if (rc != DISP_CHANGE_SUCCESSFUL) return rc;

        rc = ChangeDisplaySettingsEx(deviceName, ref newMode, IntPtr.Zero, flags, IntPtr.Zero);
        if (rc != DISP_CHANGE_SUCCESSFUL) return rc;

        // Apply: empty call to flush
        rc = ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        if (rc == DISP_CHANGE_SUCCESSFUL)
        {
            // Notify other apps
            SendMessageTimeout(new IntPtr(HWND_BROADCAST), WM_DISPLAYCHANGE,
                IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 1000, out _);
        }
        return rc;
    }

    /// <summary> Откат к предыдущему режиму: использует тот же механизм, что и Apply. </summary>
    public static int RevertTo(string deviceName, DEVMODE previousMode)
    {
        int rc = ChangeDisplaySettingsEx(deviceName, ref previousMode, IntPtr.Zero,
            CDS_UPDATEREGISTRY, IntPtr.Zero);
        if (rc == DISP_CHANGE_SUCCESSFUL)
        {
            SendMessageTimeout(new IntPtr(HWND_BROADCAST), WM_DISPLAYCHANGE,
                IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 1000, out _);
        }
        return rc;
    }
}
