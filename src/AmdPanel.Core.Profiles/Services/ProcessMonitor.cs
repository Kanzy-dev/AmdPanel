using System.Diagnostics;
using System.Management;

namespace AmdPanel.Core.Profiles.Services;

/// <summary> Информация о процессе, запущенного или завершённого. </summary>
public sealed record ProcessEventArgs(int ProcessId, string ProcessName, string? ExecutablePath);

/// <summary>
/// Мониторинг процессов БЕЗ прав администратора.
/// </summary>
/// <remarks>
/// <para>
/// <b>Почему не <c>Win32_ProcessStartTrace</c></b> — этот WMI-класс требует
/// привилегии <c>SeSystemProfilePrivilege</c>, которая выдаётся только
/// администратору. На asInvoker-приложении подписка падает с
/// <c>AccessDenied</c>. Мы НЕ используем его (Requirement 1).
/// </para>
/// <para>
/// <b>Вместо этого</b> — два уровня, выбираемых автоматически:
/// </para>
/// <list type="number">
///   <item><b>WMI polling через <c>__InstanceCreationEvent</c>/<c>__InstanceDeletionEvent</c>
///   для <c>Win32_Process</c></b> с интервалом <c>WITHIN 1.5</c>. Это работает
///   без admin-прав на большинстве Windows 10/11 машин, не требует polling
///   всего списка процессов, и не нагружает CPU.</item>
///   <item><b>Fallback на лёгкий polling <c>Process.GetProcesses()</c></b> — если
///   WMI-подписка по какой-то причине не удалась (например, WMI service
///   остановлен). Сравнение идёт по snapshot предыдущего списка, детектор
///   diff'а выполняется за O(n) с использованием HashSet.</item>
/// </list>
/// Оба пути поднимают события <see cref="ProcessStarted"/> и
/// <see cref="ProcessStopped"/> в текущем (не UI) потоке - подписчик
/// должен сам пробрасывать в Dispatcher если нужно.
/// </summary>
public sealed class ProcessMonitor : IDisposable
{
    private readonly TimeSpan _pollInterval;
    private ManagementEventWatcher? _creationWatcher;
    private ManagementEventWatcher? _deletionWatcher;
    private System.Threading.Timer? _fallbackTimer;
    private HashSet<string> _previousProcessNames = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public event EventHandler<ProcessEventArgs>? ProcessStarted;
    public event EventHandler<ProcessEventArgs>? ProcessStopped;

    public bool IsUsingWmi { get; private set; }
    public bool IsRunning { get; private set; }

    public ProcessMonitor(TimeSpan? pollInterval = null)
    {
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1.5);
    }

    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;

        if (TryStartWmiWatchers()) { IsUsingWmi = true; }
        else { StartFallbackPolling(); IsUsingWmi = false; }
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;

        try { _creationWatcher?.Stop(); } catch { }
        try { _deletionWatcher?.Stop(); } catch { }
        _fallbackTimer?.Dispose();
        _fallbackTimer = null;
    }

    /// <summary>
    /// Пробует запустить WMI event watchers без admin-прав.
    /// </summary>
    private bool TryStartWmiWatchers()
    {
        try
        {
            string scope = @"\\.\root\CIMV2";
            // __InstanceCreationEvent для Win32_Process - не требует SeSystemProfilePrivilege,
            // в отличие от Win32_ProcessStartTrace (который требует admin-прав).
            double within = _pollInterval.TotalSeconds;
            string creationQuery =
                $"SELECT TargetInstance FROM __InstanceCreationEvent " +
                $"WITHIN {within:F1} " +
                "WHERE TargetInstance ISA 'Win32_Process'";

            _creationWatcher = new ManagementEventWatcher(scope, creationQuery);
            _creationWatcher.EventArrived += OnCreationEventArrived;
            _creationWatcher.Start();

            string deletionQuery =
                $"SELECT TargetInstance FROM __InstanceDeletionEvent " +
                $"WITHIN {within:F1} " +
                "WHERE TargetInstance ISA 'Win32_Process'";
            _deletionWatcher = new ManagementEventWatcher(scope, deletionQuery);
            _deletionWatcher.EventArrived += OnDeletionEventArrived;
            _deletionWatcher.Start();

            return true;
        }
        catch (Exception)
        {
            // Access denied или WMI service stopped - fallback к polling
            _creationWatcher?.Dispose(); _creationWatcher = null;
            _deletionWatcher?.Dispose(); _deletionWatcher = null;
            return false;
        }
    }

    private void OnCreationEventArrived(object? sender, EventArrivedEventArgs e)
    {
        try
        {
            if (e.NewEvent?["TargetInstance"] is not ManagementBaseObject target) return;
            int pid = Convert.ToInt32(target["ProcessId"]);
            string name = (target["Name"] as string ?? string.Empty).ToLowerInvariant();
            // Обрезаем .exe если есть
            if (name.EndsWith(".exe")) name = name.Substring(0, name.Length - 4);
            string? path = target["ExecutablePath"] as string;
            ProcessStarted?.Invoke(this, new ProcessEventArgs(pid, name, path));
        }
        catch { /* swallow - never throw from event handler */ }
    }

    private void OnDeletionEventArrived(object? sender, EventArrivedEventArgs e)
    {
        try
        {
            if (e.NewEvent?["TargetInstance"] is not ManagementBaseObject target) return;
            int pid = Convert.ToInt32(target["ProcessId"]);
            string name = (target["Name"] as string ?? string.Empty).ToLowerInvariant();
            if (name.EndsWith(".exe")) name = name.Substring(0, name.Length - 4);
            ProcessStopped?.Invoke(this, new ProcessEventArgs(pid, name, null));
        }
        catch { /* swallow */ }
    }

    /// <summary>
    /// Fallback: лёгкий polling Process.GetProcesses() с diff'ом по имени.
    /// </summary>
    private void StartFallbackPolling()
    {
        // Начальный snapshot
        _previousProcessNames = GetCurrentProcessNames();

        _fallbackTimer = new System.Threading.Timer(OnFallbackTick, null,
            (int)_pollInterval.TotalMilliseconds, (int)_pollInterval.TotalMilliseconds);
    }

    private void OnFallbackTick(object? state)
    {
        if (_disposed) return;
        try
        {
            var current = GetCurrentProcessNames();
            // Started: в current, но не в previous
            foreach (var name in current)
                if (!_previousProcessNames.Contains(name))
                    ProcessStarted?.Invoke(this, new ProcessEventArgs(0, name, null));
            // Stopped: в previous, но не в current
            foreach (var name in _previousProcessNames)
                if (!current.Contains(name))
                    ProcessStopped?.Invoke(this, new ProcessEventArgs(0, name, null));
            _previousProcessNames = current;
        }
        catch { /* swallow */ }
    }

    private static HashSet<string> GetCurrentProcessNames()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try { set.Add(p.ProcessName.ToLowerInvariant()); } catch { }
        }
        return set;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _creationWatcher?.Dispose();
        _deletionWatcher?.Dispose();
    }
}
