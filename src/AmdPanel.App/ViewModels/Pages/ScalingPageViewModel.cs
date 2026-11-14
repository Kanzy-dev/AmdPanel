using System.Collections.ObjectModel;
using AmdPanel.App.Mvvm;
using AmdPanel.App.Services;
using AmdPanel.Core.Hardware;
using AmdPanel.Core.Hardware.Capability;

namespace AmdPanel.App.ViewModels.Pages;

/// <summary> Страница "Регулировка размера и положения рабочего стола". </summary>
public sealed class ScalingPageViewModel : ViewModelBase, IPageViewModel
{
    public string PageKey => "page.scaling";

    private readonly UiHardwareHub _hub;
    private readonly DisplayInfo _display;

    public ScalingPageViewModel(UiHardwareHub hub, DisplayInfo display)
    {
        _hub = hub;
        _display = display;
        ScalingModes.Add(new ScalingModeItem(ScalingMode.FullScreen, "scaling.fullscreen"));
        ScalingModes.Add(new ScalingModeItem(ScalingMode.AspectRatio, "scaling.aspectRatio"));
        ScalingModes.Add(new ScalingModeItem(ScalingMode.NoScaling, "scaling.noScaling"));
        ScalingModes.Add(new ScalingModeItem(ScalingMode.Centered, "scaling.centered"));
        SelectedScalingMode = ScalingModes.FirstOrDefault(x => x.Mode == _display.CurrentScalingMode)
                              ?? ScalingModes[0];

        ScalingDevices.Add("scalingDevice.gpu");
        ScalingDevices.Add("scalingDevice.display");
        SelectedScalingDevice = _display.GpuScalingEnabled ? 0 : 1;
    }

    public bool IsAvailable => _display.GpuScalingSupported;

    public string MonitorName => _display.Name;

    public ObservableCollection<ScalingModeItem> ScalingModes { get; } = new();
    public ObservableCollection<string> ScalingDevices { get; } = new();

    private ScalingModeItem? _selectedScalingMode;
    public ScalingModeItem? SelectedScalingMode
    {
        get => _selectedScalingMode;
        set => SetField(ref _selectedScalingMode, value);
    }

    private int _selectedScalingDevice;
    public int SelectedScalingDevice
    {
        get => _selectedScalingDevice;
        set => SetField(ref _selectedScalingDevice, value);
    }
}

public sealed record ScalingModeItem(ScalingMode Mode, string LabelKey);
