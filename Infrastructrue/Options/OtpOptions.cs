namespace Infrastructrue.Options;

public class OtpOptions
{
    public const string ConfigName = "OtpSettings";
    
    public int ExpiryMinutes { get; set; } = 5;
    public int MaxAttempts { get; set; } = 3;
    public int RateLimitMinutes { get; set; } = 1;
    public int MaxRequestsPerWindow { get; set; } = 1;

    /// <summary>
    /// Demo/review accounts (e.g. the Apple App Store review team). Phone numbers listed
    /// here never receive a real SMS and always use <see cref="TestOtp"/> as their code.
    /// Store numbers in normalized form, e.g. "962790000000".
    /// Leave EMPTY to disable the bypass entirely (do this once review is approved).
    /// </summary>
    public string[] TestPhoneNumbers { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Fixed OTP handed to the numbers in <see cref="TestPhoneNumbers"/>.
    /// MUST be exactly 6 digits — the request validators enforce <c>^\d{6}$</c>,
    /// so a shorter code would be rejected with a 400 before reaching this service.
    /// </summary>
    public string TestOtp { get; set; } = "000000";

    /// <summary>
    /// DEVELOPMENT ONLY. When true, EVERY phone number skips the SMS gateway and uses
    /// <see cref="TestOtp"/> as its code — so dev/QA never burns paid SMS credits.
    ///
    /// !! NEVER set this to true in appsettings.Production.json !!
    /// Doing so would let anyone sign in as any user with the fixed code.
    /// Production must rely on <see cref="TestPhoneNumbers"/> (a strict allow-list) instead.
    /// </summary>
    public bool TestModeForAllNumbers { get; set; } = false;
}