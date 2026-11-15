using System.Text.Json;
using AmdPanel.Core.Hardware;
using System.IO;
using System;
using Microsoft.Win32;

namespace AmdPanel.App.Services;

/// <summary>
/// Единый сервис управления конфигурацией приложения и профилями.
/// Сохраняет всё состояние в %AppData%\AmdPanel\
/// </summary>
public sealed class AppConfigService
{
    private readonly string _appDataPath;
    private readonly string _settingsPath;
    private readonly string _profilesPath;
    private readonly JsonSerializerOptions _jsonOptions;

    public AppConfigService()
    {
        _appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AmdPanel");
        Directory.CreateDirectory(_appDataPath);
        
        _settingsPath = Path.Combine(_appDataPath, "settings.json");
        _profilesPath = Path.Combine(_appDataPath, "profiles.json");
        
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
    }

    // ===== Настройки приложения =====
    
    public void SaveAppSettings(AppSettings settings)
    {
        var tempPath = _settingsPath + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, _jsonOptions));
            File.Replace(tempPath, _settingsPath, null);
        }
        catch
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }
    }

    public AppSettings LoadAppSettings()
    {
        if (!File.Exists(_settingsPath))
            return new AppSettings();
        
        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    // ===== Профили =====
    
    public void SaveProfiles(ProfileManager profiles)
    {
        var tempPath = _profilesPath + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(profiles, _jsonOptions));
            File.Replace(tempPath, _profilesPath, null);
        }
        catch
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }
    }

    public ProfileManager LoadProfiles()
    {
        if (!File.Exists(_profilesPath))
            return new ProfileManager();
        
        try
        {
            var json = File.ReadAllText(_profilesPath);
            return JsonSerializer.Deserialize<ProfileManager>(json, _jsonOptions) ?? new ProfileManager();
        }
        catch
        {
            return new ProfileManager();
        }
    }
}

/// <summary>
/// Полные настройки приложения
/// </summary>
public sealed record AppSettings
{
    public string Language { get; init; } = "ru"; // ru/en
    
    public string SelectedPageKey { get; init; } = "page.3dSettings";
    
    public int SelectedGpuIndex { get; init; } = 0;
    
    public int SelectedDisplayIndex { get; init; } = 0;
    
    public int SelectedWidth { get; init; } = 1920;
    
    public int SelectedHeight { get; init; } = 1080;
    
    public int SelectedRefreshRate { get; init; } = 60;
    
    public bool StartWithWindows { get; init; } = false;
    
    public bool MinimizeToTray { get; init; } = true;
    
    public bool IsFirstRun { get; init; } = true;

    public bool EnableDesktopContextMenu { get; init; } = false;
    
    public bool CheckForUpdates { get; init; } = true;
}

/// <summary>
/// Менеджер профилей
/// </summary>
public sealed record ProfileManager
{
    public ThreeDSettingsProfile GlobalProfile { get; init; } = new();
    
    public List<ApplicationProfileItem> Applications { get; init; } = new();
}

/// <summary>
/// Глобальный 3D профиль
/// </summary>
public sealed record ThreeDSettingsProfile
{
    public bool AntiLag { get; init; } = false;
    public bool Chill { get; init; } = false;
    public int ChillMinFps { get; init; } = 30;
    public int ChillMaxFps { get; init; } = 60;
    public bool ImageSharpening { get; init; } = false;
    public int Sharpness { get; init; } = 50;
    public bool Boost { get; init; } = false;
    public int VSyncMode { get; init; } = 1; // 0=Off, 1=App Controlled, 2=On
    public int AnisoLevel { get; init; } = 0; // 0=Off, 1=2x, 2=4x, 3=8x, 4=16x
    public int TessLevel { get; init; } = 0; // 0=Optimized, 1=8x, 2=16x, 3=32x, 4=64x
}

/// <summary>
/// Профиль для конкретного приложения
/// </summary>
public sealed record ApplicationProfileItem
{
    public string ExecutablePath { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool AntiLag { get; init; } = false;
    public bool Chill { get; init; } = false;
    public int ChillMinFps { get; init; } = 30;
    public int ChillMaxFps { get; init; } = 60;
    public bool ImageSharpening { get; init; } = false;
    public int Sharpness { get; init; } = 50;
    public bool Boost { get; init; } = false;
    public int VSyncMode { get; init; } = 1;
    public int AnisoLevel { get; init; } = 0;
    public int TessLevel { get; init; } = 0;
}