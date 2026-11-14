using AmdPanel.Core.DriverManager.Models;
using AmdPanel.Core.Hardware.Capability;

namespace AmdPanel.Core.DriverManager.Services;

/// <summary>
/// Определение архитектуры GPU по PCI Device ID.
/// Возвращает <see cref="AmdGpuArchitecture"/> и соответствующую ветку драйвера.
/// </summary>
/// <remarks>
/// Список DEV_ID основан на официальных спецификациях AMD. Polaris (GCN 4) и
/// Vega (GCN 5) сегодня поддерживаются "Legacy" веткой драйвера. RDNA 1-4
/// поддерживаются текущим Adrenalin.
/// </remarks>
public static class ArchitectureDetector
{
    /// <summary> Возвращает архитектуру по 16-битному PCI Device ID. </summary>
    public static AmdGpuArchitecture Detect(int pciDeviceId)
    {
        if (pciDeviceId <= 0) return AmdGpuArchitecture.Unknown;
        // Polaris (GCN 4): RX 400/500 series
        if (pciDeviceId >= 0x67C0 && pciDeviceId <= 0x67FF) return AmdGpuArchitecture.Polaris; // Polaris 10/20 (RX 470/480/570/580)
        if (pciDeviceId >= 0x6980 && pciDeviceId <= 0x699F) return AmdGpuArchitecture.Polaris; // Polaris 12 (RX 550)
        if (pciDeviceId >= 0x6DF0 && pciDeviceId <= 0x6DFF) return AmdGpuArchitecture.Polaris; // Polaris 11 (RX 560)

        // Vega (GCN 5): Vega 56/64, Radeon VII
        if (pciDeviceId == 0x687F) return AmdGpuArchitecture.Vega; // Vega 56/64
        if (pciDeviceId == 0x66AF) return AmdGpuArchitecture.Vega; // Vega 56 Nano
        if (pciDeviceId == 0x731F) return AmdGpuArchitecture.Vega; // Radeon VII

        // RDNA 1 (Navi 10): RX 5000 series
        if (pciDeviceId == 0x7310 || pciDeviceId == 0x7312 ||
            pciDeviceId == 0x7318 || pciDeviceId == 0x7319 ||
            pciDeviceId == 0x731F || pciDeviceId == 0x73BF)
            return AmdGpuArchitecture.Rdna1;

        // RDNA 2 (Navi 21/22/23/24): RX 6000 series
        if (pciDeviceId >= 0x73A0 && pciDeviceId <= 0x73FF) return AmdGpuArchitecture.Rdna2; // Navi 21 (RX 6800/6800 XT/6900 XT)
        if (pciDeviceId >= 0x73DF && pciDeviceId <= 0x73EF) return AmdGpuArchitecture.Rdna2; // Navi 22 (RX 6700/6750 XT)
        if (pciDeviceId >= 0x7420 && pciDeviceId <= 0x743F) return AmdGpuArchitecture.Rdna2; // Navi 23 (RX 6600/6650 XT)
        if (pciDeviceId >= 0x7480 && pciDeviceId <= 0x748F) return AmdGpuArchitecture.Rdna2; // Navi 24 (RX 6400/6500 XT)

        // RDNA 3 (Navi 31/32/33): RX 7000 series
        if (pciDeviceId >= 0x7440 && pciDeviceId <= 0x744C) return AmdGpuArchitecture.Rdna3; // Navi 31 (RX 7900)
        if (pciDeviceId >= 0x7480 && pciDeviceId <= 0x748F) return AmdGpuArchitecture.Rdna3; // Navi 32 (RX 7700/7800)
        if (pciDeviceId >= 0x7481 && pciDeviceId <= 0x7482) return AmdGpuArchitecture.Rdna3; // Navi 33 (RX 7600)

        // RDNA 4 (Navi 44/48): RX 8000 series
        if (pciDeviceId >= 0x6430 && pciDeviceId <= 0x645F) return AmdGpuArchitecture.Rdna4; // Navi 44
        if (pciDeviceId >= 0x6860 && pciDeviceId <= 0x686F) return AmdGpuArchitecture.Rdna4; // Navi 48

        // Sea Islands (GCN 1-3): HD 7000/R9 200/300
        if (pciDeviceId >= 0x6600 && pciDeviceId <= 0x66FF) return AmdGpuArchitecture.SeaIslands;
        if (pciDeviceId >= 0x6700 && pciDeviceId <= 0x67FF) return AmdGpuArchitecture.SeaIslands;

        return AmdGpuArchitecture.Unknown;
    }

    /// <summary> Возвращает ветку драйвера для архитектуры. </summary>
    public static DriverBranch GetBranch(AmdGpuArchitecture arch)
        => arch switch
        {
            AmdGpuArchitecture.Polaris or AmdGpuArchitecture.Vega or AmdGpuArchitecture.SeaIslands => DriverBranch.Legacy,
            AmdGpuArchitecture.Rdna1 or AmdGpuArchitecture.Rdna2 or AmdGpuArchitecture.Rdna3 or AmdGpuArchitecture.Rdna4 => DriverBranch.Current,
            _ => DriverBranch.Unknown,
        };

    /// <summary> Возвращает человекочитаемое имя архитектуры. </summary>
    public static string GetDisplayName(AmdGpuArchitecture arch)
        => arch switch
        {
            AmdGpuArchitecture.SeaIslands => "Sea Islands (GCN 1-3)",
            AmdGpuArchitecture.Polaris    => "Polaris (GCN 4)",
            AmdGpuArchitecture.Vega       => "Vega (GCN 5)",
            AmdGpuArchitecture.Rdna1      => "RDNA 1",
            AmdGpuArchitecture.Rdna2      => "RDNA 2",
            AmdGpuArchitecture.Rdna3      => "RDNA 3",
            AmdGpuArchitecture.Rdna4      => "RDNA 4",
            _ => "Unknown",
        };

    /// <summary> Проверяет, поддерживает ли архитектура ADLX (требуется для панели). </summary>
    public static bool SupportsAdlx(AmdGpuArchitecture arch)
        => arch is not (AmdGpuArchitecture.Unknown or AmdGpuArchitecture.SeaIslands);

    /// <summary> Возвращает архитектуру из GpuInfo из HardwareScanner. </summary>
    public static AmdGpuArchitecture DetectFromGpu(GpuInfo gpu)
        => Detect(gpu.PciDeviceId);
}
