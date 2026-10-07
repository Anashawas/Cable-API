using System.Text.Json;

namespace Cable.Ocpp.Protocol;

/// <summary>
/// OCPP-J 1.6 wire format (RPC framework spec §4):
///   CALL       [2, "uniqueId", "Action", {payload}]
///   CALLRESULT [3, "uniqueId", {payload}]
///   CALLERROR  [4, "uniqueId", "errorCode", "description", {details}]
/// </summary>
public static class OcppMessageType
{
    public const int Call = 2;
    public const int CallResult = 3;
    public const int CallError = 4;
}

public static class OcppErrorCode
{
    public const string NotImplemented = "NotImplemented";
    public const string NotSupported = "NotSupported";
    public const string InternalError = "InternalError";
    public const string ProtocolError = "ProtocolError";
    public const string SecurityError = "SecurityError";
    public const string FormationViolation = "FormationViolation";
    public const string PropertyConstraintViolation = "PropertyConstraintViolation";
    public const string OccurenceConstraintViolation = "OccurenceConstraintViolation";
    public const string TypeConstraintViolation = "TypeConstraintViolation";
    public const string GenericError = "GenericError";
}

/// <summary>A parsed inbound frame. Only one of Call / Result / Error is set.</summary>
public sealed class OcppInbound
{
    public int MessageType { get; init; }
    public string UniqueId { get; init; } = "";
    public string? Action { get; init; }
    public JsonElement Payload { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorDescription { get; init; }
}

public static class OcppFrame
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        // OCPP timestamps are ISO-8601 UTC. DateTime.UtcNow serialises with a trailing Z.
        WriteIndented = false,
    };

    /// <summary>
    /// Parses one frame. Returns false with an error code + description when the
    /// frame is not a valid OCPP-J array; the caller answers CALLERROR when it can
    /// recover the uniqueId, otherwise it just logs.
    /// </summary>
    public static bool TryParse(string text, out OcppInbound? frame, out string? errorCode, out string? errorDescription)
    {
        frame = null;
        errorCode = null;
        errorDescription = null;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            errorCode = OcppErrorCode.FormationViolation;
            errorDescription = $"Not valid JSON: {ex.Message}";
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 3)
            {
                errorCode = OcppErrorCode.FormationViolation;
                errorDescription = "Frame must be a JSON array of at least 3 elements";
                return false;
            }

            if (root[0].ValueKind != JsonValueKind.Number || !root[0].TryGetInt32(out var type))
            {
                errorCode = OcppErrorCode.FormationViolation;
                errorDescription = "MessageTypeId must be a number";
                return false;
            }

            var uniqueId = root[1].ValueKind == JsonValueKind.String ? root[1].GetString() ?? "" : root[1].ToString();

            switch (type)
            {
                case OcppMessageType.Call when root.GetArrayLength() >= 4 && root[2].ValueKind == JsonValueKind.String:
                    frame = new OcppInbound
                    {
                        MessageType = type,
                        UniqueId = uniqueId,
                        Action = root[2].GetString(),
                        Payload = root[3].Clone(),
                    };
                    return true;

                case OcppMessageType.CallResult:
                    frame = new OcppInbound { MessageType = type, UniqueId = uniqueId, Payload = root[2].Clone() };
                    return true;

                case OcppMessageType.CallError when root.GetArrayLength() >= 4:
                    frame = new OcppInbound
                    {
                        MessageType = type,
                        UniqueId = uniqueId,
                        ErrorCode = root[2].ToString(),
                        ErrorDescription = root[3].ToString(),
                        Payload = root.GetArrayLength() >= 5 ? root[4].Clone() : default,
                    };
                    return true;

                default:
                    errorCode = OcppErrorCode.FormationViolation;
                    errorDescription = $"Unsupported or malformed MessageTypeId {type}";
                    return false;
            }
        }
    }

    /// <summary>Server-initiated CALL (phase 2 control messages).</summary>
    public static string Call(string uniqueId, string action, object payload) =>
        JsonSerializer.Serialize(new object[] { OcppMessageType.Call, uniqueId, action, payload }, Json);

    public static string CallResult(string uniqueId, object payload) =>
        JsonSerializer.Serialize(new object[] { OcppMessageType.CallResult, uniqueId, payload }, Json);

    public static string CallError(string uniqueId, string errorCode, string description) =>
        JsonSerializer.Serialize(new object[] { OcppMessageType.CallError, uniqueId, errorCode, description, new { } }, Json);
}
