namespace Application.Billing;

/// <summary>One band of the expected amount distribution: transactions from <see cref="FromJod"/> up to the next band.</summary>
public record AmountMixEntry(decimal FromJod, decimal Percent);

public record MarginCheckResult(
    double AvgPointsPerScan,
    decimal PointValueJod,
    decimal CostPerScanJod,
    /// <summary>Price ÷ scans; null for unlimited scanning.</summary>
    decimal? PricePerScanJod,
    /// <summary>Scans the price pays for before points cost more than it (null when a scan costs nothing).</summary>
    int? BreakEvenScans,
    /// <summary>Scans to sell for the price at the target margin.</summary>
    int? RecommendedScans,
    /// <summary>Scans − break-even (positive = every scan after break-even is a loss).</summary>
    int? ScansAboveBreakEven,
    /// <summary>Unlimited plans: break-even per month — the default soft cap (decision 20).</summary>
    int? BreakEvenScansPerMonth,
    /// <summary>Unlimited plans: monthly break-even ÷ 30, a starting value for the daily soft cap.</summary>
    int? SuggestedDailySoftCap,
    bool Ok,
    decimal TargetMarginPercent,
    /// <summary>manual | provider | equal | fixed — where the amount distribution came from.</summary>
    string MixSource,
    List<AmountMixEntry> AmountMix,
    string? Warning);

/// <summary>
/// Margin and break-even maths (§2b.4b). Points cost only — no operating cost, no payment fees,
/// no campaign / promo points (those are a separate marketing budget). Pure.
///
///   point value        = 1 ÷ points per JOD
///   cost per scan      = average points per scan × point value
///   break-even (scans) = price ÷ cost per scan
///   recommended scans  = price × (1 − target margin) ÷ cost per scan
/// </summary>
public static class BillingMath
{
    public static decimal PointValueJod(double pointsPerJod) =>
        pointsPerJod > 0 ? Math.Round(1m / (decimal)pointsPerJod, 6) : 0m;

    /// <summary>
    /// Average points per scan. Fixed → the fixed points. Tiered → weighted by the amount mix
    /// (each band earns what its starting amount earns); no mix → equal weight per tier.
    /// </summary>
    public static (double Avg, string Source, List<AmountMixEntry> Mix) AveragePoints(PointsRule rule, IReadOnlyList<AmountMixEntry>? mix)
    {
        if (PointsRuleValidator.Normalize(rule.Mode) == PointsRule.Fixed)
            return (rule.FixedPoints ?? 0, "fixed", []);

        var tiers = (rule.Tiers ?? []).OrderBy(t => t.FromJod).ToList();
        if (mix is { Count: > 0 } && mix.Sum(m => m.Percent) > 0)
        {
            var total = mix.Sum(m => m.Percent);
            var avg = mix.Sum(m => (double)(m.Percent / total) * PointsRuleCalculator.PointsFor(rule, m.FromJod));
            return (avg, "manual", mix.ToList());
        }

        if (tiers.Count == 0) return (0, "equal", []);
        var share = 100m / tiers.Count;
        return (tiers.Average(t => (double)t.Points), "equal", tiers.Select(t => new AmountMixEntry(t.FromJod, Math.Round(share, 2))).ToList());
    }

    /// <summary>Average points per scan from real transaction amounts (the provider's last 90 days).</summary>
    public static (double Avg, List<AmountMixEntry> Mix) AveragePointsFromAmounts(PointsRule rule, IReadOnlyList<decimal> amounts)
    {
        if (amounts.Count == 0) return (0, []);
        var avg = amounts.Average(a => (double)PointsRuleCalculator.PointsFor(rule, a));
        var mix = new List<AmountMixEntry>();
        if (PointsRuleValidator.Normalize(rule.Mode) == PointsRule.Tiered)
        {
            var tiers = (rule.Tiers ?? []).OrderBy(t => t.FromJod).ToList();
            var below = amounts.Count(a => tiers.Count == 0 || a < tiers[0].FromJod);
            if (below > 0) mix.Add(new AmountMixEntry(0, Math.Round(100m * below / amounts.Count, 2)));
            foreach (var t in tiers)
            {
                var n = amounts.Count(a => a >= t.FromJod && (t.ToJod is null || a < t.ToJod));
                mix.Add(new AmountMixEntry(t.FromJod, Math.Round(100m * n / amounts.Count, 2)));
            }
        }
        return (avg, mix);
    }

