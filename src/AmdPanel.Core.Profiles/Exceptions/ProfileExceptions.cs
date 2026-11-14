namespace AmdPanel.Core.Profiles.Exceptions;

public class ProfileException : Exception
{
    public ProfileException(string message) : base(message) { }
    public ProfileException(string message, Exception inner) : base(message, inner) { }
}

public sealed class ProfileNotFoundException : ProfileException
{
    public ProfileNotFoundException(string profileKey)
        : base($"Профиль с ключом '{profileKey}' не найден в репозитории.") { }
}

public sealed class ProfileCorruptedException : ProfileException
{
    public ProfileCorruptedException(string path, Exception inner)
        : base($"Профиль '{path}' повреждён и не может быть десериализован.", inner) { }
}

public sealed class ProfileRegistryApplyException : ProfileException
{
    public ProfileRegistryApplyException(string message) : base(message) { }
    public ProfileRegistryApplyException(string message, Exception innerException) : base(message, innerException) { }
}
