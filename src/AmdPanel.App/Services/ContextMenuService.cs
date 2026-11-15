using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using AmdPanel.Core.Profiles.Models;
using AmdPanel.Core.Profiles.Services;

namespace AmdPanel.App.Services;

/// <summary>
/// Сервис управления пунктом "Панель управления AMD" в контекстном меню рабочего стола
/// </summary>
public sealed class ContextMenuService : IDisposable
{
    private const string RegistryKeyPath = @"Software\Classes\Directory\Background\shell\AmdPanel";
    private const string CommandKeyPath = @"Software\Classes\Directory\Background\shell\AmdPanel\command";
    private readonly IAppConfigService _configService;
    private readonly string _appPath;

    public ContextMenuService(IAppConfigService configService)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _appPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AmdPanel.exe");
    }

    /// <summary>
    /// Проверяет, добавлен ли пункт в контекстное меню
    /// </summary>
    public bool IsContextMenuEnabled()
    {
        var settings = _configService.LoadAppSettings();
        return settings.EnableDesktopContextMenu;
    }

    /// <summary>
    /// Включает или выключает пункт в контекстном меню
    /// </summary>
    public void SetContextMenuEnabled(bool enabled)
    {
        var settings = _configService.LoadAppSettings();
        settings.EnableDesktopContextMenu = enabled;
        _configService.SaveAppSettings(settings);

        if (enabled)
        {
            AddContextMenu();
        }
        else
        {
            RemoveContextMenu();
        }
    }

    /// <summary>
    /// Добавляет пункт в контекстное меню рабочего стола
    /// </summary>
    private void AddContextMenu()
    {
        try
        {
            // Основной ключ с названием и иконкой
            using (var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath))
            {
                var settings = _configService.LoadAppSettings();
                var menuText = settings.Language == "ru" ? "Панель управления AMD" : "AMD Control Panel";
                
                key.SetValue(null, menuText);
                key.SetValue("Icon", $"{_appPath},0");
            }

            // Ключ с командой запуска
            using (var key = Registry.CurrentUser.CreateSubKey(CommandKeyPath))
            {
                key.SetValue(null, $"\"{_appPath}\"");
            }

            // Требуется уведомление Explorer о изменении контекстного меню
            NotifyShellOfChange();
        }
        catch (Exception ex)
        {
            // Логируем ошибку, но не прерываем выполнение
            System.Diagnostics.Trace.WriteLine($"Failed to add context menu: {ex.Message}");
        }
    }

    /// <summary>
    /// Удаляет пункт из контекстного меню рабочего стола
    /// </summary>
    private void RemoveContextMenu()
    {
        try
        {
            // Удаляем ключ с командой
            Registry.CurrentUser.DeleteSubKeyTree(CommandKeyPath, false);

            // Удаляем основной ключ
            Registry.CurrentUser.DeleteSubKeyTree(RegistryKeyPath, false);

            // Требуется уведомление Explorer о изменении контекстного меню
            NotifyShellOfChange();
        }
        catch (Exception ex)
        {
            // Логируем ошибку, но не прерываем выполнение
            System.Diagnostics.Trace.WriteLine($"Failed to remove context menu: {ex.Message}");
        }
    }

    /// <summary>
    /// Уведомляет Explorer о изменениях в контекстном меню
    /// </summary>
    private static void NotifyShellOfChange()
    {
        try
        {
            // Принудительно обновляем оболочку Windows
            if (Environment.OSVersion.Version >= new Version(6, 0)) // Windows Vista и выше
            {
                var notifyPtr = new IntPtr(0x8000000); // HWND_BROADCAST
                var message = 0x0015; // WM_SETTINGCHANGE
                var wParam = new IntPtr(0); // SPI_SETNONCLIENTREGISTRY
                var lParam = Marshal.StringToHGlobalAuto("Environment");

                // Отправляем сообщение всем окнам
                NativeMethods.SendMessageTimeout(notifyPtr, message, wParam, lParam,
                    NativeMethods.SMTO_BLOCK | NativeMethods.SMTO_ABORTIFHUNG,
                    5000, out _);

                Marshal.FreeHGlobal(lParam);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Failed to notify shell: {ex.Message}");
        }
    }

    public void Dispose()
    {
        // Нет ресурсов для очистки
    }
}

/// <summary>
/// Win32 API методы для работы с контекстным меню
/// </summary>
internal static class NativeMethods
{
    public const int WM_SETTINGCHANGE = 0x0015;
    public const uint SMTO_BLOCK = 0x0001;
    public const uint SMTO_ABORTIFHUNG = 0x0002;
    public const uint SMTO_NOTIMEOUTIFNOTHUNG = 0x0008;

    [DllImport("user32.dll", SetLastError = false, CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
}