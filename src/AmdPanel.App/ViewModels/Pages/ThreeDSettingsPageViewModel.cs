using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using AmdPanel.App.Mvvm;
using AmdPanel.App.Services;
using AmdPanel.Core.Hardware.Capability;
using AmdPanel.Core.Profiles.Models;
using AmdPanel.Core.Profiles.Services;
using CommunityToolkit.Mvvm.Input;

namespace AmdPanel.App.ViewModels.Pages;

/// <summary>
/// Страница "Управление параметрами 3D" с двумя табами:
/// Глобальные параметры / Программные настройки.
/// Program Settings показывает список ApplicationProfile из GlobalProfile,
/// позволяет добавить новый .exe, редактировать его 3D-настройки.
/// </summary>
public sealed class ThreeDSettingsPageViewModel : ViewModelBase, IPageViewModel
{
    public string PageKey => "page.3dSettings";

    private readonly GpuCapabilities _caps;
    private readonly int _gpuIndex;
    private readonly GlobalProfile _globalProfile;
    private readonly ProfileService? _profileService;

    /// <summary>
    /// ctor для Stage 2 (без ProfileService — Program Settings tab недоступен).
    /// </summary>
    public ThreeDSettingsPageViewModel(int gpuIndex, GpuCapabilities caps)
        : this(gpuIndex, caps, null!, null) { }

    /// <summary> ctor для Stage 3 — с поддержкой Program Settings. </summary>
    public ThreeDSettingsPageViewModel(int gpuIndex, GpuCapabilities caps,
        ProfileService profileService, GlobalProfile globalProfile)
    {
        _gpuIndex = gpuIndex;
        _caps = caps;
        _profileService = profileService;
        _globalProfile = globalProfile;

        // Загрузить список программных профилей
        if (_globalProfile is not null)
        {
            foreach (var ap in _globalProfile.ApplicationProfiles)
                ApplicationProfiles.Add(new ApplicationProfileItem(ap));
        }

        // Инициализация команд в стиле NVIDIA Control Panel
        BrowseProgramCommand = new RelayCommand(BrowseProgram);
        RemoveProgramCommand = new RelayCommand(RemoveProgram, () => SelectedProgram is not null);
    }

    public bool IsAvailable => _caps.AntiLag || _caps.Chill || _caps.ImageSharpening
                              || _caps.Tessellation || _caps.WaitForVRefresh;

    // --- Состояние глобальных параметров (Dirty через SetField) ---
    private bool _antiLag;        public bool AntiLag        { get => _antiLag;        set => SetField(ref _antiLag, value); }
    private bool _chill;          public bool Chill          { get => _chill;          set => SetField(ref _chill, value); }
    private int  _chillMinFps=72; public int  ChillMinFps    { get => _chillMinFps;    set => SetField(ref _chillMinFps, value); }
    private int  _chillMaxFps=144;public int  ChillMaxFps    { get => _chillMaxFps;    set => SetField(ref _chillMaxFps, value); }
    private bool _imageSharpen;   public bool ImageSharpening{ get => _imageSharpen;   set => SetField(ref _imageSharpen, value); }
    private int  _sharpness=50;   public int  Sharpness      { get => _sharpness;      set => SetField(ref _sharpness, value); }
    private bool _boost;          public bool Boost          { get => _boost;          set => SetField(ref _boost, value); }
    private int  _vSyncMode;      public int  VSyncMode      { get => _vSyncMode;      set => SetField(ref _vSyncMode, value); }
    private int  _anisoLevel;     public int  AnisoLevel     { get => _anisoLevel;     set => SetField(ref _anisoLevel, value); }
    private int  _tessLevel;      public int  TessLevel      { get => _tessLevel;      set => SetField(ref _tessLevel, value); }

    // --- Capabilities ---
    public bool CanAntiLag        => _caps.AntiLag;
    public bool CanChill          => _caps.Chill;
    public bool CanImageSharpen   => _caps.ImageSharpening;
    public bool CanBoost          => _caps.Boost;
    public bool CanTessellation   => _caps.Tessellation;
    public bool CanAnisotropic    => _caps.Anisotropic;
    public bool CanWaitForVRefresh=> _caps.WaitForVRefresh;
    public bool CanFrameRateTarget=> _caps.FrameRateTarget;

    public bool HasProgramSettings => _profileService is not null && _globalProfile is not null;

