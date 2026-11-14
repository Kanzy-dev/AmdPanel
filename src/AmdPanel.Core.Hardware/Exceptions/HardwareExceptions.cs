namespace AmdPanel.Core.Hardware.Exceptions;

/// <summary>
/// Базовая ошибка слоя Core.Hardware. Любая ошибка ADLX / Win32 API
/// оборачивается в этот класс или его наследников, чтобы上层 UI мог
/// ловить всё одним catch.
/// </summary>
public class HardwareException : Exception
{
    public HardwareException(string message) : base(message) { }
    public HardwareException(string message, Exception inner) : base(message, inner) { }
}

/// <summary> amdadlx64.dll / AmdPanel.NativeBridge.dll не найдены или не загрузились. </summary>
public sealed class AdlxLibraryNotFoundException : HardwareException
{
    public AdlxLibraryNotFoundException(string message) : base(message) { }
}

/// <summary> ADLXInitialize / ADLXTerminate вернули ошибку. </summary>
public sealed class AdlxInitializationException : HardwareException
{
    public int BridgeResultCode { get; }
    public AdlxInitializationException(int code, string message) : base(message)
    {
        BridgeResultCode = code;
    }
}

/// <summary> На машине нет ни одного AMD GPU, видимого ADLX. </summary>
public sealed class NoAmdGpuException : HardwareException
{
    public NoAmdGpuException() : base("AMD GPU не обнаружен. Убедитесь, что установлен драйвер AMD (Driver-Only) и в системе присутствует видеокарта с PCI Vendor ID 0x1002.") { }
}

/// <summary> Запрошенная возможность не поддерживается железом. </summary>
public sealed class CapabilityNotSupportedException : HardwareException
{
    public CapabilityNotSupportedException(string feature) : base($"Функция \"{feature}\" не поддерживается данным GPU.") { }
}

/// <summary> Display watchdog: пользователь не подтвердил новый режим дисплея за отведённое время, был выполнён откат. </summary>
public sealed class DisplayWatchdogRevertedException : HardwareException
{
    public DisplayWatchdogRevertedException(string message) : base(message) { }
}
