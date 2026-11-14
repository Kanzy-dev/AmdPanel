using System.Diagnostics;
using System.Reflection;
using System.Windows;
using AmdPanel.App.Services;
using AmdPanel.Core.Hardware;

namespace AmdPanel.App.Views;

/// <summary>
/// Окно "О программе". Версия читается из AssemblyInformationalVersion
/// (атрибут генерируется из csproj &lt;Version&gt;1.0.0&lt;/Version&gt;),
/// поэтому всегда синхронизирована с релизной.
/// </summary>
public partial class AboutDialog : Window
{
    private readonly UiHardwareHub? _hub;

    public AboutDialog() : this(null) { }

    public AboutDialog(UiHardwareHub? hub)
    {
        InitializeComponent();
        _hub = hub;
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
}