    // === Program Settings tab ===
    public ObservableCollection<ApplicationProfileItem> ApplicationProfiles { get; } = new();

    /// <summary>
    /// Загружает список установленных программ при переключении на вкладку
    /// "Программные настройки" в стиле NVIDIA Control Panel.
    /// </summary>
    public void LoadInstalledApps()
    {
        InstalledPrograms.Clear();
        var apps = InstalledAppsScanner.ScanAll();
        foreach (var app in apps)
        {
            InstalledPrograms.Add(app);
        }
        SelectedProgram = null;
        SelectedParameterDescription = string.Empty;
        SelectedParameterUsage = string.Empty;
    }

    // Новые свойства для NVIDIA Control Panel стиля
    public ObservableCollection<InstalledAppInfo> InstalledPrograms { get; } = new();
    private InstalledAppInfo? _selectedProgram;
    public InstalledAppInfo? SelectedProgram
    {
        get => _selectedProgram;
        set
        {
            if (SetField(ref _selectedProgram, value))
            {
                UpdateParameterDescription();
            }
        }
    }

    public ICommand BrowseProgramCommand { get; }
    public ICommand RemoveProgramCommand { get; }

    public string SelectedParameterDescription { get; set; } = string.Empty;
    public string SelectedParameterUsage { get; set; } = string.Empty;

