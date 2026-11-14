using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AmdPanel.Core.Hardware.Win32;

/// <summary>
/// Win32 SetupApi + Configuration Manager API: динамическое обнаружение
/// активного видеоадаптера AMD (VEN_1002) и вычисление точного пути к
/// его ветке драйвера в реестре
/// HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\XXXX.
/// </summary>
/// <remarks>
/// Никакого хардкода "\0000" - на многоадаптерных системах (iGPU AMD APU +
/// dGPU RDNA) индекс может быть любым. Класс перебирает все устройства
/// класса Display и выбирает первое активное с PCI Vendor ID 0x1002.
/// </remarks>
internal static class SetupApiLocator
{
    // GUID класса "Display" в Windows: {4d36e968-e325-11ce-bfc1-08002be10318}
    public static readonly Guid DisplayClassGuid =
        new(0x4d36e968, 0xe325, 0x11ce, 0xbf, 0xc1, 0x08, 0x00, 0x2b, 0xe1, 0x03, 0x18);

    private const uint DIGCF_PRESENT     = 0x00000002;
    private const uint DIGCF_DEVICEINTERFACE = 0x00000010;
    private const uint DIGCF_ALLCLASSES  = 0x00000004;

    private const int CM_DRP_HARDWAREID  = 0x00000002; // REG_MULTI_SZ
    private const int CM_DRP_DEVICEDESC  = 0x00000000; // REG_SZ

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator,
        IntPtr hwndParent, uint Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, int MemberIndex,
        ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr DeviceInfoSet,
        ref SP_DEVINFO_DATA DeviceInfoData, int Property,
        out int PropertyRegDataType, [Out] byte[] PropertyBuffer, int PropertyBufferSize,
        out int RequiredSize);

    [DllImport("cfgmgr32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int CM_Open_DevNode_Key(uint dnDevNode, uint samDesired,
        uint ulHardwareProfile, uint ulDisposition, out IntPtr phkDevice, uint ulFlags);

    [DllImport("cfgmgr32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Parent(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegCloseKey(IntPtr hKey);

    private const uint KEY_READ = 0x20019;
    private const uint CM_REGISTRY_HARDWARE = 0;
    private const uint CM_OPEN_REGISTRY_KEY = 0;

    /// <summary> Результат поиска ветки драйвера AMD в реестре. </summary>
    public sealed record AmdDriverRegistryPath(
        string DeviceDescription,
        string[] HardwareIds,
        string DriverKeyPath,   // напр. "HKLM\SYSTEM\...\Class\{GUID}\0001"
        string DriverInfPath,   // путь к .inf, если доступен
        int    PciDeviceId);

    /// <summary>
    /// Перебирает все присутствующие устройства класса Display, отбирает
    /// те, у кого HardwareID начинается с "PCI\VEN_1002", и для каждого
    /// возвращает полный путь к ветке драйвера.
    /// </summary>
    public static List<AmdDriverRegistryPath> EnumerateAmdDisplayDevices()
    {
        var result = new List<AmdDriverRegistryPath>();
        Guid cls = DisplayClassGuid;
        IntPtr hDevInfo = SetupDiGetClassDevs(ref cls, IntPtr.Zero, IntPtr.Zero,
            DIGCF_PRESENT);
        if (hDevInfo == new IntPtr(-1) || hDevInfo == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupDiGetClassDevs failed");

        try
        {
            var did = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
            for (int i = 0; SetupDiEnumDeviceInfo(hDevInfo, i, ref did); i++)
            {
                var hwIds = ReadMultiStringProperty(hDevInfo, ref did, CM_DRP_HARDWAREID);
                if (hwIds is null || hwIds.Count == 0) continue;
                // Нас интересует только AMD: ищем VEN_1002 в первой строке.
                string? firstAmd = hwIds.FirstOrDefault(s => s.IndexOf("VEN_1002", StringComparison.OrdinalIgnoreCase) >= 0);
                if (firstAmd is null) continue;

                int devId = ExtractPciDeviceId(firstAmd);
                string desc = ReadStringProperty(hDevInfo, ref did, CM_DRP_DEVICEDESC) ?? "AMD Display Adapter";
                string? driverKey = ReadDriverKeyName(hDevInfo, ref did);
                if (driverKey is null) continue;

                // Полный абсолютный путь для Registry.LocalMachine.OpenSubKey
                string absPath = "SYSTEM\\CurrentControlSet\\Control\\Class\\" + driverKey;

                result.Add(new AmdDriverRegistryPath(
                    desc,
                    hwIds.ToArray(),
                    absPath,
                    ReadInfPathFromRegistry(absPath),
                    devId));
            }
            return result;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(hDevInfo);
        }
    }

    /// <summary> Возвращает первое активное AMD-устройство класса Display. </summary>
    public static AmdDriverRegistryPath? FindPrimaryAmdAdapter()
        => EnumerateAmdDisplayDevices().FirstOrDefault();

    /// <summary>
    /// Читает DriverKeyName (SPDRP_DRIVER) через SetupDi - возвращает
    /// суффикс вида "{GUID}\0001", который присоединяется к базовому пути
    /// Control\Class для формирования полного ключа реестра драйвера.
    /// </summary>
    private static string? ReadDriverKeyName(IntPtr hDevInfo, ref SP_DEVINFO_DATA did)
    {
        // SPDRP_DRIVER = 0x00000009
        const int SPDRP_DRIVER = 0x00000009;
        int required;
        byte[] buf = new byte[512];
        if (!SetupDiGetDeviceRegistryProperty(hDevInfo, ref did, SPDRP_DRIVER,
            out _, buf, buf.Length, out required))
        {
            return null;
        }
        return ReadUtf16StringFromBuffer(buf);
    }

    private static List<string> ReadMultiStringProperty(IntPtr hDevInfo, ref SP_DEVINFO_DATA did, int property)
    {
        int required;
        SetupDiGetDeviceRegistryProperty(hDevInfo, ref did, property, out _, null, 0, out required);
        if (required <= 0) return new List<string>();
        byte[] buf = new byte[required];
        if (!SetupDiGetDeviceRegistryProperty(hDevInfo, ref did, property, out _, buf, buf.Length, out _))
            return new List<string>();
        // REG_MULTI_SZ = последовательность UTF-16 LE строк, заканчивающаяся двойным NUL.
        return SplitMultiSz(buf);
    }

    private static string? ReadStringProperty(IntPtr hDevInfo, ref SP_DEVINFO_DATA did, int property)
    {
        int required;
        SetupDiGetDeviceRegistryProperty(hDevInfo, ref did, property, out _, null, 0, out required);
        if (required <= 0) return null;
        byte[] buf = new byte[required];
        if (!SetupDiGetDeviceRegistryProperty(hDevInfo, ref did, property, out _, buf, buf.Length, out _))
            return null;
        return ReadUtf16StringFromBuffer(buf);
    }

    private static string? ReadInfPathFromRegistry(string absPath)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(absPath, writable: false);
            if (key is null) return null;
            // Ветвь драйвера хранит "InfPath" (абсолютный путь к .inf) и "InfSectionExt".
            string? infPath = key.GetValue("InfPath") as string;
            if (infPath is null) return null;
            if (!Path.IsPathRooted(infPath))
            {
                // Windows хранит путь относительно FileRepository
                string root = @"C:\Windows\System32\DriverStore\FileRepository\";
                infPath = Path.Combine(root, infPath);
            }
            return infPath;
        }
        catch { return null; }
    }

    private static int ExtractPciDeviceId(string hwId)
    {
        // Формат: "PCI\VEN_1002&DEV_73BF&SUBSYS_..."
        int idx = hwId.IndexOf("DEV_", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return 0;
        string sub = hwId.Substring(idx + 4);
        int end = sub.IndexOfAny(new[] { '&', '\\' });
        if (end > 0) sub = sub.Substring(0, end);
        return int.TryParse(sub, System.Globalization.NumberStyles.HexNumber, null, out int v) ? v : 0;
    }

    private static string ReadUtf16StringFromBuffer(byte[] buf)
    {
        // Обрезаем завершающие NUL-NUL.
        int end = buf.Length;
        while (end >= 2 && buf[end - 1] == 0 && buf[end - 2] == 0) end -= 2;
        return Encoding.Unicode.GetString(buf, 0, end);
    }

    private static List<string> SplitMultiSz(byte[] buf)
    {
        var list = new List<string>();
        int start = 0;
        for (int i = 0; i + 1 < buf.Length; i += 2)
        {
            if (buf[i] == 0 && buf[i + 1] == 0)
            {
                if (i > start)
                {
                    string s = Encoding.Unicode.GetString(buf, start, i - start);
                    if (s.Length > 0) list.Add(s);
                }
                start = i + 2;
            }
        }
        return list;
    }
}
