using AmdPanel.App.Mvvm;
using AmdPanel.App.Services;
using AmdPanel.Core.Hardware;
using AmdPanel.Core.Hardware.Capability;

namespace AmdPanel.App.ViewModels.Pages;

/// <summary> Страница "Настройка FreeSync". </summary>
public sealed class FreeSyncPageViewModel : ViewModelBase, IPageViewModel
{
    public string PageKey => "page.freesync";

    private readonly UiHardwareHub _hub;
    private readonly DisplayInfo _display;

    public FreeSyncPageViewModel(UiHardwareHub hub, DisplayInfo display)
    {
        _hub = hub;
        _display = display;
        FreeSyncEnabled = _display.FreeSyncEnabled;
    }

    public bool IsAvailable => _display.FreeSyncSupported;

    public string MonitorName => _display.Name;
    public string PortName => _display.PortType.ToString();
    public bool FreeSyncSupported => _display.FreeSyncSupported;
    public int FreeSyncMinHz => _display.FreeSyncMinHz;
    public int FreeSyncMaxHz => _display.FreeSyncMaxHz;

    private bool _freeSyncEnabled;
    public bool FreeSyncEnabled
    {
        get => _freeSyncEnabled;
        set => SetField(ref _freeSyncEnabled, value);
    }
}
