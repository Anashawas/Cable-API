namespace Application.Common.Models;

/// <summary>
/// The cached state between the password step and the OTP step of a partner login (owner by email, worker by
/// phone). Keyed by the session token the app carries between the calls.
/// </summary>
public class ProviderAuthSession
{
    public int UserId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// <summary>True for POST /api/provider/worker-login: verify-otp then issues a trusted-device token and marks the first login.</summary>
    public bool IsWorkerLogin { get; set; }

    /// <summary>The app-chosen device id sent with a worker login, stored with the trusted device.</summary>
    public string? DeviceId { get; set; }
}
