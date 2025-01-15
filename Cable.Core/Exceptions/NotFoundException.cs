using Cable.Core.Localization;

namespace Cable.Core.Exceptions;

public class NotFoundException : Exception
{
    public NotFoundException() : base(Resources.NotFound)
    {
    }

    public NotFoundException(string message): base(message)
    {
    }

    public NotFoundException(string message, Exception innerException): base(message, innerException)
    {
    }

    public NotFoundException(string name, object key)
        : base($"\"{name}\" ({key}) ${Resources.WasNotFound}.")
    {
    }
}