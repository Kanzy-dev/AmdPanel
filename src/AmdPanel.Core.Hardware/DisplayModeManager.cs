using System.Diagnostics;
using System.Runtime.InteropServices;
using AmdPanel.Core.Hardware.Capability;
using AmdPanel.Core.Hardware.Exceptions;
using AmdPanel.Core.Hardware.Native;
using AmdPanel.Core.Hardware.Win32;

namespace AmdPanel.Core.Hardware;

/// <summary>
/// Параметры применения нового режима дисплея (для Watchdog).
/// Содержит всё, что нужно для отката: предыдущий режим + новый режим.
/// </summary>
public sealed record DisplayChangeRequest(
    string DeviceName,
    User32Display.DEVMODE PreviousMode,
    User32Display.DEVMODE NewMode);

/// <summary>
/// Результат применения режима дисплея.
/// </summary>
public sealed record DisplayApplyResult(
    int Win32Result,
    bool ConfirmedByUser,
    bool RevertedByWatchdog,
    string Message);

/// <summary>
/// Делегат для UI: вызывается при старте watchdog-таймера.
/// UI показывает модальное окно "Сохранить изменения?" с кнопками
/// "Да" (Confirm) и "Нет" (Revert). Если UI не подтверждает за
/// <see cref="WatchdogSeconds"/>, менеджер автоматически откатывает.
/// </summary>
public delegate void DisplayWatchdogCallback(DisplayChangeRequest request, DisplayWatchdogController controller);

/// <summary>
/// Контроллер watchdog-диалога: UI получает ссылку на него и вызывает
/// Confirm() или Revert() для досрочного завершения таймера.
/// </summary>
public sealed class DisplayWatchdogController
{
    private readonly DisplayModeManager _mgr;
    private readonly DisplayChangeRequest _req;
    internal DisplayWatchdogController(DisplayModeManager mgr, DisplayChangeRequest req)
    { _mgr = mgr; _req = req; }

    public bool IsConfirmed { get; private set; }
    public bool IsReverted  { get; private set; }

    public void Confirm()
    {
        if (IsConfirmed || IsReverted) return;
        IsConfirmed = true;
        _mgr.CancelWatchdog();
    }

    public void Revert()
    {
        if (IsConfirmed || IsReverted) return;
        IsReverted = true;
        _mgr.CancelWatchdogAndRevert();
    }
}

/// <summary>
/// Безопасное управление режимами дисплея: разрешение, герцовка, bpc.
/// <para>
/// Главный принцип: <b>никогда не применять режим без подтверждения пользователя</b>.
/// При ApplyModeAsync менеджер:
/// </para>
/// <list type="number">
///   <item>Сохраняет предыдущий DEVMODE.</item>
///   <item>Вызывает ChangeDisplaySettingsEx с новым режимом.</item>
///   <item>Запускает таймер на WatchdogSeconds (по умолчанию 15).</item>
///   <item>Через callback уведомляет UI, чтобы показать модальный диалог подтверждения.</item>
///   <item>Если за отведённое время не пришло Confirm() - менеджер
///         автоматически откатывается к PreviousMode через RevertTo().</item>
/// </list>
/// Это копия поведения NVIDIA Control Panel и классической панели Windows.
/// </summary>
public sealed class DisplayModeManager : IDisposable
{
    /// <summary> Стандартный watchdog таймаут в секундах. </summary>
    public const int WatchdogSeconds = 15;

    private readonly AdlxContext _ctx;
    private System.Threading.Timer? _watchdogTimer;
    private DisplayChangeRequest? _pendingRequest;
    private DisplayWatchdogController? _pendingController;
    private readonly object _lock = new();

    /// <summary>
    /// Callback, который вызывает UI: показать модальный диалог подтверждения.
    /// Должен быть установлен до ApplyModeAsync. Если null - watchdog срабатывает
    /// автоматически (используется в --apply-profile сценарии, без UI).
    /// </summary>
    public DisplayWatchdogCallback? WatchdogCallback { get; set; }

    public int WatchdogTimeoutSeconds { get; set; } = WatchdogSeconds;

