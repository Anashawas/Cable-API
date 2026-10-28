namespace Cable.Core.Exceptions;

/// <summary>
/// 422: the request is well-formed but a business rule refuses it (not enough points, out of stock, promo
/// not applicable…). <see cref="Code"/> is a stable UPPER_SNAKE or camelCase code the apps switch on; the message
/// is ready to show. Optional <see cref="Details"/> carry figures the app may display (e.g. distanceMeters).
/// </summary>
public class UnprocessableException(string code, string message, IReadOnlyDictionary<string, object?>? details = null) : Exception(message)
{
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, object?>? Details { get; } = details;
}
