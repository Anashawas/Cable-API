namespace Cable.Ocpp.Protocol;

/// <summary>A handler's way of answering CALLERROR instead of CALLRESULT.</summary>
public sealed class OcppCallErrorException(string errorCode, string description) : Exception(description)
{
    public string ErrorCode { get; } = errorCode;
}
