using System.Windows;
using System.Windows.Threading;
using AmdPanel.Core.Hardware;

namespace AmdPanel.App.Services;

/// <summary>
/// Сервис watchdog-окна подтверждения изменений дисплея.
/// Все обращения к UI-элементам strictly через Application.Current.Dispatcher.Invoke,
/// потому что System.Threading.Timer срабатывает в фоновом (не UI) потоке,
/// а PropertyChange + Window.Close требуют Dispatcher.
/// </summary>
public sealed class WatchdogDialogService
{
    private Views.WatchdogDialog? _currentDialog;
    private DisplayWatchdogController? _currentController;

    /// <summary>
    /// Регистрируется как DisplayModeManager.WatchdogCallback. Запускается
    /// в фоновом потоке (откуда зовёт ADLX) - поэтому всё пробрасываем
    /// в Dispatcher UI-потока.
    /// </summary>
    public void OnDisplayChangeRequest(DisplayChangeRequest request, DisplayWatchdogController controller)
    {
        Application.Current?.Dispatcher.Invoke(() => ShowDialogOnUiThread(request, controller));
    }

    private void ShowDialogOnUiThread(DisplayChangeRequest request, DisplayWatchdogController controller)
    {
        _currentController = controller;
        _currentDialog = new Views.WatchdogDialog
        {
            Owner = Application.Current?.MainWindow,
            DisplayDevice = request.DeviceName,
            NewModeDescription = $"{request.NewMode.dmPelsWidth}×{request.NewMode.dmPelsHeight} @ {request.NewMode.dmDisplayFrequency} Hz · {request.NewMode.dmBitsPerPel} bpc",
            OldModeDescription = $"{request.PreviousMode.dmPelsWidth}×{request.PreviousMode.dmPelsHeight} @ {request.PreviousMode.dmDisplayFrequency} Hz · {request.PreviousMode.dmBitsPerPel} bpc",
            TotalSeconds = DisplayModeManager.WatchdogSeconds,
        };
        _currentDialog.Confirmed += (_, _) =>
        {
            controller.Confirm();
            CloseCurrent();
        };
        _currentDialog.Reverted += (_, _) =>
        {
            controller.Revert();
            CloseCurrent();
        };
        _currentDialog.ShowDialog();
    }

    /// <summary> Закрывает модальное окно (вызывается после Confirm/Revert). </summary>
    private void CloseCurrent()
    {
        if (_currentDialog is null) return;
        var dlg = _currentDialog;
        _currentDialog = null;
        _currentController = null;
        // Безопасно закрываем: диалог умирает в UI-потоке
        if (!dlg.Dispatcher.CheckAccess())
            dlg.Dispatcher.Invoke(() => dlg.Close());
        else
            dlg.Close();
    }
}
