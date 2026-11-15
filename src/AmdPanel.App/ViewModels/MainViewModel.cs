using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using AmdPanel.App.Mvvm;
using AmdPanel.App.Services;
using AmdPanel.App.ViewModels.Pages;
using AmdPanel.Core.Hardware;
using AmdPanel.Core.Hardware.Capability;
using AmdPanel.Core.Profiles.Models;
using AmdPanel.Core.Profiles.Services;
using AmdPanel.Core.Profiles.Storage;
using CommunityToolkit.Mvvm.Input;

namespace AmdPanel.App.ViewModels;

/// <summary>
/// Корневая ViewModel приложения. Управляет:
/// - списком GPU (GpuSelectorItem в шапке)
/// - деревом навигации слева
/// - активной страницей (SelectedPage + DataTemplate-маппинг в App.xaml)
/// - состоянием Dirty кнопок Apply / Cancel
/// - языковой переключением
/// - связкой с ProfileService (Stage 3): Apply теперь не только меняет железо,
///   но и persistит настройки в GlobalProfile и репозиторий.
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly UiHardwareHub _hub;
    private readonly WatchdogDialogService _watchdogService;
    private readonly LanguageService _languageService;
    private readonly ProfileService _profileService;
    private readonly GlobalProfile _globalProfile;
    private readonly ContextMenuService _contextMenuService;
    private readonly AppUpdateService _updateService;
    private readonly IProfileRepository _profileRepository;

    public MainViewModel(UiHardwareHub hub, WatchdogDialogService watchdogService,
        LanguageService languageService, ProfileService profileService, GlobalProfile globalProfile,
        ContextMenuService contextMenuService, AppUpdateService updateService, IProfileRepository profileRepository)
    {
        _hub = hub;
        _watchdogService = watchdogService;
        _languageService = languageService;
        _profileService = profileService;
        _globalProfile = globalProfile;
        _contextMenuService = contextMenuService;
        _updateService = updateService;
        _profileRepository = profileRepository;
        _hub.DisplayModeManager.WatchdogCallback = _watchdogService.OnDisplayChangeRequest;
        ApplyCommand = new RelayCommand(async () => await ApplyAsync(), () => CanApply);
        CancelCommand = new RelayCommand(Cancel, () => IsDirty);
        RescanCommand = new RelayCommand(async () => await RescanAsync());
        ChangeLanguageCommand = new RelayCommand<string>(lang => _languageService.ApplyLanguage(lang!));
        BrowseDriverFileCommand = new RelayCommand(BrowseDriverFile);
        CheckDriverUpdatesCommand = new RelayCommand(async () => await CheckDriverUpdatesAsync());
        InstallDriverCommand = new RelayCommand(async () => await InstallDriverAsync());
    }

    // ===== Свойства для шапки =====

    public ObservableCollection<GpuSelectorItem> Gpus { get; } = new();

    private GpuSelectorItem? _selectedGpu;
    public GpuSelectorItem? SelectedGpu
    {
        get => _selectedGpu;
        set
        {
            if (_selectedGpu == value) return;
            _selectedGpu = value;
            OnPropertyChanged();
            RebuildNavigation();
        }
    }

    public string AdlxVersion => _hub.Snapshot.AdlxVersion;
    public bool IsDriverOnly => _hub.Snapshot.IsDriverOnlyInstallation;

    // ===== Настройки интерфейса =====
    
    public bool ContextMenuEnabled
    {
        get => _contextMenuService.IsContextMenuEnabled();
        set
        {
            if (_contextMenuService.IsContextMenuEnabled() == value) return;
            _contextMenuService.SetContextMenuEnabled(value);
            OnPropertyChanged();
        }
    }

    // ===== Навигация =====

    public ObservableCollection<NavigationNode> Navigation { get; } = new();

    private NavigationNode? _selectedNode;
    public NavigationNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (_selectedNode == value) return;
            _selectedNode = value;
            OnPropertyChanged();
            OnNavigationNodeChanged();
        }
    }

    private ViewModelBase? _activePage;
    public ViewModelBase? ActivePage
    {
        get => _activePage;
        set
        {
            if (_activePage is IPageViewModel oldPage)
            {
                oldPage.DirtyStateChanged -= OnPageDirtyChanged;
                if (oldPage is PerformancePageViewModel perf) perf.StopPolling();
            }
            if (SetField(ref _activePage, value)) // это меняет IsDirty на true; не хотим
            {
                IsDirty = false;
            }
            if (_activePage is IPageViewModel newPage)
            {
                newPage.DirtyStateChanged += OnPageDirtyChanged;
                if (newPage is PerformancePageViewModel perf) perf.StartPolling();
            }
            ApplyCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnPageDirtyChanged(object? sender, bool dirty)
    {
        // Глобальный Dirty синхронизирован с Dirty активной страницы
        IsDirty = dirty;
        ApplyCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    // ===== Commands =====

    public IRelayCommand ApplyCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand RescanCommand { get; }
    public IRelayCommand<string> ChangeLanguageCommand { get; }
    public IRelayCommand BrowseDriverFileCommand { get; }
    public IRelayCommand CheckDriverUpdatesCommand { get; }
    public IRelayCommand InstallDriverCommand { get; }

    private bool CanApply => IsDirty;

    private async Task ApplyAsync()
    {
        if (ActivePage is null) return;
        try
        {
            switch (ActivePage)
            {
                case DisplayResolutionPageViewModel res:
                    var result = await res.ApplyAsync();
                    if (result.RevertedByWatchdog)
                        ShowStatus(string.Format(FindString("status.watchdogReverted") ?? "Reverted by watchdog after {0}s",
                            DisplayModeManager.WatchdogSeconds));
                    else if (result.ConfirmedByUser)
                    {
                        // Persist in globalProfile
                        _globalProfile.Display = new DisplaySettingsProfile
                        {
                            Width = res.SelectedResolution?.Width ?? 0,
                            Height = res.SelectedResolution?.Height ?? 0,
                            RefreshHz = res.SelectedRefresh,
                            BitsPerPixel = res.SelectedBpc,
                            PixelFormat = (int)(res.SelectedPixelFormat?.Format ?? PixelFormat.Rgb444)
                        };
                        _profileService.Save(_globalProfile);
                        ShowStatus(FindString("status.resolutionApplied") ?? "Resolution applied");
                    }
                    break;
                case DisplayColorPageViewModel col:
                    col.ApplyColor();
                    _globalProfile.DesktopColor = new DesktopColorProfile
                    {
                        Brightness = col.Brightness, Contrast = col.Contrast,
                        Gamma = col.Gamma, Saturation = col.Saturation
                    };
                    _profileService.Save(_globalProfile);
                    ShowStatus(FindString("status.colorApplied") ?? "Color settings applied");
                    break;
                case FreeSyncPageViewModel fs:
                    _globalProfile.FreeSync = new FreeSyncProfile { Enabled = fs.FreeSyncEnabled };
                    _profileService.Save(_globalProfile);
                    ShowStatus(FindString("status.freesyncApplied") ?? "FreeSync setting applied");
                    break;
                case ScalingPageViewModel sc:
                    _globalProfile.Scaling = new ScalingProfile
                    {
                        // ScalingMode — enum из AmdPanel.Core.Hardware.Capability,
                        // using уже добавлен, обращаемся напрямую без префикса Capability.
                        ScalingMode = (int)(sc.SelectedScalingMode?.Mode ?? ScalingMode.AspectRatio),
                        ScalingDevice = sc.SelectedScalingDevice
                    };
                    _profileService.Save(_globalProfile);
                    ShowStatus(FindString("status.scalingApplied") ?? "Scaling applied");
                    break;
                case ThreeDSettingsPageViewModel d3:
                    _globalProfile.ThreeDSettings = new ThreeDSettingsProfile
                    {
                        AntiLag = d3.AntiLag, Chill = d3.Chill,
                        ChillMinFps = d3.ChillMinFps, ChillMaxFps = d3.ChillMaxFps,
                        ImageSharpening = d3.ImageSharpening, Sharpness = d3.Sharpness,
                        Boost = d3.Boost, VSyncMode = d3.VSyncMode,
                        AnisoLevel = d3.AnisoLevel, TessLevel = d3.TessLevel
                    };
                    _profileService.ApplyThreeDSettings(_globalProfile.ThreeDSettings);
                    _profileService.Save(_globalProfile);
                    ShowStatus(FindString("status.d3Applied") ?? "3D settings applied");
                    break;
                default:
                    ShowStatus(FindString("status.applied") ?? "Applied");
                    break;
            }
            ActivePage.ClearDirty();
            IsDirty = false;
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message);
        }
        ApplyCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private void Cancel()
    {
        ActivePage?.ClearDirty();
        IsDirty = false;
        ApplyCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        ShowStatus(FindString("status.cancelled") ?? "Cancelled");
    }

    private async Task RescanAsync()
    {
        ShowStatus(FindString("status.scanning") ?? "Scanning hardware…");
        await _hub.ScanAsync();
        Gpus.Clear();
        foreach (var g in _hub.GetAmdGpus()) Gpus.Add(new GpuSelectorItem(g));
        SelectedGpu = Gpus.FirstOrDefault();
        OnPropertyChanged(nameof(AdlxVersion));
        OnPropertyChanged(nameof(IsDriverOnly));
        ShowStatus(FindString("status.scanComplete") ?? "Scan complete");
    }

    // ===== Build navigation tree =====

    /// <summary>
    /// Перестроение дерева навигации после смены активного GPU или пересканирования.
    /// Каждая нода-страница создаётся через ViewModel, которая сама знает свои
    /// capabilities (IsAvailable) — недоступные узлы не добавляются в дерево.
    /// </summary>
    private void RebuildNavigation()
    {
        if (SelectedGpu is null) { Navigation.Clear(); return; }

        var gpu = SelectedGpu.Gpu;
        var (caps, ranges) = _hub.GetGpuDetails(gpu.Index);

        Navigation.Clear();

        // Раздел 3D Settings
        var d3Node = new NavigationNode { Key = "nav.3dSettings" };
        if (caps.AntiLag || caps.Chill || caps.ImageSharpening || caps.Tessellation || caps.WaitForVRefresh)
        {
            d3Node.Children.Add(new NavigationNode
            {
                Key = "nav.3dSettings.manage",
                PageKey = "page.3dSettings"
            });
        }
        if (d3Node.Children.Count > 0) Navigation.Add(d3Node);

        // Раздел Display
        var dispNode = new NavigationNode { Key = "nav.display" };
        foreach (var disp in _hub.GetActiveDisplays())
        {
            dispNode.Children.Add(new NavigationNode
            {
                Key = "nav.display.resolution",
                PageKey = "page.resolution"
            });
            dispNode.Children.Add(new NavigationNode
            {
                Key = "nav.display.color",
                PageKey = "page.color"
            });
            if (disp.FreeSyncSupported)
                dispNode.Children.Add(new NavigationNode
                {
                    Key = "nav.display.freesync",
                    PageKey = "page.freesync"
                });
            if (disp.GpuScalingSupported)
                dispNode.Children.Add(new NavigationNode
                {
                    Key = "nav.display.scaling",
                    PageKey = "page.scaling"
                });
            break; // Stage 2: только первый активный монитор; multi-monitor → Stage 3
        }
        if (dispNode.Children.Count > 0) Navigation.Add(dispNode);

        // Раздел Performance (optional)
        if (caps.PerformanceMonitoring)
        {
            var perfNode = new NavigationNode { Key = "nav.performance" };
            perfNode.Children.Add(new NavigationNode
            {
                Key = "nav.performance.monitor",
                PageKey = "page.performance"
            });
            Navigation.Add(perfNode);
        }

        // Раздел System / Drivers
        var sysNode = new NavigationNode { Key = "nav.system" };
        sysNode.Children.Add(new NavigationNode
        {
            Key = "nav.system.driverManager",
            PageKey = "page.driverManager"
        });
        Navigation.Add(sysNode);
    }

    private void OnNavigationNodeChanged()
    {
        if (SelectedNode?.PageKey is null) return;
        if (SelectedGpu is null) return;
        var gpu = SelectedGpu.Gpu;
        var (caps, ranges) = _hub.GetGpuDetails(gpu.Index);

        ActivePage = SelectedNode.PageKey switch
        {
            "page.3dSettings"   => new ThreeDSettingsPageViewModel(gpu.Index, caps, _profileService, _globalProfile, _profileRepository),
            "page.resolution"   => new DisplayResolutionPageViewModel(_hub, _hub.GetActiveDisplays().First()),
            "page.color"        => new DisplayColorPageViewModel(_hub, _hub.GetActiveDisplays().First()),
            "page.freesync"     => new FreeSyncPageViewModel(_hub, _hub.GetActiveDisplays().First()),
            "page.scaling"      => new ScalingPageViewModel(_hub, _hub.GetActiveDisplays().First()),
            "page.driverManager"=> CreateDriverManagerPage(),
            "page.performance"  => new PerformancePageViewModel(_hub, gpu.Index, caps),
            _ => null
        };
    }

    /// <summary> Создаёт DriverManagerPageViewModel и биндит к ней DriverManagerService. </summary>
    private DriverManagerPageViewModel CreateDriverManagerPage()
    {
        var vm = new DriverManagerPageViewModel(_hub);
        var service = new AmdPanel.Core.DriverManager.Services.DriverManagerService(_hub.Snapshot);
        vm.BindService(service);
        return vm;
    }

    // ===== Status bar =====

    private string _statusText = string.Empty;
    public string StatusText
    {
        get => _statusText;
        private set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    private void ShowStatus(string message) => StatusText = message;

    /// <summary> Ищет строку в ресурсах приложения (из Strings.{lang}.xaml). </summary>
    private string? FindString(string key)
    {
        var v = Application.Current?.TryFindResource(key);
        return v as string;
    }

    // ===== Driver manager commands =====

    private void BrowseDriverFile()
    {
        if (ActivePage is not DriverManagerPageViewModel drv) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "AMD driver package (*.exe;*.inf)|*.exe;*.inf|All files (*.*)|*.*",
            Title = "Select AMD driver file"
        };
        if (dlg.ShowDialog() == true)
            drv.SelectedFilePath = dlg.FileName;
    }

    private async Task CheckDriverUpdatesAsync()
    {
        if (ActivePage is DriverManagerPageViewModel drv)
            await drv.CheckUpdatesAsync();
    }

    private async Task InstallDriverAsync()
    {
        if (ActivePage is DriverManagerPageViewModel drv)
        {
            if (string.IsNullOrEmpty(drv.SelectedFilePath))
            {
                ShowStatus(FindString("status.selectDriverFile") ?? "Select driver file first");
                return;
            }
            // Установка драйвера требует UAC (pnputil.exe /add-driver)
            if (!Services.UacElevationService.IsRunningElevated)
            {
                Services.UacElevationService.LaunchElevated(
                    $"--install-driver \"{drv.SelectedFilePath}\" --clean {drv.CleanInstallChecked}");
                return;
            }
            await drv.InstallFromFileAsync(drv.SelectedFilePath);
        }
    }
}
