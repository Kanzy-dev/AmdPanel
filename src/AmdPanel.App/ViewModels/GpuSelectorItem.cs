using System.Windows.Media;
using AmdPanel.App.Mvvm;
using AmdPanel.Core.Hardware.Capability;

namespace AmdPanel.App.ViewModels;

/// <summary>
/// Элемент селектора активного GPU в шапке окна (Requirement 1).
/// Содержит ссылку на GpuInfo и DisplayString вида "Radeon RX 6700 XT · 12 GB".
/// </summary>
public sealed class GpuSelectorItem : ViewModelBase
{
    public GpuInfo Gpu { get; }

    public GpuSelectorItem(GpuInfo gpu)
    {
        Gpu = gpu;
        RefreshDisplayString();
    }

    public string DisplayString { get; private set; } = string.Empty;

    public void RefreshDisplayString()
    {
        double gb = Gpu.DedicatedVramBytes / 1024.0 / 1024.0 / 1024.0;
        DisplayString = $"{Gpu.Name} · {gb:F0} GB";
        OnPropertyChanged(nameof(DisplayString));
    }
}
