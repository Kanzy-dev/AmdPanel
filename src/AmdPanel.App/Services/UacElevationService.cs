using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AmdPanel.App.Services;

/// <summary>
/// Управление правами UAC по требованию (Requirement 3).
/// </summary>
/// <remarks>
/// Приложение запускается как <c>asInvoker</c> (см. app.manifest).
/// Большинство операций ADLX (SetScalingMode, SetColorDepth, SetFreeSync,
/// настройка 3D parameters) работает без повышения прав, потому что
/// ADLX сам пишет в HKLM от имени драйвера AMD kernel-mode компонента.
/// Однако некоторые операции требуют записи в HKLM\SYSTEM\...\Class\XXXX\UMD
/// напрямую (Polaris-legacy scenarios) или вызывают <c>pnputil.exe</c>.
/// </summary>
public static class UacElevationService
{
    /// <summary> True, если текущий процесс уже запущен с правами администратора. </summary>
    public static bool IsRunningElevated
    {
        get
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                return new System.Security.Principal.WindowsPrincipal(identity)
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// Запускает дочерний процесс (по умолчанию - сам AmdPanel.exe) с правами
    /// администратора через ShellExecuteEx с глаголом "runas".
    /// </summary>
    /// <param name="arguments"> Аргументы командной строки (например "--apply-elevated C:\tmp\cmd.json"). </param>
    /// <param name="exePath"> Путь к исполняемому файлу. По умолчанию - текущий процесс. </param>
    /// <returns> true, если запуск инициирован (пользователь подтвердил UAC). </returns>
    public static bool LaunchElevated(string arguments, string? exePath = null)
    {
        exePath ??= Environment.ProcessPath!;
        if (exePath is null) return false;

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",           // UAC prompt
            WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
            WindowStyle = ProcessWindowStyle.Normal
        };
        try
        {
            using var p = Process.Start(startInfo);
            return p is not null;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // NativeErrorCode 1223 = ERROR_CANCELLED (пользователь нажал "Нет" в UAC)
            if (ex.NativeErrorCode != 1223)
                MessageBox.Show(ex.Message, "AmdPanel", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    /// <summary>
    /// Возвращает встроенный UAC shield icon как ImageSource для отображения
    /// на кнопках, требующих повышения прав. Иконка загружается через
    /// user32.LoadIcon с IDI_SHIELD.
    /// </summary>
    public static ImageSource? GetShieldIcon()
    {
        try
        {
            const int IDI_SHIELD = 32518;
            IntPtr hIcon = LoadIcon(IntPtr.Zero, IDI_SHIELD);
            if (hIcon == IntPtr.Zero) return null;
            var bmp = Imaging.CreateBitmapSourceFromHIcon(
                hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, int lpIconName);
}
