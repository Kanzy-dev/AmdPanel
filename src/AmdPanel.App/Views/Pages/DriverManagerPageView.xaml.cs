using System.Windows;
using System.Windows.Controls;
using AmdPanel.App.ViewModels;

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
