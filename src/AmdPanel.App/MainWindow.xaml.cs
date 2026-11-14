using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AmdPanel.App.Services;
using AmdPanel.App.ViewModels;

namespace AmdPanel.App;

/// <summary>
/// Code-behind для MainWindow. Содержит только механику, которую
/// невозможно выразить чистым XAML DataBinding'ом:
/// - TreeView.SelectedItemChanged → пробрасывает в MainViewModel.SelectedNode
/// - Language ComboBox SelectionChanged → вызывает LanguageService.ApplyLanguage
/// - Application of UAC shield icon на кнопку Apply (через UacElevationService)
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // UAC shield: если текущий процесс не elevated, показываем щит рядом
        // с кнопкой Apply, чтобы пользователь знал о возможности требовать права.
        // На самом деле Apply для большинства параметров не требует UAC,
        // но для Driver Manager - требует. Shield на кнопке Apply - индикатор.
        if (!UacElevationService.IsRunningElevated)
        {
            // Можно добавить shield overlay; здесь оставлено как Ready-for-UI
            // extension point. Полная реализация: размещаем Image поверх кнопки
            // с Source = UacElevationService.GetShieldIcon(), Visibility
            // управляется через binding к Page.RequiresElevation.
        }

        // Локализация: подписываемся на изменение ComboBox
        if (LanguageCombo != null)
        {
            LanguageCombo.SelectionChanged += OnLanguageChanged;
            // Установка начального языка по культуре ОС
            string iso = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            LanguageCombo.SelectedIndex = iso == "ru" ? 1 : 0;
        }
    }

    private void OnNavSelectedChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel vm && e.NewValue is NavigationNode node)
        {
            vm.SelectedNode = node;
        }
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;
        if (DataContext is MainViewModel vm)
        {
            string lang = LanguageCombo.SelectedIndex == 1 ? "ru" : "en";
            vm.ChangeLanguageCommand.Execute(lang);
        }
    }

    /// <summary> Открывает модальное окно "О программе" с версией 1.0.0. </summary>
    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        UiHardwareHub? hub = null;
        if (Application.Current is App app)
        {
            // Пробуем получить hub через reflection-приватное поле; в простейшем
            // случае просто передаём null - AboutDialog покажет только версию сборки.
            var hubField = app.GetType().GetField("_hub",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            hub = hubField?.GetValue(app) as UiHardwareHub;
        }
        var dlg = new Views.AboutDialog(hub) { Owner = this };
        dlg.ShowDialog();
    }
}

/// <summary>
/// Converter: ключ строки интерфейса (например "nav.3dSettings") → локализованный текст.
/// Используется в HierarchicalDataTemplate для отображения названий навигационных узлов.
/// Реализация через DynamicResource lookup в Application.Current.Resources.
/// </summary>
public sealed class NavKeyToTextConverter : IValueConverter
{
    public static readonly NavKeyToTextConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter,
        System.Globalization.CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrEmpty(key)) return string.Empty;
        var found = Application.Current?.TryFindResource(key);
        return found as string ?? key;
    }

    public object ConvertBack(object value, Type targetType, object parameter,
        System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
