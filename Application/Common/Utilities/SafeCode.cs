using System.Security.Cryptography;
using System.Text;

namespace Application.Common.Utilities;

/// <summary>
/// Human-friendly codes (GFT- gift orders, PRM- promo codes): upper-case letters and digits WITHOUT the look-alikes
/// 0 O 1 I L, drawn from a cryptographic RNG so they are neither sequential nor guessable. Pure, testable.
/// </summary>
public static class SafeCode
{
    /// <summary>31 characters: A–Z and 2–9 minus I, L, O.</summary>
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    /// <summary>A random block of <paramref name="length"/> characters from <see cref="Alphabet"/>.</summary>
    public static string Random(int length)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }

    /// <summary>"PRM-" + 8 random characters.</summary>
    public static string NewPrefixed(string prefix, int length) => $"{prefix}-{Random(length)}";

    /// <summary>
    /// What a typed code is matched on: upper case, every space, tab and dash removed (codes pasted from WhatsApp
    /// come with spaces and odd dashes), Arabic-Indic digits turned into 0–9. "prm 7xk2-m9qa " → "PRM7XK2M9QA".
    /// </summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var sb = new StringBuilder(input.Length);
        foreach (var raw in input)
        {
            var c = raw;
            if (char.IsWhiteSpace(c) || IsDash(c) || c == '‏' || c == '‎' || c == '​') continue;
            if (c is >= '٠' and <= '٩') c = (char)('0' + (c - '٠'));       // Arabic-Indic digits
            else if (c is >= '۰' and <= '۹') c = (char)('0' + (c - '۰'));  // Extended (Persian) digits
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    private static bool IsDash(char c) =>
        c is '-' or '_' or '‐' or '‑' or '‒' or '–' or '—' or '―' or '−' or '­' or 'ـ';
}
