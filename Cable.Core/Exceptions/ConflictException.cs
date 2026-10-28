namespace Cable.Core.Exceptions;

/// <summary>
/// 409: the request clashes with the current state (e.g. the driver already has a running session).
/// <see cref="Code"/> is a stable, camelCase code the apps switch on; the message is ready to show.
/// </summary>
public class ConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