    private void BrowseProgram()
    {
        var openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Executable files (*.exe)|*.exe",
            Title = "Select application to add"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            var exePath = openFileDialog.FileName;
            var icon = InstalledAppsScanner.ExtractIcon(exePath);
            var name = System.IO.Path.GetFileNameWithoutExtension(exePath);

            var appInfo = new InstalledAppInfo
            {
                DisplayName = name,
                ExecutablePath = exePath,
                ProcessName = name,
                Icon = icon,
                Source = "Custom"
            };

            InstalledPrograms.Add(appInfo);
            SelectedProgram = appInfo;
        }
    }

    private void RemoveProgram()
    {
        if (SelectedProgram is not null)
        {
            InstalledPrograms.Remove(SelectedProgram);
            SelectedProgram = null;
            SelectedParameterDescription = string.Empty;
            SelectedParameterUsage = string.Empty;
        }
    }

    private void UpdateParameterDescription()
    {
        if (SelectedProgram is not null)
        {
            SelectedParameterDescription = $"Select 3D settings for {SelectedProgram.DisplayName}";
            SelectedParameterUsage = "Settings will apply when this program is running.";
        }
        else
        {
            SelectedParameterDescription = string.Empty;
            SelectedParameterUsage = string.Empty;
        }
    }


            var existing = ApplicationProfiles.FirstOrDefault(ap => ap.Profile.ProcessName == SelectedProgram.ProcessName);
            if (existing is null && _profileService is not null && _globalProfile is not null)
            {
                // Создаём новый профиль
                var newProfile = new ApplicationProfile
                {
                    ProcessName = SelectedProgram.ProcessName,
                    DisplayName = SelectedProgram.DisplayName,
                    ExecutablePath = SelectedProgram.ExecutablePath,
                    ThreeDSettings = ThreeDSettingsProfile.Defaults()
                };
                _globalProfile.ApplicationProfiles.Add(newProfile);
                ApplicationProfiles.Add(new ApplicationProfileItem(newProfile));
                existing = ApplicationProfiles.Last();
            }
            SelectedApplication = existing;
        }
        else
        {
            SelectedApplication = null;
        }
    }

    private string _newProcessName = string.Empty;
    public string NewProcessName
    {
        get => _newProcessName;
        set => SetField(ref _newProcessName, value);
    }

    // Edit panel: copy of SelectedApplication.ThreeDSettings for binding
    private bool _appAntiLag;   public bool AppAntiLag   { get => _appAntiLag;   set => SetField(ref _appAntiLag, value); }
    private bool _appChill;     public bool AppChill     { get => _appChill;     set => SetField(ref _appChill, value); }
    private int  _appSharpness=50; public int AppSharpness { get => _appSharpness; set => SetField(ref _appSharpness, value); }
    private bool _appSharpnessEnabled; public bool AppSharpnessEnabled { get => _appSharpnessEnabled; set => SetField(ref _appSharpnessEnabled, value); }
    private int  _appVSync;     public int AppVSync       { get => _appVSync;     set => SetField(ref _appVSync, value); }
    private int  _appTess;      public int AppTess        { get => _appTess;      set => SetField(ref _appTess, value); }
    private int  _appAniso;     public int AppAniso       { get => _appAniso;     set => SetField(ref _appAniso, value); }
    private bool _appBoost;     public bool AppBoost     { get => _appBoost;     set => SetField(ref _appBoost, value); }

    /// <summary> Добавляет новый программный профиль по имени процесса. </summary>
    public void AddApplicationProfile(string processName, string? exePath = null)
    {
        if (string.IsNullOrWhiteSpace(processName)) return;
        if (_globalProfile is null || _profileService is null) return;

        string name = processName.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 4);
        name = name.ToLowerInvariant();

        if (_globalProfile.ApplicationProfiles.Any(p => p.Matches(name)))
            return; // уже есть

        var ap = new ApplicationProfile
        {
            ProcessName = name,
            ExecutablePath = exePath,
            DisplayName = name,
            ThreeDSettings = ThreeDSettingsProfile.Defaults()
        };
        _globalProfile.ApplicationProfiles.Add(ap);
        ApplicationProfiles.Add(new ApplicationProfileItem(ap));
        _profileService.Save(_globalProfile);
        SelectedApplication = ApplicationProfiles.Last();
    }

    /// <summary> Удаляет выбранный программный профиль. </summary>
    public void RemoveSelectedApplication()
    {
        if (SelectedApplication is null || _globalProfile is null || _profileService is null) return;
        _globalProfile.ApplicationProfiles.Remove(SelectedApplication.Profile);
        ApplicationProfiles.Remove(SelectedApplication);
        _profileService.Save(_globalProfile);
        SelectedApplication = ApplicationProfiles.FirstOrDefault();
    }

    /// <summary> Сохраняет отредактированные в UI настройки обратно в профиль приложения. </summary>
    public void PersistSelectedApplication()
    {
        if (SelectedApplication is null || _profileService is null) return;
        SelectedApplication.Profile.ThreeDSettings.AntiLag = AppAntiLag;
        SelectedApplication.Profile.ThreeDSettings.Chill = AppChill;
        SelectedApplication.Profile.ThreeDSettings.Sharpness = AppSharpnessEnabled ? AppSharpness : 0;
        SelectedApplication.Profile.ThreeDSettings.VSyncMode = AppVSync;
        SelectedApplication.Profile.ThreeDSettings.TessLevel = AppTess;
        SelectedApplication.Profile.ThreeDSettings.AnisotropicLevel = AppAniso;
        SelectedApplication.Profile.ThreeDSettings.Boost = AppBoost;
        _profileService.Save(_globalProfile!);
    }

    private void RefreshApplicationEditPanel()
    {
        if (SelectedApplication is null)
        {
            AppAntiLag = false;
            AppChill = false;
            AppSharpnessEnabled = false;
            AppSharpness = 50;
            AppVSync = 0;
            AppTess = 0;
            AppAniso = 0;
            AppBoost = false;
            UpdateParameterDescription();
            return;
        }
        var t = SelectedApplication.Profile.ThreeDSettings;
        // Без SetField чтобы не помечать Dirty при программном заполнении
        _appAntiLag = t.AntiLag;
        OnPropertyChanged(nameof(AppAntiLag));

        _appChill = t.Chill;
        OnPropertyChanged(nameof(AppChill));

        _appSharpnessEnabled = t.Sharpness > 0;
        OnPropertyChanged(nameof(AppSharpnessEnabled));

        _appSharpness = t.Sharpness;
        OnPropertyChanged(nameof(AppSharpness));

        _appVSync = t.VSyncMode;
        OnPropertyChanged(nameof(AppVSync));

        _appTess = t.TessLevel;
        OnPropertyChanged(nameof(AppTess));

        _appAniso = t.AnisotropicLevel;
        OnPropertyChanged(nameof(AppAniso));

        _appBoost = t.Boost;
        OnPropertyChanged(nameof(AppBoost));

        UpdateParameterDescription();
    }
}

/// <summary> Обёртка над ApplicationProfile для отображения в ListBox. </summary>
public sealed class ApplicationProfileItem : ViewModelBase
{
    public ApplicationProfile Profile { get; }
    public ApplicationProfileItem(ApplicationProfile profile) { Profile = profile; }
    public string DisplayName => Profile.DisplayName;
    public string ProcessName => Profile.ProcessName;
}
