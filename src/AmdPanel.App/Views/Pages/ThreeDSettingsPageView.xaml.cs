using System.Windows;
using System.Windows.Controls;
using AmdPanel.App.ViewModels.Pages;

namespace AmdPanel.App.Views.Pages;

/// <summary>
/// Code-behind для 3D Settings страницы. Глобальные настройки биндятся напрямую
/// через DataContext; обработчики кликов в Program Settings tab работают через
/// ссылки на VM (поскольку кнопки +/- и Save требуют imperative логики).
/// </summary>
public partial class ThreeDSettingsPageView : UserControl
{
    public ThreeDSettingsPageView()
    {
        InitializeComponent();
    }

    private void OnAddApplicationClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is ThreeDSettingsPageViewModel vm)
        {
            if (!string.IsNullOrWhiteSpace(vm.NewProcessName))
            {
                vm.AddApplicationProfile(vm.NewProcessName);
                vm.NewProcessName = string.Empty;
            }
            else
            {
                // Browse for .exe через OpenFileDialog
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*",
                    Title = "Select game executable"
                };
                if (dlg.ShowDialog() == true)
                {
                    string fileName = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
                    vm.AddApplicationProfile(fileName, dlg.FileName);
                }
            }
        }
    }

    private void OnRemoveApplicationClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is ThreeDSettingsPageViewModel vm)
            vm.RemoveSelectedApplication();
    }

    private void OnSaveApplicationClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is ThreeDSettingsPageViewModel vm)
            vm.PersistSelectedApplication();
    }
}
