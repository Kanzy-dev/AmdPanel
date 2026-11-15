using System.Text.Json;
using AmdPanel.Core.Hardware;
using AmdPanel.Core.Profiles.Models;
using AmdPanel.Core.Profiles.Services;
using System.IO;
using System;
using Microsoft.Win32;

namespace AmdPanel.App.Services;

/// <summary>
/// Единый сервис управления конфигурацией приложения и профилями.
/// Сохраняет всё состояние в %AppData%\AmdPanel\
/// </summary>
public sealed class AppConfigService : IAppConfigService
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

