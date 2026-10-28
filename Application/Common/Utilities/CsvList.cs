using System.Globalization;
using System.Text;

namespace Application.Common.Utilities;

/// <summary>Comma-separated id / name lists stored in one column (gift cities, promo stations…), and CSV export cells.</summary>
public static class CsvList
{
    public static List<int> Ints(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : (int?)null)
                .Where(v => v.HasValue).Select(v => v!.Value).Distinct().ToList();

    public static List<string> Strings(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Null for an empty list, so "empty = all" stays a NULL column.</summary>
    public static string? Join(IEnumerable<int>? values)
    {
        var list = values?.Distinct().OrderBy(v => v).ToList();
        return list is { Count: > 0 } ? string.Join(',', list.Select(v => v.ToString(CultureInfo.InvariantCulture))) : null;
    }

    public static string? Join(IEnumerable<string>? values)
    {
        var list = values?.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return list is { Count: > 0 } ? string.Join(',', list) : null;
    }

    /// <summary>One CSV cell, quoted when needed.</summary>
    public static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        // A leading = @ (or + - followed by text) would be run as a formula by Excel; phone numbers like +962… stay as they are.
        if (value[0] is '=' or '@' || (value[0] is '+' or '-' && value.Skip(1).Any(ch => !char.IsDigit(ch) && ch != ' ')))
            value = "'" + value;
        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    /// <summary>UTF-8 with BOM, so Excel opens Arabic correctly.</summary>
    public static byte[] Utf8WithBom(StringBuilder csv) =>
        [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
}
