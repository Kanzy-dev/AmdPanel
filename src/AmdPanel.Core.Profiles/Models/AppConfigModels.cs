using System;
using System.Collections.Generic;

namespace AmdPanel.Core.Profiles.Models;

/// <summary>
/// Полные настройки приложения
/// </summary>
public sealed record AppSettings
{
    public string Language { get; init; } = "ru"; // ru/en
    
    public string SelectedPageKey { get; init; } = "page.3dSettings";
    
    public int SelectedGpuIndex { get; init; } = 0;
    
    public int SelectedDisplayIndex { get; init; } = 0;
    
    public int SelectedWidth { get; init; } = 1920;
    
    public int SelectedHeight { get; init; } = 1080;
    
    public int SelectedRefreshRate { get; init; } = 60;
    
    public bool StartWithWindows { get; init; } = false;
    
    public bool MinimizeToTray { get; init; } = true;
    
    public bool IsFirstRun { get; init; } = true;

    public bool EnableDesktopContextMenu { get; init; } = false;
    
    public bool CheckForUpdates { get; init; } = true;
}

/// <summary>
/// Профиль для конкретного приложения
/// </summary>
public sealed record ApplicationProfileItem
{
    public string ExecutablePath { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool AntiLag { get; init; } = false;
    public bool Chill { get; init; } = false;
    public int ChillMinFps { get; init; } = 30;
    public int ChillMaxFps { get; init; } = 60;
    public bool ImageSharpening { get; init; } = false;
    public int Sharpness { get; init; } = 50;
    public bool Boost { get; init; } = false;
    public int VSyncMode { get; init; } = 1;
    public int AnisoLevel { get; init; } = 0;
    public int TessLevel { get; init; } = 0;
}

/// <summary>
/// Менеджер профилей
/// </summary>
public sealed record ProfileManager
{
    public ThreeDSettingsProfile GlobalProfile { get; init; } = new();
    
    public List<ApplicationProfileItem> Applications { get; init; } = new();
}