    public static MarginCheckResult Check(
        decimal priceJod, int? scans, int? months, double avgPointsPerScan, double pointsPerJod,
        decimal targetMarginPercent, int? expectedMonthlyScans, string mixSource, List<AmountMixEntry> mix)
    {
        var pointValue = PointValueJod(pointsPerJod);
        var costPerScan = Math.Round((decimal)avgPointsPerScan * pointValue, 6);
        var margin = Math.Clamp(targetMarginPercent, 0m, 100m) / 100m;

        int? breakEven = costPerScan > 0 ? (int)Math.Floor(priceJod / costPerScan) : null;
        int? recommended = costPerScan > 0 ? (int)Math.Floor(priceJod * (1 - margin) / costPerScan) : null;
        decimal? pricePerScan = scans is > 0 ? Math.Round(priceJod / scans.Value, 6) : null;

        int? above = null, perMonth = null, dailyCap = null;
        bool ok;
        string? warning = null;

        if (scans is > 0)
        {
            above = breakEven is null ? null : scans.Value - breakEven.Value;
            ok = breakEven is null || scans.Value <= breakEven.Value;
            if (!ok) warning = $"Every scan after {breakEven} is a loss";
            else if (recommended is not null && scans.Value > recommended.Value)
                warning = $"Below the {targetMarginPercent:0.#}% target margin — recommended {recommended} scans";
        }
        else
        {
            // Unlimited: break-even is per period; the monthly figure is the default soft cap.
            var m = months is > 0 ? months.Value : 1;
            perMonth = breakEven is null ? null : (int)Math.Floor(breakEven.Value / (double)m);
            dailyCap = perMonth is null ? null : (int)Math.Ceiling(perMonth.Value / 30.0);
            ok = perMonth is null || expectedMonthlyScans is null || expectedMonthlyScans.Value <= perMonth.Value;
            if (!ok) warning = $"Expected {expectedMonthlyScans} scans a month is above the monthly break-even of {perMonth}";
        }

        return new MarginCheckResult(
            Math.Round(avgPointsPerScan, 3), pointValue, Math.Round(costPerScan, 3), pricePerScan is null ? null : Math.Round(pricePerScan.Value, 3),
            breakEven, recommended, above, perMonth, dailyCap, ok, targetMarginPercent, mixSource, mix, warning);
    }

    /// <summary>ok below 80% of break-even, warning from 80%, over past 100% (§2b.4b meter colours).</summary>
    public static (double Percent, string State) BreakEvenState(int scansUsed, int? breakEvenScans)
    {
        if (breakEvenScans is not > 0) return (0, "ok");
        var pct = Math.Round(100.0 * scansUsed / breakEvenScans.Value, 1);
        return (pct, pct > 100 ? "over" : pct >= 80 ? "warning" : "ok");
    }

    /// <summary>
    /// Decision 16: the unused part of what was paid, credited in JOD on the new plan's price.
    /// Period plans by remaining time, bundles by remaining scans; both → whichever is smaller.
    /// </summary>
    public static decimal RemainingCredit(decimal paidJod, DateTime startsAt, DateTime? endsAt, int? scansIncluded, int scansUsed, DateTime nowUtc)
    {
        if (paidJod <= 0) return 0m;
        var fractions = new List<decimal>();
        if (endsAt is { } end && end > startsAt)
        {
            var total = (decimal)(end - startsAt).TotalSeconds;
            var left = (decimal)Math.Max(0, (end - (nowUtc > startsAt ? nowUtc : startsAt)).TotalSeconds);
            fractions.Add(Math.Clamp(left / total, 0m, 1m));
        }
        if (scansIncluded is > 0)
            fractions.Add(Math.Clamp((decimal)(scansIncluded.Value - scansUsed) / scansIncluded.Value, 0m, 1m));
        if (fractions.Count == 0) return 0m;
        return Math.Round(paidJod * fractions.Min(), 3);
    }

    /// <summary>Remaining scans as a whole percent (null for unlimited).</summary>
    public static int? PercentRemaining(int? scansIncluded, int scansUsed) =>
        scansIncluded is > 0 ? (int)Math.Floor(100.0 * Math.Max(0, scansIncluded.Value - scansUsed) / scansIncluded.Value) : null;
}
