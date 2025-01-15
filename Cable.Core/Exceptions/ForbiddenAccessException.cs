using Cable.Core.Localization;

namespace Cable.Core;

public class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException() : base(Resources.Forbidden)
    {
    }

    public ForbiddenAccessException(string message) : base(message)
    {
    }
}
