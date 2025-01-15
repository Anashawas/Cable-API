using Cable.Core.Localization;

namespace Cable.Core;

public class CableApplicationException : Exception
{
    public CableApplicationException() : base(Resources.ApplicationFailure)
    {
    }

    public CableApplicationException(string message) : base(message)
    {
    }
}