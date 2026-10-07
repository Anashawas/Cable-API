using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Application.Ocpp;

/// <summary>
/// Charge point ids and passwords as typed into the charger by a technician —
/// so the alphabet avoids anything a charger's config screen or URL field might
/// choke on. Many firmwares cap the id at 20 characters; we allow 40 for units
/// that already carry a longer id (the pilot unit is simply "RH4").
/// </summary>
public static partial class OcppCredentials
{
    /// <summary>OCPP 1.6 identifies a charge point with a CiString20; many units refuse anything longer.</summary>
    public const int MaxChargePointIdLength = 20;
    private const string PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,19}$")]
    private static partial Regex ChargePointIdPattern();

    public static bool IsValidChargePointId(string? id) => id is not null && ChargePointIdPattern().IsMatch(id);

    /// <summary>CBL-{station}-{nn}: unique system-wide, not tied to the hardware serial (swapping a unit keeps the link).</summary>
    public static string GenerateChargePointId(int chargingPointId, int sequence) =>
        $"CBL-{chargingPointId}-{sequence:D2}";

    /// <summary>24 characters, no look-alikes (0/O, 1/l/I) — it will be typed on a touch screen.</summary>
    public static string GeneratePassword(int length = 24)
    {
        Span<char> chars = stackalloc char[length];
        for (var i = 0; i < length; i++)
            chars[i] = PasswordAlphabet[RandomNumberGenerator.GetInt32(PasswordAlphabet.Length)];
        return new string(chars);
    }

    /// <summary>Tags are matched case-insensitively and without surrounding whitespace.</summary>
    public static string NormalizeIdTag(string idTag) => idTag.Trim().ToUpperInvariant();
}
