namespace Cable.Core.Exceptions;

/// <summary>
/// A business refusal that needs an HTTP status the other coded exceptions do not cover (403 with a reason the
/// app can switch on, 423 for a locked login, 401 with a code…). <see cref="Code"/> is stable, the message is
/// ready to show, and <see cref="Details"/> are extra figures for the problem body (e.g. lockedUntil).
/// </summary>
public class CodedHttpException(int status, string code, string message, IReadOnlyDictionary<string, object?>? details = null)
    : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, object?>? Details { get; } = details;
}
