using System.Globalization;

namespace Cable.Core.Utilities;

/// <summary>Arabic or English for server-written texts, from the request culture (Accept-Language). Arabic is the default.</summary>
public static class Lang
{
    public static bool IsArabic => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "en";

    public static string Pick(string ar, string en) => IsArabic ? ar : en;

    /// <summary>For texts sent outside a request (pushes, jobs): language "en" → English, anything else → Arabic.</summary>
    public static string Pick(string? language, string ar, string en) =>
        string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? en : ar;
}
