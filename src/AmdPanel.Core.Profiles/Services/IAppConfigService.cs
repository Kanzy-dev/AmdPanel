using AmdPanel.Core.Profiles.Models;

namespace AmdPanel.Core.Profiles.Services;

/// <summary>
/// Интерфейс для сервиса управления конфигурацией приложения
/// </summary>
public interface IAppConfigService
{
    /// <summary>
    /// Сохранить настройки приложения
    /// </summary>
    void SaveAppSettings(AppSettings settings);
    
    /// <summary>
    /// Загрузить настройки приложения
    /// </summary>
    AppSettings LoadAppSettings();
    
    /// <summary>
    /// Сохранить менеджер профилей
    /// </summary>
    void SaveProfiles(ProfileManager profiles);
    
    /// <summary>
    /// Загрузить менеджер профилей
    /// </summary>
    ProfileManager LoadProfiles();
}