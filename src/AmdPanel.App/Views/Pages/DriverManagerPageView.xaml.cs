using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Globalization;
using AmdPanel.App.ViewModels;
using AmdPanel.App.ViewModels.Pages;

namespace AmdPanel.App.Views.Pages;

public partial class DriverManagerPageView : UserControl
{
    public DriverManagerPageView() { InitializeComponent(); }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this)?.DataContext is ViewModels.MainViewModel mvm)
            mvm.BrowseDriverFileCommand.Execute(null);
    }

    private void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this)?.DataContext is ViewModels.MainViewModel mvm)
            mvm.CheckDriverUpdatesCommand.Execute(null);
    }

    private void OnInstallClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this)?.DataContext is ViewModels.MainViewModel mvm)
            mvm.InstallDriverCommand.Execute(null);
    }
}

/// <summary>
/// Converter: bool IsDriverOnly → "Driver-Only (recommended)" / "Adrenalin (full)".
/// Используется в DriverManagerPageView для отображения текста режима установки.
/// </summary>
public sealed class DriverModeLabelConverter : IValueConverter
{
    public static readonly DriverModeLabelConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isDriverOnly = value is bool b && b;
        string key = isDriverOnly ? "header.driverOnly" : "header.adrenalinInstalled";
        var found = System.Windows.Application.Current?.TryFindResource(key);
        return found as string ?? (isDriverOnly ? "Driver-Only" : "Adrenalin");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
