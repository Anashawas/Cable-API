using Cable.Core.Localization;

namespace Cable.Core;

public class NotAuthorizedAccessException:Exception
{
    public NotAuthorizedAccessException() : base(Resources.Unauthorized)
    {
    }

    public NotAuthorizedAccessException(string message) : base(message)
    {
    }
}
