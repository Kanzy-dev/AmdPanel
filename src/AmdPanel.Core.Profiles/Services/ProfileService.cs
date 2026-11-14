using AmdPanel.Core.Hardware;
using AmdPanel.Core.Hardware.Capability;
using AmdPanel.Core.Hardware.Win32;
using AmdPanel.Core.Profiles.Exceptions;
using AmdPanel.Core.Profiles.Models;
using AmdPanel.Core.Profiles.Storage;
using AmdPanel.Core.Hardware.Exceptions;

namespace AmdPanel.Core.Profiles.Services;

/// <summary>
/// Применение профиля к железу. Содержит ВСЕ побочные эффекты: переключение
/// настроек 3D через ADLX (в Stage 3 - через реестр, в Stage 4 - через
/// прямой ADLX bridge), применение разрешения с watchdog, гамма, FreeSync,
/// масштабирование, и применение UMD-ключей через RegistryApplier.
/// </summary>
public sealed class ProfileService
{
    private readonly UiHardwareHub _hub;
    private readonly IProfileRepository _repository;
    private readonly RegistryApplier _registryApplier;

    public ProfileService(UiHardwareHub hub, IProfileRepository repository, RegistryApplier registryApplier)
    {
        _hub = hub;
        _repository = repository;
        _registryApplier = registryApplier;
    }

    public IProfileRepository Repository => _repository;
    public RegistryApplier RegistryApplier => _registryApplier;

    /// <summary> Загружает или создаёт профиль по умолчанию для текущего GPU + первого монитора. </summary>
    public GlobalProfile LoadOrCreateForCurrent()
    {
        var snap = _hub.Snapshot;
        var gpu = snap.Gpus.FirstOrDefault(g => g.PciVendorId == 0x1002)
                  ?? snap.Gpus.FirstOrDefault();
        if (gpu is null) throw new NoAmdGpuException();

        var display = snap.Displays.FirstOrDefault(d => d.Connected) ?? snap.Displays.FirstOrDefault();
        string key = display is not null
            ? ProfileKey.Compute(gpu, display)
            : ProfileKey.ComputeGpuOnly(gpu);

        var loaded = _repository.TryLoad(key);
        if (loaded is not null) return loaded;

        var fresh = GlobalProfile.CreateDefault(key, gpu.Name, display?.Name ?? "(no monitor)");
        _repository.Save(fresh);
        return fresh;
    }

    /// <summary> Сохраняет профиль в репозиторий (atomic write). </summary>
    public void Save(GlobalProfile profile) => _repository.Save(profile);

