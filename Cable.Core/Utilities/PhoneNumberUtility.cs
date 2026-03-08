using System.Text.RegularExpressions;

namespace Cable.Core.Utilities;

/// <summary>
/// Centralized phone number normalization and validation utility for Jordan phone numbers
/// </summary>
public static class PhoneNumberUtility
{
    /// <summary>
    /// Jordan country code
    /// </summary>
    public const string JORDAN_COUNTRY_CODE = "962";
    
    /// <summary>
    /// Standard normalized format: 962XXXXXXXXX (12 digits)
    /// </summary>
    public const int NORMALIZED_LENGTH = 12;
    
    /// <summary>
    /// Jordan mobile number length without country code (9 digits)
    /// </summary>
    public const int MOBILE_NUMBER_LENGTH = 9;

    private static readonly Regex PhoneCleanupRegex = new(@"[^\d]", RegexOptions.Compiled);
    
    /// <summary>
    /// Normalizes any Jordan phone number format to the standard format: 962XXXXXXXXX
    /// </summary>
    /// <param name="phoneNumber">Input phone number in any supported format</param>
    /// <returns>Normalized phone number in format 962XXXXXXXXX or null if invalid</returns>
    public static string? NormalizePhoneNumber(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return null;
        
        var cleaned = PhoneCleanupRegex.Replace(phoneNumber.Trim(), "");

        if (string.IsNullOrEmpty(cleaned))
            return null;

        // Handle different input formats and normalize to 962XXXXXXXXX
        var normalized = cleaned switch
        {
            // Already in correct format: 962XXXXXXXXX (12 digits)
            var s when s.Length == 12 && s.StartsWith(JORDAN_COUNTRY_CODE) => 
                IsValidJordanMobileNumber(s.Substring(3)) ? s : null,
            
            // International format with 00: 00962XXXXXXXXX (14 digits)
            var s when s.Length == 14 && s.StartsWith("00" + JORDAN_COUNTRY_CODE) => 
                IsValidJordanMobileNumber(s.Substring(5)) ? JORDAN_COUNTRY_CODE + s.Substring(5) : null,
            
            // National format with leading 0: 0XXXXXXXXX (10 digits)
            var s when s.Length == 10 && s.StartsWith("0") => 
                IsValidJordanMobileNumber(s.Substring(1)) ? JORDAN_COUNTRY_CODE + s.Substring(1) : null,
            
            // Local format: XXXXXXXXX (9 digits)
            var s when s.Length == 9 => 
                IsValidJordanMobileNumber(s) ? JORDAN_COUNTRY_CODE + s : null,
            
            // Short format missing leading 7: XXXXXXXX (8 digits)
            var s when s.Length == 8 => 
                IsValidJordanMobileNumber("7" + s) ? JORDAN_COUNTRY_CODE + "7" + s : null,
            
            // Handle edge case: 11 digits starting with 962 (missing one digit)
            var s when s.Length == 11 && s.StartsWith(JORDAN_COUNTRY_CODE) => 
                IsValidJordanMobileNumber("7" + s.Substring(3)) ? JORDAN_COUNTRY_CODE + "7" + s.Substring(3) : null,
            
            _ => null
        };

        return normalized;
    }

    /// <summary>
    /// Validates if the input is a valid Jordan phone number (any supported format)
    /// </summary>
    /// <param name="phoneNumber">Phone number to validate</param>
    /// <returns>True if valid Jordan phone number</returns>
    public static bool IsValidJordanPhoneNumber(string? phoneNumber)
    {
        return !string.IsNullOrEmpty(NormalizePhoneNumber(phoneNumber));
    }

    /// <summary>
    /// Validates Jordan mobile number format (9 digits starting with 7)
    /// </summary>
    /// <param name="mobileNumber">9-digit mobile number without country code</param>
    /// <returns>True if valid Jordan mobile number format</returns>
    private static bool IsValidJordanMobileNumber(string mobileNumber)
    {
        return mobileNumber.Length == MOBILE_NUMBER_LENGTH && 
               mobileNumber.StartsWith("7") && 
               mobileNumber.All(char.IsDigit) &&
               IsValidJordanMobilePrefix(mobileNumber);
    }

    /// <summary>
    /// Validates Jordan mobile number prefixes
    /// </summary>
    /// <param name="mobileNumber">9-digit mobile number</param>
    /// <returns>True if valid Jordan mobile prefix</returns>
    private static bool IsValidJordanMobilePrefix(string mobileNumber)
    {
        if (mobileNumber.Length != 9 || !mobileNumber.StartsWith("7"))
            return false;

        // Jordan mobile numbers start with:
        // 76 (Umniah), 77 (Orange), 78 (Zain), 79 (Zain/Umniah)
        var prefix = mobileNumber.Substring(0, 2);

        return prefix switch
        {
            "76" or "77" or "78" or "79" => true,
            _ => false
        };
    }

    /// <summary>
    /// Formats phone number for SMS API (typically needs 962XXXXXXXXX format)
    /// </summary>
    /// <param name="phoneNumber">Input phone number</param>
    /// <returns>SMS-ready format or null if invalid</returns>
    public static string? FormatForSms(string? phoneNumber)
    {
        return NormalizePhoneNumber(phoneNumber);
    }

    /// <summary>
    /// Formats phone number for display (adds + prefix for international display)
    /// </summary>
    /// <param name="phoneNumber">Input phone number</param>
    /// <returns>Display format (+962XXXXXXXXX) or original if invalid</returns>
    public static string FormatForDisplay(string? phoneNumber)
    {
        var normalized = NormalizePhoneNumber(phoneNumber);
        return normalized != null ? $"+{normalized}" : phoneNumber ?? string.Empty;
    }

    /// <summary>
    /// Gets supported phone number format examples
    /// </summary>
    /// <returns>List of example formats</returns>
    public static IEnumerable<string> GetSupportedFormats()
    {
        return new[]
        {
            "962XXXXXXXXX (International)",
            "00962XXXXXXXXX (International with 00)",
            "+962XXXXXXXXX (International with +)",
            "0XXXXXXXXX (National)",
            "XXXXXXXXX (Local)",
            "7XXXXXXXX (Mobile without leading 0/962)"
        };
    }
}