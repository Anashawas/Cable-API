namespace Cable.Core.Exceptions;

/// <summary>
/// 404 with a stable <see cref="Code"/> the apps switch on (e.g. CODE_NOT_FOUND for an unknown sticker code),
/// when "not found" is an expected answer the app must tell apart from a missing route. The message is ready to show.
/// </summary>
public class CodedNotFoundException(string code, string message) : NotFoundException(message)
{
    public string Code { get; } = code;
}
