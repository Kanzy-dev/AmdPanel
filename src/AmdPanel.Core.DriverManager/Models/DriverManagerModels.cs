namespace AmdPanel.Core.DriverManager.Models;

/// <summary>
/// Архитектура GPU AMD, определяемая по PCI Device ID.
/// Используется для выбора ветки драйвера (Legacy / Current).
/// </summary>
public enum AmdGpuArchitecture
{
    Unknown,
    /// <summary> GCN 1-3: HD 7000 - R9 200/300 series (legacy, нет ADLX). </summary>
    SeaIslands,
    /// <summary> GCN 4: Radeon RX 400/500 series (Polaris). </summary>
    Polaris,
    /// <summary> GCN 5: Vega / Radeon VII. </summary>
    Vega,
    /// <summary> RDNA 1: RX 5000 series (Navi 10). </summary>
    Rdna1,
    /// <summary> RDNA 2: RX 6000 series (Navi 21/22/23/24). </summary>
    Rdna2,
    /// <summary> RDNA 3: RX 7000 series (Navi 31/32/33). </summary>
    Rdna3,
    /// <summary> RDNA 4: RX 8000 series (Navi 44/48). </summary>
    Rdna4,
}

/// <summary>
/// Категория ветки драйвера: Legacy (Polaris/Vega) или Current (RDNA).
/// AMD продолжает выпускать отдельные пакеты для старых GPU.
/// </summary>
public enum DriverBranch
{
    Unknown,
    Current,    // RDNA 1/2/3/4 → main Adrenalin branch
    Legacy,     // Polaris/Vega → "Polaris/Vega" legacy branch
}

/// <summary>
/// Информация о текущем установленном драйвере AMD.
/// </summary>
public sealed class DriverStatusInfo
{
    public string InstalledVersion { get; init; } = string.Empty;
    public string DriverStoreVersion { get; init; } = string.Empty;
    public string InfPath { get; init; } = string.Empty;
    public string PublishedInfName { get; init; } = string.Empty;  // oemXX.inf
    public DateTimeOffset? ReleaseDate { get; init; }
    public bool IsAdrenalinInstalled { get; init; }
    public bool IsDriverOnly { get; init; }
    public AmdGpuArchitecture DetectedArchitecture { get; init; }
    public DriverBranch Branch { get; init; }
    public string RegistryKeyPath { get; init; } = string.Empty;
    public string GpuModelName { get; init; } = string.Empty;
}

/// <summary>
/// Информация о доступном обновлении драйвера.
/// </summary>
public sealed class DriverUpdateInfo
{
    public string Version { get; init; } = string.Empty;
    public DateTimeOffset ReleaseDate { get; init; }
    public Uri DownloadUrl { get; init; } = new Uri("https://amd.com");
    public long SizeBytes { get; init; }
    public DriverBranch Branch { get; init; }
    public string ReleaseNotesUrl { get; init; } = string.Empty;
    public bool IsNewer { get; init; }
}

/// <summary>
/// Стадии процесса установки драйвера. Каждая стадия сигнализируется
/// через IDriverInstallProgress.ProgressChanged.
/// </summary>
public enum DriverInstallStage
{
    Idle,
    Downloading,
    DownloadComplete,
    Extracting7z,
    ExtractionComplete,
    LocatingInf,
    BackingUpProfiles,
    DeletingOldDriver,
    OldDriverDeleted,
    InstallingInf,
    VerifyingAdlxLibrary,
    Completed,
    Failed,
    Cancelled,
}

/// <summary> Событие прогресса установки. </summary>
public sealed class DriverInstallProgress
{
    public DriverInstallStage Stage { get; init; }
    public int Percent { get; init; }       // 0..100 (относительно всей установки)
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
