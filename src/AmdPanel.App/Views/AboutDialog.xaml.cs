using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Documents;
using AmdPanel.App.Services;
using AmdPanel.Core.Hardware;
using CommunityToolkit.Mvvm.Input;

namespace AmdPanel.App.Views;

/// <summary>
/// Окно "О программе". Версия читается из AssemblyInformationalVersion
/// (атрибут генерируется из csproj &lt;Version&gt;1.0.0&lt;/Version&gt;),
/// поэтому всегда синхронизирована с релизной.
/// </summary>
public partial class AboutDialog : Window
{
    private readonly UiHardwareHub? _hub;
    private readonly AppUpdateService? _updateService;

    public AboutDialog() : this(null) { }

    public AboutDialog(UiHardwareHub? hub, AppUpdateService? updateService = null)
    {
        InitializeComponent();
        _hub = hub;
        _updateService = updateService;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // AssemblyInformationalVersion = "1.0.0" из <Version>1.0.0</Version>
        var asm = Assembly.GetExecutingAssembly();
        var infoVersion = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
        var fileVersion = asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "1.0.0.0";
        VersionText.Text = $"{infoVersion} (build {fileVersion})";

        AdlxVersionText.Text = _hub?.Snapshot.AdlxVersion ?? "—";
        DriverVersionText.Text = _hub?.Snapshot.InstalledDriverVersion ?? "—";

        // Initialize update checking
        if (_updateService != null)
        {
            CheckForUpdatesButton.IsEnabled = true;
            CheckForUpdatesButton.Click += OnCheckForUpdatesClick;
            
            // Simulate async check after 3 seconds
            Task.Delay(3000).ContinueWith(_ =>
            {
                if (CheckForUpdatesButton.Dispatcher.CheckAccess())
                {
                    _updateService.CheckForUpdatesAsync().ContinueWith(t =>
                    {
                        if (CheckForUpdatesButton.Dispatcher.CheckAccess())
                        {
                            CheckForUpdatesButton.IsEnabled = true;
                        }
                    }, TaskScheduler.FromCurrentSynchronizationContext());
                }
            });
        }
    }

    private void OnRepoClick(object sender, RoutedEventArgs e)
    {
        if (sender is Hyperlink link && link.NavigateUri is Uri uri)
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
            }
            catch { /* ignore */ }
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnCheckForUpdatesClick(object sender, RoutedEventArgs e)
    {
        if (_updateService == null) return;
        
        CheckForUpdatesButton.IsEnabled = false;
        CheckForUpdatesButton.Content = "Checking...";
        
        _updateService.CheckForUpdatesAsync().ContinueWith(t =>
        {
            if (CheckForUpdatesButton.Dispatcher.CheckAccess())
            {
                if (_updateService.HasUpdate)
                {
                    var result = MessageBox.Show(
                        $"Доступно обновление до версии {_updateService.UpdateVersion}\n\n{_updateService.UpdateMessage}\n\nЗагрузить сейчас?",
                        "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        _updateService.OpenUpdatePage();
                    }
                }
                else
                {
                    MessageBox.Show(_updateService.UpdateMessage, "No Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                
                CheckForUpdatesButton.IsEnabled = true;
                CheckForUpdatesButton.Content = "Check for updates...";
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }
}
