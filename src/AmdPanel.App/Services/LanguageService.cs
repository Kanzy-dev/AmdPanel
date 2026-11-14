using System.Globalization;
using System.Windows;

namespace AmdPanel.App.Services;

/// <summary>
/// Управление локализацией UI. Загружает Strings.{lang}.xaml словарь в
/// MergedDictionaries Application.Resources, выбирая язык автоматически
/// по культуре ОС, либо по явному выбору пользователя в Настройках.
/// Поддерживаются EN и RU. Выбор сохраняется в
/// %AppData%\AmdPanel\settings.json (Stage 3) - пока в памяти.
/// </summary>
public sealed class LanguageService
{
    public const string English = "en";
    public const string Russian = "ru";

    private static readonly string[] Supported = { English, Russian };

    public string CurrentLanguage { get; private set; } = English;

    public event EventHandler? LanguageChanged;

    /// <summary> Автовыбор языка по CultureInfo.InstalledUICulture. </summary>
    public static string DetectFromOs()
    {
        string iso = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Supported.Contains(iso) ? iso : English;
    }

    public void Initialize()
    {
        ApplyLanguage(DetectFromOs());
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
}
