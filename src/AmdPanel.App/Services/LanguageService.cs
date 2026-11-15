using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace AmdPanel.App.Services;

/// <summary>
/// Управление локализацией UI.
/// </summary>
/// <remarks>
/// <para>
/// v1.1.0: Добавлена персистентная сохранялка выбранного языка в
/// <c>%AppData%\AmdPanel\settings.json</c>. При первом запуске язык определяется
/// по <c>CultureInfo.CurrentUICulture</c> (ru для русского, en для остальных).
/// При переключении языка в шапке окна выбор сохраняется в файл, и при
/// следующем запуске загружается оттуда — пользователь не вынужден каждый
/// раз переключать заново.
/// </para>
/// </remarks>
public sealed class LanguageService
{
    public const string English = "en";
    public const string Russian = "ru";

    private static readonly string[] Supported = { English, Russian };

    /// <summary> Путь к settings.json в %AppData%\AmdPanel\. </summary>
    private static string SettingsFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AmdPanel", "settings.json");

    public string CurrentLanguage { get; private set; } = English;

    public event EventHandler? LanguageChanged;

    /// <summary> Автовыбор языка по CultureInfo.InstalledUICulture. </summary>
    public static string DetectFromOs()
    {
        string iso = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Supported.Contains(iso) ? iso : English;
    }

    /// <summary>
    /// Инициализация при старте приложения: сначала пробуем загрузить
    /// сохранённый язык из settings.json, если файла нет — определяем по ОС.
    /// </summary>
    public void Initialize()
    {
        string saved = LoadSavedLanguage();
        ApplyLanguage(string.IsNullOrEmpty(saved) ? DetectFromOs() : saved);
    }

    public void ApplyLanguage(string language)
    {
        if (!Supported.Contains(language))
            language = English;
        if (language == CurrentLanguage && Application.Current.Resources.MergedDictionaries
            .Any(d => d.Source?.OriginalString.EndsWith($"Strings.{language}.xaml") == true))
            return;

        var appDictionaries = Application.Current.Resources.MergedDictionaries;
        // Удаляем предыдущий Strings.*.xaml словарь
        for (int i = appDictionaries.Count - 1; i >= 0; i--)
        {
            var src = appDictionaries[i].Source?.OriginalString;
            if (src is not null && src.Contains("Strings."))
                appDictionaries.RemoveAt(i);
        }
        var newDict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/AmdPanel;component/Themes/Strings.{language}.xaml", UriKind.Absolute)
        };
        appDictionaries.Add(newDict);

        CurrentLanguage = language;

        // v1.1.0: Сохраняем выбор в settings.json для следующего запуска
        SaveLanguage(language);

        // Меняем культуру для дат/чисел
        try
        {
            var ci = new CultureInfo(language);
            CultureInfo.DefaultThreadCurrentCulture = ci;
            CultureInfo.DefaultThreadCurrentUICulture = ci;
        }
        catch { /* ignore */ }

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    // =================================================================
    //  settings.json persistence
    // =================================================================

    /// <summary>
    /// Загружает сохранённый язык из %AppData%\AmdPanel\settings.json.
    /// Возвращает null если файл не существует или повреждён.
    /// </summary>
    private static string? LoadSavedLanguage()
    {
        try
        {
            if (!File.Exists(SettingsFilePath)) return null;
            string json = File.ReadAllText(SettingsFilePath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("language", out var langEl))
            {
                string? lang = langEl.GetString();
                if (!string.IsNullOrEmpty(lang) && Supported.Contains(lang))
                    return lang;
            }
        }
        catch { /* ignore — fallback to OS detection */ }
        return null;
    }

    /// <summary> Сохраняет выбранный язык в settings.json (atomic write через .tmp). </summary>
    private static void SaveLanguage(string language)
    {
        try
        {
            string dir = Path.GetDirectoryName(SettingsFilePath) ?? string.Empty;
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // Сохраняем как объект { "language": "en" } — формат легко расширяется
            // другими настройками (тема, polling interval и т.д.) в будущих версиях.
            var settings = new { language };
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            // Atomic write: .tmp → File.Replace (если основной файл уже существует,
            // старая версия сохраняется в .bak для восстановления при сбое).
            string tmpPath = SettingsFilePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            if (File.Exists(SettingsFilePath))
            {
                string bakPath = SettingsFilePath + ".bak";
                File.Replace(tmpPath, SettingsFilePath, bakPath, checkPath: false);
            }
            else
            {
                File.Move(tmpPath, SettingsFilePath, overwrite: false);
            }
        }
        catch
        {
            // Никогда не пробрасываем исключение из настроек языка
        }
    }
}
