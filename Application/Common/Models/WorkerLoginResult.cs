namespace Application.Common.Models;

/// <summary>
/// POST /api/provider/worker-login. Either the OTP step is needed (<see cref="OtpRequired"/> true: send
/// <see cref="SessionToken"/> to send-otp / verify-otp as the owner login does) or the device was trusted and
/// <see cref="Login"/> already holds the tokens.
/// </summary>
public record WorkerLoginResult(
    bool OtpRequired,
    string? SessionToken,
    string? PhoneMasked,
    DateTime? ExpiresAt,
    /// <summary>How the OTP goes out: "sms" (WhatsApp later).</summary>
    string? Channel,
    UserLoginResult? Login);
