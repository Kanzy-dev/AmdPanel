namespace AmdPanel.Core.Profiles.Models;

/// <summary>
/// Полный набор 3D-настроек для глобального или программного профиля.
/// Сериализуется в JSON как часть GlobalProfile.ApplicationProfile.
/// </summary>
public sealed class ThreeDSettingsProfile
{
    public bool AntiLag { get; set; }
    public bool Chill { get; set; }
    public int  ChillMinFps { get; set; } = 72;
    public int  ChillMaxFps { get; set; } = 144;
    public bool ImageSharpening { get; set; }
    public int  Sharpness { get; set; } = 50;
    public bool Boost { get; set; }
    public int  VSyncMode { get; set; }        // 0=Off, 1=UnlessAppSpecifies, 2=On
    public int  AnisoLevel { get; set; }        // 0=Off, 1=2x, 2=4x, 3=8x, 4=16x
    public int  TessLevel { get; set; }         // 0=Optimized, 1..4=8x..64x

    public ThreeDSettingsProfile Clone() => new()
    {
        AntiLag = AntiLag, Chill = Chill, ChillMinFps = ChillMinFps, ChillMaxFps = ChillMaxFps,
        ImageSharpening = ImageSharpening, Sharpness = Sharpness, Boost = Boost,
        VSyncMode = VSyncMode, AnisoLevel = AnisoLevel, TessLevel = TessLevel,
    };

    public static ThreeDSettingsProfile Defaults() => new()
    {
        AntiLag = false, Chill = false, ImageSharpening = false, Boost = false,
        VSyncMode = 1, AnisoLevel = 0, TessLevel = 0, Sharpness = 50,
        ChillMinFps = 72, ChillMaxFps = 144
    };
}

/// <summary> Настройки разрешения и формата пикселей монитора. </summary>
public sealed class DisplaySettingsProfile
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int RefreshHz { get; set; }
    public int BitsPerPixel { get; set; } = 8;
    public int PixelFormat { get; set; }    // PixelFormat enum int

    public static DisplaySettingsProfile Defaults() => new() { Width = 0, Height = 0, RefreshHz = 0, BitsPerPixel = 8, PixelFormat = 1 };
}

/// <summary> Параметры цвета рабочего стола (gamma ramp). </summary>
public sealed class DesktopColorProfile
{
    public int Brightness { get; set; } = 50;
    public int Contrast { get; set; } = 50;
    public int Gamma { get; set; } = 100;
    public int Saturation { get; set; } = 50;

    public static DesktopColorProfile Defaults() => new();
}

/// <summary> Настройки FreeSync. </summary>
public sealed class FreeSyncProfile
{
    public bool Enabled { get; set; }
    public static FreeSyncProfile Defaults() => new();
}

/// <summary> Настройки масштабирования рабочего стола. </summary>
public sealed class ScalingProfile
{
    public int ScalingMode { get; set; }    // ScalingMode enum int
    public int ScalingDevice { get; set; }  // 0=GPU, 1=Display
    public static ScalingProfile Defaults() => new() { ScalingMode = 1, ScalingDevice = 1 };
}

/// <summary>
/// Мутация реестра HKLM для UMD-ключей AMD драйвера (тесселяция на уровне драйвера,
/// Frame Rate Target Control, общий профиль настроек Catalyst).
/// Применяется через RegistryApplier, который при отсутствии админ-прав
/// вызывает UacElevationService.
/// </summary>
public sealed class RegistryMutation
{
    /// <summary> Полный путь вида "SYSTEM\CurrentControlSet\Control\Class\{GUID}\0001\UMD". </summary>
    public string KeyPath { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;
    public string ValueKind { get; set; } = "String";  // String | DWord | QWord | Binary
    public string StringValue { get; set; } = string.Empty;
    public long?  DwordValue  { get; set; }
}

/// <summary>
/// Профиль приложения: привязка .exe → 3D-настройки.
/// Список хранится в GlobalProfile.ApplicationProfiles и используется
/// GameProfileWatcher для авто-применения при запуске игры.
/// </summary>
public sealed class ApplicationProfile
{
    /// <summary> Имя процесса без пути и без расширения, например "cyberpunk2077". </summary>
    public string ProcessName { get; set; } = string.Empty;
    /// <summary> Полный путь к .exe (опционально, для точной верификации). </summary>
    public string? ExecutablePath { get; set; }
    /// <summary> Отображаемое имя (берётся из FileVersionInfo или вводится вручную). </summary>
    public string DisplayName { get; set; } = string.Empty;
    public ThreeDSettingsProfile ThreeDSettings { get; set; } = new();
    public List<RegistryMutation> RegistryOverrides { get; set; } = new();

    /// <summary> true, если процесс совпадает по имени (case-insensitive). </summary>
    public bool Matches(string processName)
    {
        if (string.IsNullOrEmpty(ProcessName)) return false;
        return string.Equals(ProcessName, processName, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Корневой профиль для связки (GPU + Monitor) или GPU-only.
/// Хранится в %AppData%\AmdPanel\profiles\{ProfileKey}.json.
/// </summary>
public sealed class GlobalProfile
{
    public string ProfileKey { get; set; } = string.Empty;
    public string GpuName { get; set; } = string.Empty;
    public string MonitorName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ThreeDSettingsProfile ThreeDSettings { get; set; } = new();
    public DisplaySettingsProfile Display { get; set; } = new();
    public DesktopColorProfile DesktopColor { get; set; } = new();
    public FreeSyncProfile FreeSync { get; set; } = new();
    public ScalingProfile Scaling { get; set; } = new();

    /// <summary> UMD-настройки драйвера (HKLM\...\Class\{GUID}\XXXX\UMD). </summary>
    public List<RegistryMutation> DriverRegistry { get; set; } = new();

    /// <summary> Программные профили: один .exe → 3D override. </summary>
    public List<ApplicationProfile> ApplicationProfiles { get; set; } = new();

    public static GlobalProfile CreateDefault(string profileKey, string gpuName, string monitorName) => new()
    {
        ProfileKey = profileKey,
        GpuName = gpuName,
        MonitorName = monitorName,
        ThreeDSettings = ThreeDSettingsProfile.Defaults(),
        Display = DisplaySettingsProfile.Defaults(),
        DesktopColor = DesktopColorProfile.Defaults(),
        FreeSync = FreeSyncProfile.Defaults(),
        Scaling = ScalingProfile.Defaults()
    };
}
