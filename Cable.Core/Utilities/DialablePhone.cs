using System.Text.RegularExpressions;

namespace Cable.Core.Utilities;

/// <summary>
/// Any number a phone can dial — Jordanian mobiles AND landlines, short service numbers
/// (911, 117…) and international numbers — as opposed to <see cref="PhoneNumberUtility"/>,
/// which is Jordan-mobile-only (it is used for SMS / OTP and must stay that way).
///
/// Do NOT feed landlines to <see cref="PhoneNumberUtility.NormalizePhoneNumber"/>: its
/// 11-digit "962…" branch treats them as a mobile missing its 7 and rewrites them into a
/// different, wrong number.
///
/// Storage form: digits only, with the country code ("962791234567", "96265551234",
/// "447700900123"); short codes as typed ("911").
/// Display / API form: "+" + digits for full numbers, short codes unchanged.
/// </summary>
public static class DialablePhone
{
    public const int MinDigits = 3;
    public const int MaxDigits = 15;   // E.164 maximum
    public const int ShortCodeMaxDigits = 6;
    public const string JordanCode = "962";

    /// <summary>Digits, spaces, dashes, dots, brackets and one leading "+". Letters, "*" and "#" (USSD) are refused.</summary>
    private static readonly Regex AllowedChars = new(@"^\+?[\d\s\-().]+$", RegexOptions.Compiled);

    /// <summary>The storage form, or null when the value cannot be dialled.</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var s = PhoneNumberUtility.ToAsciiDigits(input.Trim());
        if (!AllowedChars.IsMatch(s)) return null;

        var plus = s.StartsWith('+');
        var digits = new string(s.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return null;

        string result;
        if (plus)
            result = digits;                                    // +962…, +44…
        else if (digits.StartsWith("00"))
            result = digits[2..];                               // 00962…, 0044…
        else if (digits.StartsWith('0'))
        {
            // Jordan national format: 0 + 9-digit mobile (07…) or 0 + 8-digit landline (02/03/05/06…).
            var rest = digits[1..];
            if (rest.Length is not (8 or 9)) return null;
            result = JordanCode + rest;
        }
        else if (digits.Length <= ShortCodeMaxDigits)
            result = digits;                                    // 911, 117, 1717…
        else if (digits.Length == 9 && digits[0] == '7')
            result = JordanCode + digits;                       // 791234567 (mobile without the 0)
        else if (digits.Length == 8 && digits[0] is '2' or '3' or '5' or '6')
            result = JordanCode + digits;                       // 65551234 (landline without the 0)
        else
            result = digits;                                    // already with a country code: 962…, 44…

        // A frequent slip: "+962 079…" — the national 0 kept after the country code.
        if (result.StartsWith(JordanCode + "0") && result.Length is 12 or 13)
            result = JordanCode + result[4..];

        // Country codes never start with 0, and E.164 caps the length.
        if (result.Length > ShortCodeMaxDigits && result[0] == '0') return null;
        if (result.Length < MinDigits || result.Length > MaxDigits) return null;

        return result;
    }

    public static bool IsValid(string? input) => Normalize(input) is not null;

    /// <summary>Dial-ready form for API responses: "+962791234567", short codes unchanged; an unusable stored value is returned as stored.</summary>
    public static string? ForDisplay(string? stored)
    {
        var n = Normalize(stored);
        if (n is null) return stored;
        return n.Length <= ShortCodeMaxDigits ? n : "+" + n;
    }
}
