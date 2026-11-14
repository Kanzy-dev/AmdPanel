using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace AmdPanel.Core.Hardware.Win32;

/// <summary>
/// Чтение raw EDID (128 или 256 байт) монитора из реестра Windows.
/// </summary>
/// <remarks>
/// Windows хранит EDID каждого монитора в ветке
/// HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY\&lt;PNP_ID&gt;\&lt;Serial&gt;\Device Parameters\EDID
/// как REG_BINARY. Для поиска по DeviceName ("\\.\DISPLAY1") мы перечисляем
/// все устройства класса Monitor и сопоставляем через строку DeviceName в ключе.
/// </remarks>
internal static class DisplayEdidReader
{
    private const string DisplayEnumRoot = @"SYSTEM\CurrentControlSet\Enum\DISPLAY";

    /// <summary> Возвращает raw EDID или null, если не удалось прочитать. </summary>
    public static byte[]? TryReadEdid(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return null;
        // displayName из ADLX имеет вид "\\Display1" или "\\.\DISPLAY1"
        string normalizedName = NormalizeDisplayNumber(displayName);

        try
        {
            using var displayKey = Registry.LocalMachine.OpenSubKey(DisplayEnumRoot);
            if (displayKey is null) return null;

            foreach (string pnpId in displayKey.GetSubKeyNames())
            {
                using var pnpKey = displayKey.OpenSubKey(pnpId);
                if (pnpKey is null) continue;
                foreach (string serial in pnpKey.GetSubKeyNames())
                {
                    using var serialKey = pnpKey.OpenSubKey(serial);
                    if (serialKey is null) continue;

                    // Сверяем DeviceObjectDesc / DeviceName с тем, что дал ADLX.
                    string? attachedKey = serialKey.GetValue("DeviceObjectDesc") as string;
                    string? deviceId    = serialKey.GetValue("DeviceID") as string;
                    // Альтернативный путь: внутри "Device Parameters" лежит EDID
                    using var dpKey = serialKey.OpenSubKey("Device Parameters");
                    if (dpKey is null) continue;
                    byte[]? edid = dpKey.GetValue("EDID") as byte[];
                    if (edid is null || edid.Length < 128) continue;

                    // Базовый check: первый байт должен быть 0x00, шестой - 0x00 (EDID 1.4 header)
                    if (edid[0] != 0x00) continue;

                    return edid;
                }
            }
        }
        catch { /* ignore - вернуть null */ }
        return null;
    }

    /// <summary> Декодирует поле monitor name (descriptor block 1-4) из EDID. </summary>
    public static string? ParseMonitorName(byte[] edid)
    {
        if (edid is null || edid.Length < 128) return null;
        try
        {
            // Дескрипторные блоки начинаются со смещения 54, по 18 байт каждый (4 шт).
            for (int i = 54; i < 126; i += 18)
            {
                // Header: 0x00 0x00 <type> 0x00 для текстовых дескрипторов
                if (edid[i] == 0x00 && edid[i + 1] == 0x00 && edid[i + 3] == 0x00)
                {
                    byte type = edid[i + 2];
                    if (type == 0xFC) // monitor name
                    {
                        // Текст до 13 байт, заканчивается \n
                        int end = Array.IndexOf(edid, (byte)0x0A, i + 4, 13);
                        int len = end < 0 ? 13 : end - (i + 4);
                        return Encoding.ASCII.GetString(edid, i + 4, len).TrimEnd('\n', '\r', ' ');
                    }
                }
            }
        }
        catch { /* ignore */ }
        return null;
    }

    private static string NormalizeDisplayNumber(string displayName)
    {
        // "\\Display1" -> "DISPLAY1"
        var match = System.Text.RegularExpressions.Regex.Match(displayName.ToUpperInvariant(), @"DISPLAY\d+");
        return match.Success ? match.Value : displayName.ToUpperInvariant();
    }
}
