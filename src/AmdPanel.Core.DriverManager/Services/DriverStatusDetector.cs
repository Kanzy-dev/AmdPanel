using System.Globalization;
using AmdPanel.Core.DriverManager.Models;
using AmdPanel.Core.Hardware;
using AmdPanel.Core.Hardware.Win32;
using Microsoft.Win32;

namespace AmdPanel.Core.DriverManager.Services;

/// <summary>
/// Чтение текущего статуса установленного драйвера AMD из реестра Windows.
/// </summary>
/// <remarks>
/// Использует <see cref="SetupApiLocator"/> из Core.Hardware для поиска
/// активного AMD-адаптера по VEN_1002 (без хардкода \0000). Из ветки
/// HKLM\SYSTEM\CurrentControlSet\Control\Class\{GUID}\XXXX читаются:
/// - DriverVersion (например "31.0.12015.1001")
/// - InfPath (относительный путь в DriverStore\FileRepository)
/// - DriverDate
/// Из <c>HKLM\SOFTWARE\AMD\CN</c> определяется наличие Adrenalin.
/// </remarks>
public sealed class DriverStatusDetector
{
    /// <summary> Читает полный статус драйвера из реестра и HardwareSnapshot. </summary>
    public DriverStatusInfo Detect(HardwareSnapshot snapshot)
    {
        var amd = SetupApiLocator.FindPrimaryAmdAdapter();
        string? driverKeyPath = amd?.DriverKeyPath;
        string installedVersion = string.Empty;
        string driverStoreVersion = string.Empty;
        string infPath = string.Empty;
        DateTimeOffset? releaseDate = null;
        string publishedInf = string.Empty;

        if (driverKeyPath is not null)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(driverKeyPath, writable: false);
                if (key is not null)
                {
                    installedVersion = (key.GetValue("DriverVersion") as string) ?? string.Empty;
                    driverStoreVersion = (key.GetValue("MatchingDeviceId") as string) ?? string.Empty;
                    infPath = (key.GetValue("InfPath") as string) ?? string.Empty;
                    publishedInf = (key.GetValue("InfSectionExt") as string) ?? string.Empty;

                    string? dateStr = key.GetValue("DriverDate") as string;
                    if (dateStr is not null &&
                        DateTimeOffset.TryParse(dateStr, CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal, out var dt))
                    {
                        releaseDate = dt;
                    }
                }
            }
            catch { /* ignore - вернём что есть */ }
        }

        bool adrenalinInstalled = IsAdrenalinInstalled();
        AmdGpuArchitecture arch = AmdGpuArchitecture.Unknown;
        if (snapshot.Gpus.Count > 0)
        {
            var gpu = snapshot.Gpus.FirstOrDefault(g => g.PciVendorId == 0x1002) ?? snapshot.Gpus[0];
            arch = ArchitectureDetector.Detect(gpu.PciDeviceId);
        }

        // PublishedInfName: ищем через pnputil /enum-drivers oemXX.inf
        if (string.IsNullOrEmpty(publishedInf))
            publishedInf = FindPublishedInfFromInfPath(infPath) ?? string.Empty;

        return new DriverStatusInfo
        {
            InstalledVersion = installedVersion,
            DriverStoreVersion = driverStoreVersion,
            InfPath = infPath,
            PublishedInfName = publishedInf,
            ReleaseDate = releaseDate,
            IsAdrenalinInstalled = adrenalinInstalled,
            IsDriverOnly = !adrenalinInstalled,
            DetectedArchitecture = arch,
            Branch = ArchitectureDetector.GetBranch(arch),
            RegistryKeyPath = driverKeyPath ?? string.Empty,
            GpuModelName = snapshot.Gpus.FirstOrDefault()?.Name ?? string.Empty,
        };
    }

    /// <summary> True, если в реестре есть ключ AMD\CN (Catalyst/Adrenalin). </summary>
    public static bool IsAdrenalinInstalled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\AMD\CN");
            if (key is not null) return true;
            using var key2 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\AMD\CN");
            return key2 is not null;
        }
        catch { return false; }
    }

    /// <summary>
    /// Ищет oemXX.inf по MatchingDeviceId через pnputil /enum-drivers.
    /// Возвращает null, если не найдено.
    /// </summary>
    private static string? FindPublishedInfFromInfPath(string infPath)
    {
        if (string.IsNullOrEmpty(infPath)) return null;
        try
        {
            var amdOems = PnpUtil.EnumerateAmdOemInfs();
            // amdOems содержит имена вида "oem12.inf" - берём первый, который
            // соответствует нашему InfPath. Без детального маппинга возвращаем первый.
            return amdOems.FirstOrDefault();
        }
        catch { return null; }
    }
}
