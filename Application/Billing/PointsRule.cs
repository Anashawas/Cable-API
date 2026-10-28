using System.Text.Json;
using System.Text.Json.Serialization;

namespace Application.Billing;

/// <summary>One amount band of a Tiered rule. FromJod is inside the band, ToJod outside; the last band has ToJod null.</summary>
public record PointsTier(decimal FromJod, decimal? ToJod, int Points);

/// <summary>How a loyalty plan turns a transaction into points (§2b.5): Fixed or Tiered.</summary>
public record PointsRule(string Mode, int? FixedPoints, List<PointsTier>? Tiers)
{
    public const string Fixed = "Fixed";
    public const string Tiered = "Tiered";
}

/// <summary>Price option as copied onto a subscription record.</summary>
public record PriceOptionSnapshot(int? Months, decimal PriceJod, int? Scans);

/// <summary>JSON for the list-like plan columns. camelCase, like the API.</summary>
public static class BillingJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Read<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try { return JsonSerializer.Deserialize<T>(json, Options); }
        catch (JsonException) { return default; }
    }

    public static List<string> ReadStrings(string? json) => Read<List<string>>(json) ?? [];
    public static List<int> ReadInts(string? json) => Read<List<int>>(json) ?? [];
    public static PointsRule? ReadRule(string? json) => Read<PointsRule>(json);
}

/// <summary>Server-side rules for a points rule (§2b.5). Pure, so it is unit-testable.</summary>
public static class PointsRuleValidator
{
    /// <summary>Returns null when valid, else a bilingual-ready English reason (the caller wraps it in INVALID_TIERS).</summary>
    public static string? Validate(PointsRule? rule)
    {
        if (rule is null) return "A points rule is required for a loyalty plan";
        var mode = Normalize(rule.Mode);
        if (mode is null) return "mode must be Fixed or Tiered";

        if (mode == PointsRule.Fixed)
        {
            if (rule.FixedPoints is not > 0) return "fixedPoints must be greater than 0";
            return null;
        }

        var tiers = rule.Tiers;
        if (tiers is null || tiers.Count == 0) return "A tiered rule needs at least one tier";
        if (tiers[0].FromJod < 0) return "The first tier cannot start below 0";

        for (var i = 0; i < tiers.Count; i++)
        {
            var t = tiers[i];
            if (t.Points < 0) return $"Tier {i + 1}: points cannot be negative";
            var last = i == tiers.Count - 1;
            if (last)
            {
                if (t.ToJod is not null) return "The last tier must be open-ended (toJod: null)";
            }
            else
            {
                if (t.ToJod is null) return $"Tier {i + 1}: only the last tier can be open-ended";
                if (t.ToJod <= t.FromJod) return $"Tier {i + 1}: toJod must be greater than fromJod";
                var next = tiers[i + 1];
                if (next.FromJod < t.ToJod) return $"Tiers {i + 1} and {i + 2} overlap";
                if (next.FromJod > t.ToJod) return $"Gap between tier {i + 1} and tier {i + 2}";
            }
        }
        return null;
    }

    public static string? Normalize(string? mode) =>
        string.Equals(mode, PointsRule.Fixed, StringComparison.OrdinalIgnoreCase) ? PointsRule.Fixed
        : string.Equals(mode, PointsRule.Tiered, StringComparison.OrdinalIgnoreCase) ? PointsRule.Tiered
        : null;

    /// <summary>Canonical form for storage: normalised mode, tiers sorted, unused half cleared.</summary>
    public static PointsRule Canonical(PointsRule rule)
    {
        var mode = Normalize(rule.Mode) ?? PointsRule.Fixed;
        return mode == PointsRule.Fixed
            ? new PointsRule(mode, rule.FixedPoints, null)
            : new PointsRule(mode, null, rule.Tiers!.OrderBy(t => t.FromJod).ToList());
    }
}

/// <summary>Points a transaction earns under a rule. Pure.</summary>
public static class PointsRuleCalculator
{
    /// <summary>
    /// Fixed → the fixed points. Tiered → the band where FromJod ≤ amount &lt; ToJod (3.000 falls in 3–5);
    /// below the first band → 0 (reported as belowMinimumAmount).
    /// </summary>
    public static int PointsFor(PointsRule? rule, decimal amount)
    {
        if (rule is null) return 0;
        if (PointsRuleValidator.Normalize(rule.Mode) == PointsRule.Fixed) return Math.Max(0, rule.FixedPoints ?? 0);
        var tier = TierFor(rule, amount);
        return tier?.Points ?? 0;
    }

    public static PointsTier? TierFor(PointsRule rule, decimal amount)
        => (rule.Tiers ?? []).OrderBy(t => t.FromJod)
            .FirstOrDefault(t => amount >= t.FromJod && (t.ToJod is null || amount < t.ToJod));

    /// <summary>The smallest amount that earns points (first band's start), or null for a Fixed rule.</summary>
    public static decimal? MinimumAmountForPoints(PointsRule? rule)
    {
        if (rule is null || PointsRuleValidator.Normalize(rule.Mode) == PointsRule.Fixed) return null;
        return (rule.Tiers ?? []).Where(t => t.Points > 0).OrderBy(t => t.FromJod).Select(t => (decimal?)t.FromJod).FirstOrDefault();
    }
}