    public DisplayModeManager(AdlxContext ctx)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
    }

    // =================================================================
    //  Публичный API: apply / revert / read
    // =================================================================

    /// <summary>
    /// Асинхронно применяет новый режим с watchdog. Возвращает управление
    /// после того, как пользователь подтвердил или откатился.
    /// </summary>
    /// <param name="deviceName"> Имя устройства ("\\\\.\\DISPLAY1"). </param>
    /// <param name="newWidth"> Новое разрешение по X. </param>
    /// <param name="newHeight"> Новое разрешение по Y. </param>
    /// <param name="newRefreshHz"> Новая частота обновления. </param>
    /// <param name="newBpc"> Глубина цвета (8 или 10). </param>
    /// <param name="ct"> Токен отмены. </param>
    public Task<DisplayApplyResult> ApplyModeAsync(
        string deviceName,
        int newWidth, int newHeight, int newRefreshHz, int newBpc,
        CancellationToken ct = default)
    {
        // Сохраняем предыдущий режим
        var prev = User32Display.QueryCurrentMode(deviceName);

        var newMode = new User32Display.DEVMODE
        {
            dmSize              = (short)Marshal.SizeOf<User32Display.DEVMODE>(),
            dmPelsWidth         = newWidth,
            dmPelsHeight        = newHeight,
            dmDisplayFrequency  = newRefreshHz,
            dmBitsPerPel        = newBpc,
            dmFields            = User32Display.DM_PELSWIDTH | User32Display.DM_PELSHEIGHT
                                | User32Display.DM_DISPLAYFREQUENCY | User32Display.DM_BITSPERPEL
        };

        return ApplyModeCoreAsync(deviceName, prev, newMode, ct);
    }

    /// <summary> Синхронная версия для использования в CLI режиме (--apply-profile). </summary>
    public DisplayApplyResult ApplyMode(string deviceName, int newWidth, int newHeight,
        int newRefreshHz, int newBpc, bool autoConfirm = false)
    {
        if (autoConfirm)
            WatchdogCallback = null; // безопасно: если UI нет, watchdog сам откатит

        return ApplyModeAsync(deviceName, newWidth, newHeight, newRefreshHz, newBpc)
            .GetAwaiter().GetResult();
    }

    /// <summary> Текущий режим дисплея. </summary>
    public User32Display.DEVMODE QueryCurrent(string deviceName)
        => User32Display.QueryCurrentMode(deviceName);

    /// <summary> Перечисляет все устройства отображения через EnumDisplayDevices. </summary>
    public static List<(string DeviceName, string Description, bool Active)> EnumerateAdapters()
    {
        var result = new List<(string, string, bool)>();
        int i = 0;
        while (true)
        {
            var dd = new User32Display.DISPLAY_DEVICE { cb = Marshal.SizeOf<User32Display.DISPLAY_DEVICE>() };
            if (!User32Display.EnumDisplayDevices(IntPtr.Zero, i, ref dd, 0)) break;
            bool active = (dd.StateFlags & 0x00000001 /*DISPLAY_DEVICE_ATTACHED_TO_DESKTOP*/) != 0;
            result.Add((dd.DeviceName, dd.DeviceString, active));
            i++;
        }
        return result;
    }

    /// <summary> Возвращает список валидных режимов, отфильтрованных по ADLX capabilities. </summary>
    public IEnumerable<DisplayMode> GetFilteredModes(HardwareSnapshot snapshot, int displayIndex)
    {
        var scanner = new HardwareScanner(_ctx);
        var modes = scanner.GetValidResolutions(displayIndex);
        if (displayIndex < 0 || displayIndex >= snapshot.Displays.Count) return modes;
        var info = snapshot.Displays[displayIndex];
        int maxBpc = (int)info.CurrentColorDepth;
        if (maxBpc < (int)ColorDepthBits.Bpp8) maxBpc = 8;
        return scanner.FilterByBandwidth(modes, info.PortType, maxBpc);
    }

    // =================================================================
    //  Watchdog control
    // =================================================================

    internal void CancelWatchdog()
    {
        lock (_lock)
        {
            if (_watchdogTimer is not null)
            {
                _watchdogTimer.Dispose();
                _watchdogTimer = null;
            }
            _pendingRequest = null;
            _pendingController = null;
        }
    }

    internal void CancelWatchdogAndRevert()
    {
        DisplayChangeRequest? req;
        lock (_lock)
        {
            if (_watchdogTimer is not null)
            {
                _watchdogTimer.Dispose();
                _watchdogTimer = null;
            }
            req = _pendingRequest;
            _pendingRequest = null;
            _pendingController = null;
        }
        if (req is not null) DoRevert(req);
    }

    private async Task<DisplayApplyResult> ApplyModeCoreAsync(
        string deviceName,
        User32Display.DEVMODE previousMode,
        User32Display.DEVMODE newMode,
        CancellationToken ct)
    {
        // Шаг 1: Test mode - проверяем, что Windows примет этот DEVMODE
        int testRc = User32Display.ChangeDisplaySettingsEx(deviceName, ref newMode, IntPtr.Zero,
            User32Display.CDS_TEST, IntPtr.Zero);
        if (testRc != User32Display.DISP_CHANGE_SUCCESSFUL)
        {
            return new DisplayApplyResult(testRc, false, false,
                $"Тестовый вызов ChangeDisplaySettingsEx отклонил режим: код {testRc}");
        }

        // Шаг 2: Apply
        int applyRc = User32Display.ApplyMode(deviceName, newMode, persistToRegistry: false);
        if (applyRc != User32Display.DISP_CHANGE_SUCCESSFUL)
        {
            // Сразу откатываемся к previous
            User32Display.RevertTo(deviceName, previousMode);
            return new DisplayApplyResult(applyRc, false, true,
                $"ChangeDisplaySettingsEx вернул {applyRc}; выполнен откат к предыдущему режиму.");
        }

        // Шаг 3: Watchdog
        var request = new DisplayChangeRequest(deviceName, previousMode, newMode);
        var controller = new DisplayWatchdogController(this, request);
        var tcs = new TaskCompletionSource<DisplayApplyResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock)
        {
            _pendingRequest  = request;
            _pendingController = controller;
            _watchdogTimer = new System.Threading.Timer(_ => OnWatchdogTick(tcs), null,
                WatchdogTimeoutSeconds * 1000, Timeout.Infinite);
        }

        // Уведомляем UI
        if (WatchdogCallback is not null)
        {
            // UI callback выполняется синхронно в этом потоке - обычно показывает
            // модальное окно. После решения пользователя вызывает controller.Confirm() / .Revert().
            // Мы отписываемся через continuation, которое завершает tcs.
            WatchdogCallback.Invoke(request, controller);

            // Если callback вернулся с решением - завершаем
            if (controller.IsConfirmed)
            {
                tcs.TrySetResult(new DisplayApplyResult(User32Display.DISP_CHANGE_SUCCESSFUL,
                    true, false, "Пользователь подтвердил новый режим."));
            }
            else if (controller.IsReverted)
            {
                tcs.TrySetResult(new DisplayApplyResult(User32Display.DISP_CHANGE_SUCCESSFUL,
                    false, true, "Пользователь отменил; выполнен откат."));
            }
            // Если ещё не решили - ждём watchdog tick
        }
        else
        {
            // CLI режим без UI: watchdog сам откатит через WatchdogTimeoutSeconds.
        }

        // Регистрируем continuation, чтобы вернуть результат
        using (ct.Register(() =>
        {
            CancelWatchdogAndRevert();
            tcs.TrySetResult(new DisplayApplyResult(User32Display.DISP_CHANGE_SUCCESSFUL,
                false, true, "Отмена; выполнен откат."));
        }))
        {
            return await tcs.Task.ConfigureAwait(false);
        }
    }

    private void OnWatchdogTick(TaskCompletionSource<DisplayApplyResult> tcs)
    {
        // Таймер сработал - пользователь не подтвердил за WatchdogTimeoutSeconds
        DisplayChangeRequest? req;
        lock (_lock)
        {
            if (_watchdogTimer is null) return;
            _watchdogTimer.Dispose();
            _watchdogTimer = null;
            req = _pendingRequest;
            _pendingRequest = null;
            _pendingController = null;
        }
        if (req is not null) DoRevert(req);

        tcs.TrySetResult(new DisplayApplyResult(User32Display.DISP_CHANGE_SUCCESSFUL,
            false, true,
            $"Watchdog: пользователь не подтвердил изменение за {WatchdogTimeoutSeconds} с. Выполнен автоматический откат."));
    }

    private static void DoRevert(DisplayChangeRequest req)
    {
        try
        {
            int rc = User32Display.RevertTo(req.DeviceName, req.PreviousMode);
            Trace.WriteLine($"[DisplayModeManager] Reverted to {req.PreviousMode.dmPelsWidth}x{req.PreviousMode.dmPelsHeight}@{req.PreviousMode.dmDisplayFrequency}, rc={rc}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[DisplayModeManager] Revert failed: {ex.Message}");
        }
    }

    // =================================================================
    //  SetDisplayScalingMode / PixelFormat / ColorDepth через ADLX
    //  (эти операции атомарны и не требуют watchdog, т.к. они не
    //   затрагивают timing сигнала и не дают чёрного экрана).
    // =================================================================

    public bool SetScalingMode(int displayIndex, ScalingMode mode)
    {
        int rc = AdlxBridgeNative.ADLXBridge_SetDisplayScalingMode(displayIndex, (int)mode);
        return rc == AdlxBridgeNative.BRIDGE_OK;
    }

    public bool SetPixelFormat(int displayIndex, PixelFormat format)
    {
        int rc = AdlxBridgeNative.ADLXBridge_SetDisplayPixelFormat(displayIndex, (int)format);
        return rc == AdlxBridgeNative.BRIDGE_OK;
    }

    public bool SetColorDepth(int displayIndex, ColorDepthBits depth)
    {
        int rc = AdlxBridgeNative.ADLXBridge_SetDisplayColorDepth(displayIndex, (int)depth);
        return rc == AdlxBridgeNative.BRIDGE_OK;
    }

    public void Dispose()
    {
        CancelWatchdog();
    }
}
