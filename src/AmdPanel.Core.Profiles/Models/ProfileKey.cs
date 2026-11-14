using System.Security.Cryptography;
using System.Text;
using AmdPanel.Core.Hardware.Capability;

namespace AmdPanel.Core.Profiles.Models;

/// <summary>
/// Ключ профиля: детерминированный SHA256 от связки GPU PCI ID + EDID монитора.
/// Это позволяет разделить профили разных адаптеров и разных мониторов в одной системе.
/// </summary>
public static class ProfileKey
{
    /// <summary>
    /// Вычисляет ключ профиля для пары (GPU, активный монитор).
    /// Формат входа: "VEN_1002:DEV_73BF:SUBSYS_12345678|MON_PNP_ID|MON_SERIAL|EDID_HASH"
    /// </summary>
    public static string Compute(GpuInfo gpu, DisplayInfo display)
    {
        string edidHash = "none";
        if (display.RawEdid is { Length: > 0 } edid)
        {
            using var sha = SHA256.Create();
            edidHash = Convert.ToHexString(sha.ComputeHash(edid)).ToLowerInvariant();
        }
        string input =
            $"VEN_{gpu.PciVendorId:X4}:DEV_{gpu.PciDeviceId:X4}:SUBSYS_{gpu.PciSubSystemId:X4}|" +
            $"{display.ManufacturerId}|{display.ProductCode}|{display.SerialNumber}|{edidHash}";
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }

    /// <summary> Ключ для глобального профиля GPU (без привязки к конкретному монитору). </summary>
    public static string ComputeGpuOnly(GpuInfo gpu)
    {
        string input = $"VEN_{gpu.PciVendorId:X4}:DEV_{gpu.PciDeviceId:X4}:SUBSYS_{gpu.PciSubSystemId:X4}";
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }
}
