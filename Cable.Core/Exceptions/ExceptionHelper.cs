using System.Runtime.CompilerServices;

namespace Cable.Core.Helpers;

public static class ExceptionHelper
{
    public static void ThrowIfNullOrEmpty(string argument, [CallerArgumentExpression("argument")] string paramName = null)
    {
        if (String.IsNullOrEmpty(argument))
        {
            throw new ArgumentNullException(paramName);
        }
    }
}