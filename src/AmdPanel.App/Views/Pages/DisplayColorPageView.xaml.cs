using System.Windows;
using System.Windows.Controls;
using AmdPanel.App.ViewModels.Pages;

namespace AmdPanel.App.Views.Pages;

public partial class DisplayColorPageView : UserControl
{
    public DisplayColorPageView() { InitializeComponent(); }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is DisplayColorPageViewModel vm)
            vm.ResetColor();
    }
}
