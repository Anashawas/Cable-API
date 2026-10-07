namespace Application.Common.Utilities;

/// <summary>
/// Maps a caller-supplied city name onto one of the 12 canonical Jordanian
/// cities that provider records are stored under.
///
/// Provider CityName values are already canonical English (Amman, Irbid, …) —
/// the same set Scripts/NormalizeUserAccountCity.sql normalizes UserAccount.City
/// to. This resolver exists because the consumer app is Arabic-first: a user in
/// عمّان would otherwise match nothing and be told there are no offers in their
/// city, which reads as a broken feature rather than an input mismatch.
/// </summary>
public static class JordanCityResolver
{
    public static readonly IReadOnlyList<string> CanonicalCities =
    [
        "Amman", "Zarqa", "Irbid", "Salt", "Mafraq", "Madaba",
        "Jerash", "Ajloun", "Karak", "Tafilah", "Maan", "Aqaba"
    ];

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["amman"] = "Amman", ["عمان"] = "Amman", ["عمّان"] = "Amman", ["العاصمة"] = "Amman",
        ["zarqa"] = "Zarqa", ["al zarqa"] = "Zarqa", ["الزرقاء"] = "Zarqa", ["زرقاء"] = "Zarqa",
        ["irbid"] = "Irbid", ["اربد"] = "Irbid", ["إربد"] = "Irbid",
        ["salt"] = "Salt", ["al salt"] = "Salt", ["as salt"] = "Salt", ["السلط"] = "Salt", ["سلط"] = "Salt", ["البلقاء"] = "Salt",
        ["mafraq"] = "Mafraq", ["al mafraq"] = "Mafraq", ["المفرق"] = "Mafraq", ["مفرق"] = "Mafraq",
        ["madaba"] = "Madaba", ["مادبا"] = "Madaba", ["مأدبا"] = "Madaba",
        ["jerash"] = "Jerash", ["jarash"] = "Jerash", ["جرش"] = "Jerash",
        ["ajloun"] = "Ajloun", ["ajlun"] = "Ajloun", ["عجلون"] = "Ajloun",
        ["karak"] = "Karak", ["al karak"] = "Karak", ["kerak"] = "Karak", ["الكرك"] = "Karak", ["كرك"] = "Karak",
        ["tafilah"] = "Tafilah", ["tafila"] = "Tafilah", ["at tafilah"] = "Tafilah", ["الطفيلة"] = "Tafilah", ["طفيلة"] = "Tafilah",
        ["maan"] = "Maan", ["ma'an"] = "Maan", ["معان"] = "Maan",
        ["aqaba"] = "Aqaba", ["al aqaba"] = "Aqaba", ["العقبة"] = "Aqaba", ["عقبة"] = "Aqaba"
    };

    /// <summary>
    /// Returns the canonical city name, or null when the input matches none —
    /// which the caller reports as "no offers in that city" rather than
    /// silently falling back to a default city and returning offers the user
    /// cannot reach.
    /// </summary>
    public static string? Resolve(string? city)
    {
        if (string.IsNullOrWhiteSpace(city))
            return null;

        var key = city.Trim();

        if (Aliases.TryGetValue(key, out var canonical))
            return canonical;

        // Arabic input frequently carries the definite article as one token
        // ("الزرقاء" is covered above, but "ال زرقاء" and stray diacritics are not).
        var stripped = key.Replace("ـ", "").Replace("أ", "ا").Replace("إ", "ا").Replace("آ", "ا");
        return Aliases.GetValueOrDefault(stripped);
    }
}
