namespace Cable.Core.Constants;

/// <summary>
/// Identifies which client application a JWT was issued to, so the two apps
/// (Cable consumer app and the Provider/Worker app) can hold independent
/// sessions for the same user account instead of evicting each other.
///
/// The value is derived server-side from the login endpoint that was called
/// (/api/users/* vs /api/provider/*), so no client change is required.
/// </summary>
public static class AuthApps
{
    /// <summary>JWT claim carrying the issuing application.</summary>
    public const string ClaimType = "app";

    /// <summary>
    /// Optional request header a client may send at provider login to identify
    /// itself. Only the partner WEB portal sends it; the mobile apps send
    /// nothing and default per endpoint (consumer or provider).
    /// </summary>
    public const string ClientHeader = "X-Client-App";

    /// <summary>Cable consumer app — validated against UserAccount.SecurityStamp.</summary>
    public const string Consumer = "consumer";

    /// <summary>Provider/Worker MOBILE app — validated against UserAccount.ProviderSecurityStamp.</summary>
    public const string Provider = "provider";

    /// <summary>Partner WEB portal — validated against UserAccount.ProviderWebSecurityStamp.</summary>
    public const string ProviderWeb = "provider-web";

    /// <summary>
    /// Tokens issued before this feature shipped carry no "app" claim. They are
    /// treated as consumer tokens so existing sessions keep working across the
    /// deploy rather than everyone being force-logged-out.
    /// </summary>
    public static string NormalizeOrDefault(string? claimValue)
    {
        if (string.Equals(claimValue, Provider, StringComparison.OrdinalIgnoreCase)) return Provider;
        if (string.Equals(claimValue, ProviderWeb, StringComparison.OrdinalIgnoreCase)) return ProviderWeb;
        return Consumer;
    }

    /// <summary>
    /// Resolves which provider-family app is logging in from the optional
    /// client header. Absent/unknown header → the mobile provider app, so
    /// existing mobile builds are unaffected.
    /// </summary>
    public static string ResolveProviderApp(string? clientHeaderValue) =>
        string.Equals(clientHeaderValue, ProviderWeb, StringComparison.OrdinalIgnoreCase)
            ? ProviderWeb
            : Provider;
}
