using System.Security.Cryptography;
using Cable.Core.Constants;

namespace Application.Ocpp.LiveCharging;

/// <summary>
/// §10.2 — the plug sticker code: <c>CHG-</c> + 6 characters from an alphabet without the look-alikes 0 O 1 I L,
/// e.g. CHG-7K2M9Q (31^6 ≈ 887 million codes). Pure: no I/O.
/// </summary>
public static class ConnectorCodeFormat
{
    public const string Prefix = "CHG-";
    public const int Length = 6;

    /// <summary>2–9 and A–Z without I, L, O.</summary>
    public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    /// <summary>A new random code (cryptographic RNG, so codes cannot be predicted from earlier ones).</summary>
    public static string Generate()
    {
        Span<char> chars = stackalloc char[Length];
        for (var i = 0; i < Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return Prefix + new string(chars);
    }

    /// <summary>The link printed in the sticker's QR.</summary>
    public static string LinkFor(string code) => LiveChargingLimits.StickerLinkBase + code;

    /// <summary>
    /// What the driver scanned or typed → "CHG-XXXXXX", or null when it cannot be a sticker code.
    /// Accepts the code ("CHG-7K2M9Q", "chg 7k2m9q", "7K2M-9Q"), or the full link
    /// ("https://cable-app.com/c/CHG-7K2M9Q?utm=…"). Case-insensitive; spaces and dashes are ignored.
    /// </summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var s = input.Trim();

        // A link: keep the path segment after "/c/".
        var at = s.IndexOf("/c/", StringComparison.OrdinalIgnoreCase);
        if (at >= 0)
        {
            s = s[(at + 3)..];
            var end = s.IndexOfAny(['?', '#', '/']);
            if (end >= 0) s = s[..end];
            s = Uri.UnescapeDataString(s);
        }

        var compact = new string(s.Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '_').ToArray()).ToUpperInvariant();
        // "CHG-…" / "CHG …" is always the prefix; glued ("CHG7K2M9Q") only when 9 characters long, so a bare
        // code that happens to begin with CHG still resolves.
        var prefixed = s.Length > 3 && s.StartsWith("CHG", StringComparison.OrdinalIgnoreCase) && (s[3] is '-' or '_' || char.IsWhiteSpace(s[3]));
        if (compact.StartsWith("CHG", StringComparison.Ordinal) && (prefixed || compact.Length == 3 + Length))
            compact = compact[3..];
        if (compact.Length != Length || compact.Any(c => !Alphabet.Contains(c)))
            return null;
        return Prefix + compact;
    }
}
