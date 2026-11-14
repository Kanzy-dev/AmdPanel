namespace AmdPanel.Core.DriverManager.Exceptions;

public class DriverManagerException : Exception
{
    public DriverManagerException(string message) : base(message) { }
    public DriverManagerException(string message, Exception inner) : base(message, inner) { }
}

public sealed class SevenZipNotFoundException : DriverManagerException
{
    public SevenZipNotFoundException()
        : base("Не найден 7z.exe для распаковки официального .exe дистрибутива AMD. " +
               "Установите 7-Zip (https://www.7-zip.org) или NuGet-пакет SharpCompress должен быть добавлен в проект.") { }
}

public sealed class DriverDownloadException : DriverManagerException
{
    public System.Net.HttpStatusCode? StatusCode { get; }
    public DriverDownloadException(string message, System.Net.HttpStatusCode? code = null) : base(message)
    {
        StatusCode = code;
    }
}

public sealed class InfFileNotFoundException : DriverManagerException
{
    public InfFileNotFoundException(string searchedPath)
        : base($"В распакованном дистрибутиве не найден .inf файл драйвера AMD по пути: {searchedPath}") { }
}

public sealed class PnpUtilFailedException : DriverManagerException
{
    public int ExitCode { get; }
    public string PnpUtilOutput { get; }
    public PnpUtilFailedException(int exitCode, string output, string message) : base(message)
    {
        ExitCode = exitCode;
        PnpUtilOutput = output;
    }
}

public sealed class AdlxLibraryMissingException : DriverManagerException
{
    public AdlxLibraryMissingException()
        : base("После установки драйвера amdadlx64.dll не найден в C:\\Windows\\System32. " +
               "Возможно, версия драйвера слишком старая и не содержит ADLX. " +
               "Панель не сможет управлять GPU до установки ADLX-совместимого драйвера.") { }
}
