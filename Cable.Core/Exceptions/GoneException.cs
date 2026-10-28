namespace Cable.Core.Exceptions;

/// <summary>
/// 410: the thing existed but was withdrawn for good (e.g. CODE_REVOKED for a sticker code that was revoked and
/// renewed). <see cref="Code"/> is a stable code the apps switch on; the message is ready to show.
/// </summary>
public class GoneException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
