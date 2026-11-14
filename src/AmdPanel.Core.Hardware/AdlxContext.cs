using System.Runtime.InteropServices;
using System.Text;
using AmdPanel.Core.Hardware.Exceptions;

namespace AmdPanel.Core.Hardware;

/// <summary>
/// Адаптивный выбор стратегии инициализации ADLX.
/// <para>
/// <b>Auto</b> - сначала пробуем обычный Initialize, при ошибке (legacy
/// драйвер Polaris / несоответствие ADLX версии) переходим на
/// InitializeWithIncompatibleDriver. Это поведение по умолчанию и
/// покрывает обе ветки поддержки AMD.
/// </para>
/// </summary>
public enum AdlxInitializationStrategy
{
    Auto,
    ModernOnly,
    LegacyOnly,
}

/// <summary>
/// Точка входа в ADLX. Обёртка над плоской C-библиотекой
/// AmdPanel.NativeBridge.dll, которая в свою очередь динамически
/// загружает amdadlx64.dll из системной папки Windows.
/// </summary>
/// <remarks>
/// Использование:
/// <code>
/// using var ctx = new AdlxContext();
/// ctx.Initialize();
/// var scanner = new HardwareScanner(ctx);
/// var snapshot = scanner.Scan();
/// </code>
/// Класс реализует <see cref="IDisposable"/>: при освобождении вызывается
/// ADLXBridge_Terminate. Повторное создание экземпляра в одном процессе
/// безопасно - в C++ стороне g_initialized проверяется.
/// </remarks>
public sealed class AdlxContext : IDisposable
{
    private static readonly object s_initLock = new();
    private static bool s_initialized;
    private readonly bool _ownsLifetime;

    /// <summary> true, если ADLX успешно инициализирована в текущем процессе. </summary>
    public static bool IsProcessInitialized => Volatile.Read(ref s_initialized);

    /// <summary> Версия ADLX SDK в виде строки (например "23.20.0"). </summary>
    public string AdlxVersion { get; private set; } = string.Empty;

    public AdlxContext() : this(AdlxInitializationStrategy.Auto) { }

    public AdlxContext(AdlxInitializationStrategy strategy)
    {
        _ownsLifetime = true;
        Initialize(strategy);
    }

    /// <summary> Инициализирует ADLX согласно выбранной стратегии. </summary>
    /// <exception cref="AdlxLibraryNotFoundException"> amdadlx64.dll / мост не найдены. </exception>
    /// <exception cref="AdlxInitializationException"> ADLXInitialize вернул ошибку. </exception>
    public void Initialize(AdlxInitializationStrategy strategy = AdlxInitializationStrategy.Auto)
    {
        lock (s_initLock)
        {
            if (s_initialized)
            {
                RefreshVersionString();
                return;
            }

            int code;
            if (strategy == AdlxInitializationStrategy.LegacyOnly)
            {
                code = Native.AdlxBridgeNative.ADLXBridge_InitializeWithIncompatibleDriver();
            }
            else if (strategy == AdlxInitializationStrategy.ModernOnly)
            {
                code = Native.AdlxBridgeNative.ADLXBridge_Initialize();
            }
            else // Auto
            {
                code = Native.AdlxBridgeNative.ADLXBridge_Initialize();
                if (code != Native.AdlxBridgeNative.BRIDGE_OK)
                {
                    // Fallback на legacy-инициализацию: покрывает случай,
                    // когда установлен старый драйвер Polaris без Adrenalin.
                    code = Native.AdlxBridgeNative.ADLXBridge_InitializeWithIncompatibleDriver();
                }
            }

            if (code != Native.AdlxBridgeNative.BRIDGE_OK)
            {
                throw new AdlxInitializationException(code,
                    $"ADLX инициализация не удалась (стратегия={strategy}, код={code}). " +
                    Native.AdlxBridgeNative.Describe(code) +
                    " Убедитесь, что установлен драйвер AMD и amdadlx64.dll присутствует в System32.");
            }

            Volatile.Write(ref s_initialized, true);
            RefreshVersionString();
        }
    }

    private void RefreshVersionString()
    {
        try
        {
            IntPtr ptr = Native.AdlxBridgeNative.ADLXBridge_QueryVersion();
            AdlxVersion = ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
        }
        catch
        {
            AdlxVersion = string.Empty;
        }
    }

    /// <summary> Корректно освобождает ADLX (вызывает ADLXTerminate). </summary>
    public void Terminate()
    {
        lock (s_initLock)
        {
            if (!s_initialized) return;
            try { Native.AdlxBridgeNative.ADLXBridge_Terminate(); } catch { /* ignore */ }
            Volatile.Write(ref s_initialized, false);
        }
    }

    public void Dispose()
    {
        if (_ownsLifetime) Terminate();
    }

    // =================================================================
    //  Статические помощники для конвертации byte[] <-> string (UTF-8).
    //  Используются HardwareScanner'ом и другими потребителями структур.
    // =================================================================

    /// <summary> Декодирует блочный byte[] из native struct в UTF-8 строку до первого NUL. </summary>
    public static string BytesToUtf8(byte[]? buffer)
    {
        if (buffer is null || buffer.Length == 0) return string.Empty;
        int nul = Array.IndexOf(buffer, (byte)0);
        int len = nul < 0 ? buffer.Length : nul;
        return len == 0 ? string.Empty : Encoding.UTF8.GetString(buffer, 0, len);
    }

    /// <summary> MarshalHelper: читает BridgeDisplayMode[] из неуправляемого буфера. </summary>
    public static Native.BridgeDisplayMode[] ReadDisplayModeArray(IntPtr ptr, int count)
    {
        if (ptr == IntPtr.Zero || count <= 0)
            return Array.Empty<Native.BridgeDisplayMode>();
        int size = Marshal.SizeOf<Native.BridgeDisplayMode>();
        var arr = new Native.BridgeDisplayMode[count];
        for (int i = 0; i < count; i++)
        {
            arr[i] = Marshal.PtrToStructure<Native.BridgeDisplayMode>(ptr + i * size);
        }
        return arr;
    }

    /// <summary> MarshalHelper: читает int[] из неуправляемого буфера. </summary>
    public static int[] ReadInt32Array(IntPtr ptr, int count)
    {
        if (ptr == IntPtr.Zero || count <= 0) return Array.Empty<int>();
        var arr = new int[count];
        Marshal.Copy(ptr, arr, 0, count);
        return arr;
    }
}