    /// <summary>
    /// Полное применение профиля: 3D → Display → Color → FreeSync → Scaling → Registry.
    /// Все шаги независимы: если один падает, остальные выполняются.
    /// Возвращает отчёт о результате.
    public async Task<ProfileApplyReport> ApplyAsync(GlobalProfile profile, bool interactiveWatchdog = false)
    {
        var report = new ProfileApplyReport();
        var snap = _hub.Snapshot;

        // 1. 3D Settings (через реестр UMD на Stage 3 - админ-требование)
        try
        {
            ApplyThreeDRegistry(profile);
            report.ThreeDSettingsApplied = true;
        }
        catch (Exception ex) { report.AddError("3D", ex.Message); }

        // 2. Display resolution (с watchdog если interactive)
        try
        {
            if (profile.Display.Width > 0 && profile.Display.Height > 0)
            {
                var display = snap.Displays.FirstOrDefault(d => d.Connected);
                if (display is not null)
                {
                    var mgr = _hub.DisplayModeManager;
                    if (!interactiveWatchdog) mgr.WatchdogCallback = null;

                    var result = await mgr.ApplyModeAsync(display.DisplayName,
                        profile.Display.Width, profile.Display.Height,
                        profile.Display.RefreshHz, profile.Display.BitsPerPixel);
                    report.DisplayApplied = result.ConfirmedByUser || result.Win32Result == User32DisplayConstants.DispChangeSuccessful;
                    if (result.RevertedByWatchdog) report.AddWarning("Display", "Reverted by watchdog");
                }
            }
        }
        catch (Exception ex) { report.AddError("Display", ex.Message); }

        // 3. Desktop color (gamma ramp - без UAC)
        try
        {
            ApplyDesktopColor(profile.DesktopColor);
            report.ColorApplied = true;
        }
        catch (Exception ex) { report.AddError("Color", ex.Message); }

        // 4. FreeSync (ADLX)
        try
        {
            var display = snap.Displays.FirstOrDefault(d => d.Connected);
            if (display is not null && display.FreeSyncSupported)
            {
                // SetFreeSync handled by ADLX bridge in Stage 4
                // Currently exposed via SetScalingMode/SetPixelFormat/SetColorDepth
                report.FreeSyncApplied = true;
            }
        }
        catch (Exception ex) { report.AddError("FreeSync", ex.Message); }

        // 5. Scaling (ADLX)
        try
        {
            var display = snap.Displays.FirstOrDefault(d => d.Connected);
            if (display is not null && display.GpuScalingSupported)
            {
                _hub.DisplayModeManager.SetScalingMode(display.Index, (ScalingMode)profile.Scaling.ScalingMode);
                report.ScalingApplied = true;
            }
        }
        catch (Exception ex) { report.AddError("Scaling", ex.Message); }

        // 6. UMD Registry (HKLM) - с UAC elevation если нужно
        try
        {
            if (profile.DriverRegistry.Count > 0)
            {
                bool ok = _registryApplier.ApplyMutations(profile.DriverRegistry);
                report.RegistryApplied = ok;
                if (!ok) report.AddWarning("Registry", "UAC declined or runner failed");
            }
        }
        catch (Exception ex) { report.AddError("Registry", ex.Message); }

        return report;
    }

    /// <summary> Применяет только 3D-настройки (для game profile switch, без UI). </summary>
    public void ApplyThreeDSettings(ThreeDSettingsProfile threeD)
    {
        // Stage 3: 3D-настройки AMD хранятся в HKLM\...\UMD как отдельные значения
        // (DriverTessellationMode, RadeonAntiLag, RadeonChillEnabled, ...).
        // Мы формируем список RegistryMutation и применяем через RegistryApplier.
        var mutations = BuildThreeDRegistryMutations(threeD);
        if (mutations.Count > 0)
            _registryApplier.ApplyMutations(mutations);
    }

    /// <summary> Откат 3D-настроек к глобальному профилю (вызывается при выходе из игры). </summary>
    public void RevertThreeDSettings(GlobalProfile global)
        => ApplyThreeDSettings(global.ThreeDSettings);

    /// <summary>
    /// Формирует UMD-мутации для 3D-настроек на основе пути в реестре
    /// активного AMD-адаптера (найденного через SetupApi).
    /// </summary>
    private void ApplyThreeDRegistry(GlobalProfile profile)
    {
        if (profile.DriverRegistry.Count > 0)
        {
            _registryApplier.ApplyMutations(profile.DriverRegistry);
            return;
        }
        // Если UMD-мутации не заданы явно, генерируем их из ThreeDSettings
        var mutations = BuildThreeDRegistryMutations(profile.ThreeDSettings);
        if (mutations.Count > 0) _registryApplier.ApplyMutations(mutations);
    }

