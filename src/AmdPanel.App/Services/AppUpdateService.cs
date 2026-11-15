using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AmdPanel.App.Mvvm;
using AmdPanel.App.Services;
using CommunityToolkit.Mvvm.Input;

namespace AmdPanel.App.Services;

/// <summary>
/// Сервис автоматической проверки обновлений через GitHub Releases
/// </summary>
public sealed class AppUpdateService : ViewModelBase, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly AppConfigService _configService;
    private readonly string _repositoryUrl = "https://github.com/kanzy/amdpanel";
    private readonly string _apiUrl = "https://api.github.com/repos/kanzy/amdpanel/releases/latest";
    private bool _isChecking;
    private bool _hasUpdate;
    private string _updateVersion = string.Empty;
    private string _updateUrl = string.Empty;
    private string _updateMessage = string.Empty;

    public bool IsChecking
    {
        get => _isChecking;
        private set => SetField(ref _isChecking, value);
    }

    public bool HasUpdate
    {
        get => _hasUpdate;
        private set => SetField(ref _hasUpdate, value);
    }

    public string UpdateVersion
    {
        get => _updateVersion;
        private set => SetField(ref _updateVersion, value);
    }

    public string UpdateUrl
    {
        get => _updateUrl;
        private set => SetField(ref _updateUrl, value);
    }

    public string UpdateMessage
    {
        get => _updateMessage;
        private set => SetField(ref _updateMessage, value);
    }

    public AppUpdateService(AppConfigService configService)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("AmdPanel-App", "1.0.0"));
    }

    /// <summary>
    /// Проверяет наличие обновлений в фоновом режиме
    /// </summary>
    public async Task CheckForUpdatesAsync()
    {
        try
        {
            IsChecking = true;
            UpdateMessage = "Проверка обновлений...";

            var settings = _configService.LoadAppSettings();
            if (!settings.CheckForUpdates)
            {
                IsChecking = false;
                return;
            }

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
            if (currentVersion == null)
            {
                UpdateMessage = "Ошибка: не удалось определить текущую версию";
                IsChecking = false;
                return;
            }

            var response = await _httpClient.GetAsync(_apiUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var release = JsonDocument.Parse(json).RootElement;

            var latestVersion = release.GetProperty("tag_name").GetString();
            if (string.IsNullOrEmpty(latestVersion))
            {
                UpdateMessage = "Ошибка: не удалось определить версию релиза";
                IsChecking = false;
                return;
            }

            // Очистка версии от префиксов 'v' и '-beta'
            latestVersion = CleanVersion(latestVersion);
            var currentVersionStr = currentVersion.ToString();

            // Сравнение версий
            var versionCompare = CompareVersions(currentVersionStr, latestVersion);
            
            if (versionCompare < 0) // Текущая версия ниже новой
            {
                HasUpdate = true;
                UpdateVersion = latestVersion;
                
                // Находим URL установщика или архива
                var assets = release.GetProperty("assets");
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString();
                    if (name != null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        UpdateUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                        UpdateMessage = $"Доступна новая версия: {latestVersion}";
                        break;
                    }
                    else if (name != null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        UpdateUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                        UpdateMessage = $"Доступна новая версия: {latestVersion} (ZIP архив)";
                        break;
                    }
                }

                if (string.IsNullOrEmpty(UpdateUrl))
                {
                    UpdateUrl = release.GetProperty("html_url").GetString() ?? _repositoryUrl;
                    UpdateMessage = $"Доступна новая версия: {latestVersion} (страница релиза)";
                }
            }
            else
            {
                HasUpdate = false;
                UpdateMessage = "Используется последняя версия";
            }
        }
        catch (HttpRequestException ex)
        {
            UpdateMessage = $"Ошибка сети: {ex.Message}";
        }
        catch (JsonException ex)
        {
            UpdateMessage = $"Ошибка парсинга ответа: {ex.Message}";
        }
        catch (Exception ex)
        {
            UpdateMessage = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>
    /// Очистка строки версии от префиксов
    /// </summary>
    private static string CleanVersion(string version)
    {
        // Удаляем префикс 'v' и суффиксы типа '-beta'
        version = Regex.Replace(version, @"^v", "", RegexOptions.IgnoreCase);
        version = Regex.Replace(version, @"-.*$", "", RegexOptions.IgnoreCase);
        return version;
    }

    /// <summary>
    /// Сравнение версий в формате X.Y.Z
    /// </summary>
    private static int CompareVersions(string version1, string version2)
    {
        var v1 = version1.Split('.');
        var v2 = version2.Split('.');

        for (int i = 0; i < Math.Max(v1.Length, v2.Length); i++)
        {
            var part1 = i < v1.Length ? int.Parse(v1[i]) : 0;
            var part2 = i < v2.Length ? int.Parse(v2[i]) : 0;

            if (part1 < part2) return -1;
            if (part1 > part2) return 1;
        }

        return 0; // Версии равны
    }

    /// <summary>
    /// Открыть страницу загрузки обновления
    /// </summary>
    [RelayCommand]
    private void OpenUpdatePage()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = UpdateUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            UpdateMessage = $"Не удалось открыть страницу: {ex.Message}";
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}