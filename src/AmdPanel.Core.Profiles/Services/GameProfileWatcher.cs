using System.Diagnostics;
using AmdPanel.Core.Hardware;
using AmdPanel.Core.Profiles.Models;

namespace AmdPanel.Core.Profiles.Services;

/// <summary>
/// Связывает ProcessMonitor и ProfileService: при запуске .exe, который
/// присутствует в GlobalProfile.ApplicationProfiles, применяется
/// программный профиль 3D-настроек; при выходе игры - возвращается
/// глобальный профиль (Requirement 2 — автоматический откат).
/// </summary>
/// <remarks>
/// Использует LIFO-стек активных приложений: если запущено несколько игр
/// одновременно (маловероятно, но возможно), при выходе из последней
/// восстанавливается профиль предыдущей. Это копия поведения NVIDIA
/// Control Panel "Application Profiles".
/// </remarks>
public sealed class GameProfileWatcher : IDisposable
{
    private readonly ProfileService _profileService;
    private readonly ProcessMonitor _processMonitor;
    private readonly GlobalProfile _globalProfile;
    private readonly object _stackLock = new();
    private readonly Stack<ActiveApplication> _activeApps = new();
    private bool _disposed;

    /// <summary> Поднимается когда программный профиль применён (для UI status). </summary>
    public event EventHandler<ApplicationSwitchedEventArgs>? ApplicationProfileApplied;

    /// <summary> Поднимается когда глобальный профиль восстановлен. </summary>
    public event EventHandler<ApplicationSwitchedEventArgs>? GlobalProfileRestored;

    public GameProfileWatcher(ProfileService profileService, ProcessMonitor processMonitor, GlobalProfile globalProfile)
    {
        _profileService = profileService;
        _processMonitor = processMonitor;
        _globalProfile = globalProfile;

        _processMonitor.ProcessStarted += OnProcessStarted;
        _processMonitor.ProcessStopped += OnProcessStopped;
    }

    public bool IsRunning => _processMonitor.IsRunning;
    public bool IsUsingWmi => _processMonitor.IsUsingWmi;

    /// <summary> Запускает мониторинг процессов. </summary>
    public void Start()
    {
        _processMonitor.Start();
        Trace.WriteLine($"[GameProfileWatcher] Started (WMI={_processMonitor.IsUsingWmi}); " +
                        $"tracking {_globalProfile.ApplicationProfiles.Count} application profiles");
    }

    public void Stop() => _processMonitor.Stop();

    /// <summary> Текущее активное приложение (для UI). </summary>
    public ActiveApplication? CurrentActive
    {
        get
        {
            lock (_stackLock)
                return _activeApps.Count > 0 ? _activeApps.Peek() : null;
        }
    }

    private void OnProcessStarted(object? sender, ProcessEventArgs e)
    {
        var match = _globalProfile.ApplicationProfiles.FirstOrDefault(p => p.Matches(e.ProcessName));
        if (match is null) return;

        lock (_stackLock)
        {
            // Если приложение уже в стеке (дубликат процесса) - игнорируем
            if (_activeApps.Any(a => string.Equals(a.ProcessName, e.ProcessName, StringComparison.OrdinalIgnoreCase)))
                return;

            _activeApps.Push(new ActiveApplication(e.ProcessId, e.ProcessName, match, DateTimeOffset.UtcNow));
        }

        try
        {
            _profileService.ApplyThreeDSettings(match.ThreeDSettings);
            ApplicationProfileApplied?.Invoke(this, new ApplicationSwitchedEventArgs(match, e));
            Trace.WriteLine($"[GameProfileWatcher] Applied profile '{match.DisplayName}' for {e.ProcessName}.exe (pid={e.ProcessId})");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[GameProfileWatcher] Failed to apply profile for {e.ProcessName}: {ex.Message}");
        }
    }

    private void OnProcessStopped(object? sender, ProcessEventArgs e)
    {
        ActiveApplication? removed = null;
        lock (_stackLock)
        {
            // Удаляем по имени процесса (PID может быть 0 в fallback polling mode)
            var asArray = _activeApps.ToArray();
            ActiveApplication?[] kept = new ActiveApplication?[asArray.Length];
            int keptCount = 0;
            for (int i = asArray.Length - 1; i >= 0; i--)
            {
                var a = asArray[i];
                if (removed is null && string.Equals(a.ProcessName, e.ProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    removed = a;
                }
                else
                {
                    kept[keptCount++] = a;
                }
            }
            // Пересобираем стек (верхушка = последний запущенный)
            _activeApps.Clear();
            for (int i = keptCount - 1; i >= 0; i--)
                if (kept[i] is not null) _activeApps.Push(kept[i]!);
        }

        if (removed is null) return;

        // Автооткат: если стек пустой - возвращаем глобальный профиль;
        // если есть ещё приложения - применяем профиль верхнего.
        try
        {
            var top = CurrentActive;
            if (top is null)
            {
                _profileService.RevertThreeDSettings(_globalProfile);
                GlobalProfileRestored?.Invoke(this, new ApplicationSwitchedEventArgs(null, e));
                Trace.WriteLine($"[GameProfileWatcher] Reverted to global profile after {e.ProcessName}.exe exit");
            }
            else
            {
                _profileService.ApplyThreeDSettings(top.Profile.ThreeDSettings);
                ApplicationProfileApplied?.Invoke(this, new ApplicationSwitchedEventArgs(top.Profile, e));
                Trace.WriteLine($"[GameProfileWatcher] Switched to '{top.Profile.DisplayName}' after exit");
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[GameProfileWatcher] Failed to revert: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _processMonitor.ProcessStarted -= OnProcessStarted;
        _processMonitor.ProcessStopped -= OnProcessStopped;
        _processMonitor.Dispose();
    }
}

/// <summary> Запись об активном приложении в стеке GameProfileWatcher. </summary>
public sealed record ActiveApplication(
    int ProcessId,
    string ProcessName,
    ApplicationProfile Profile,
    DateTimeOffset StartedAt);

/// <summary> EventArgs для ApplicationProfileApplied / GlobalProfileRestored. </summary>
public sealed class ApplicationSwitchedEventArgs : EventArgs
{
    public ApplicationProfile? NewProfile { get; }
    public ProcessEventArgs ProcessEvent { get; }
    public ApplicationSwitchedEventArgs(ApplicationProfile? newProfile, ProcessEventArgs processEvent)
    {
        NewProfile = newProfile;
        ProcessEvent = processEvent;
    }
}