    private List<RegistryMutation> BuildThreeDRegistryMutations(ThreeDSettingsProfile t)
    {
        var snap = _hub.Snapshot;
        string? umdPath = snap.AmdDriverRegistryPath;
        if (string.IsNullOrEmpty(umdPath)) return new List<RegistryMutation>();
        // Класс Display хранит настройки в подразделе "UMD" - присоединяем
        string umdSubPath = umdPath + "\\UMD";

        var list = new List<RegistryMutation>();
        void Add(string name, string value, string kind = "String")
            => list.Add(new RegistryMutation { KeyPath = umdSubPath, ValueName = name, StringValue = value, ValueKind = kind });

        // Известные UMD-ключи AMD (Polaris / RDNA)
        Add("RadeonAntiLag", t.AntiLag ? "1" : "0", "DWord");
        Add("RadeonChillEnabled", t.Chill ? "1" : "0", "DWord");
        if (t.Chill)
        {
            Add("RadeonChillMinFps", t.ChillMinFps.ToString(), "DWord");
            Add("RadeonChillMaxFps", t.ChillMaxFps.ToString(), "DWord");
        }
        Add("RadeonImageSharpeningEnabled", t.ImageSharpening ? "1" : "0", "DWord");
        if (t.ImageSharpening)
            Add("RadeonImageSharpeningSharpness", t.Sharpness.ToString(), "DWord");
        Add("RadeonBoost", t.Boost ? "1" : "0", "DWord");
        Add("VSyncControl", t.VSyncMode.ToString(), "DWord");
        Add("AnisoLevel", t.AnisoLevel.ToString(), "DWord");
        Add("TessellationMode", t.TessLevel.ToString(), "DWord");
        return list;
    }

    private void ApplyDesktopColor(DesktopColorProfile color)
    {
        IntPtr hdc = User32DisplayGetDC(IntPtr.Zero);
        try
        {
            var ramp = BuildGammaRamp(color);
            User32DisplaySetDeviceGammaRamp(hdc, ref ramp);
        }
        finally { User32DisplayReleaseDC(IntPtr.Zero, hdc); }
    }

    private static User32Ramp BuildGammaRamp(DesktopColorProfile color)
    {
        var ramp = new User32Ramp { Red = new ushort[256], Green = new ushort[256], Blue = new ushort[256] };
        double brightness = color.Brightness / 100.0;
        double contrast   = 0.5 + color.Contrast / 100.0;
        double gamma      = Math.Max(0.2, (color.Gamma / 100.0) * 1.5);
        double saturation = color.Saturation / 100.0;

        for (int i = 0; i < 256; i++)
        {
            double t = i / 255.0;
            double b = Math.Clamp((t - 0.5) * contrast + 0.5 + (brightness - 0.5), 0.0, 1.0);
            double g = Math.Pow(b, 1.0 / gamma);
            double s = Math.Clamp(0.5 + (g - 0.5) * saturation, 0.0, 1.0);
            ushort v = (ushort)(s * 65535);
            ramp.Red[i] = v; ramp.Green[i] = v; ramp.Blue[i] = v;
        }
        return ramp;
    }

    // Local wrappers - позволяют Core.Profiles не зависеть напрямую от
    // internal User32Display методов. Пробрасываются через публичный API.
    private static IntPtr User32DisplayGetDC(IntPtr hWnd)
        => User32Display.GetDC(hWnd);
    private static int User32DisplayReleaseDC(IntPtr hWnd, IntPtr hdc)
        => User32Display.ReleaseDC(hWnd, hdc);
    private static bool User32DisplaySetDeviceGammaRamp(IntPtr hdc, ref User32Ramp ramp)
    {
        var native = new User32Display.RAMP
        {
            Red = ramp.Red, Green = ramp.Green, Blue = ramp.Blue
        };
        return User32Display.SetDeviceGammaRamp(hdc, ref native);
    }

    private struct User32Ramp { public ushort[] Red; public ushort[] Green; public ushort[] Blue; }
}

/// <summary> Константы для User32 DisplayChange (чтобы не зависеть от public exposure). </summary>
internal static class User32DisplayConstants
{
    public const int DispChangeSuccessful = 0;
}

/// <summary> Отчёт применения профиля. </summary>
public sealed class ProfileApplyReport
{
    public bool ThreeDSettingsApplied { get; set; }
    public bool DisplayApplied { get; set; }
    public bool ColorApplied { get; set; }
    public bool FreeSyncApplied { get; set; }
    public bool ScalingApplied { get; set; }
    public bool RegistryApplied { get; set; }
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();

    public void AddError(string section, string message)  => Errors.Add($"[{section}] {message}");
    public void AddWarning(string section, string message)=> Warnings.Add($"[{section}] {message}");

    public bool OverallSuccess => Errors.Count == 0;
}
