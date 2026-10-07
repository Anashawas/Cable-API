using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// A time-limited multiplier on the points a charge awards.
///
/// Deliberately independent of <see cref="LoyaltySeason"/> and
/// <see cref="LoyaltyPointAction"/>: both are empty in production, and anything
/// gated on them awards nothing. Boosts apply to the spend path only — a
/// completed <see cref="PartnerTransaction"/>, which is the only record that a
/// customer charged and paid.
///
/// The once-ever welcome bonus is <b>not</b> a boost. It is a property of the
/// app, needs no row here, and is resolved separately — see
/// <c>AppSettingsProvider.WelcomeBonusMultiplierKey</c>. The two never stack;
/// whichever multiplier is higher wins.
/// </summary>
public class LoyaltyBoost : BaseAuditableEntity
{
    public string Name { get; set; } = null!;
    public string? NameAr { get; set; }
    public string? Description { get; set; }

    /// <summary>2.0 = double points. Always greater than 1.</summary>
    public double Multiplier { get; set; }

    /// <summary>UTC, inclusive.</summary>
    public DateTime StartsAt { get; set; }

    /// <summary>UTC, exclusive.</summary>
    public DateTime EndsAt { get; set; }

    /// <summary>
    /// Minutes from midnight in <b>Jordan local time</b>, 0–1439. Null means all
    /// day. Both bounds are set together or not at all; a start later than the
    /// end is a valid window that crosses midnight (22:00–02:00).
    /// </summary>
    public int? DailyStartMinute { get; set; }

    public int? DailyEndMinute { get; set; }

    /// <summary>
    /// Bit 0 = Sunday through bit 6 = Saturday, evaluated against the Jordan
    /// local day. Null means every day.
    /// </summary>
    public int? DaysOfWeekMask { get; set; }

    /// <summary>When false, <see cref="Providers"/> lists the targeted assets.</summary>
    public bool AppliesToAllProviders { get; set; }

    /// <summary>Tie-breaker when two boosts share the same multiplier; higher wins.</summary>
    public int Priority { get; set; }

    /// <summary>Cap on the <b>bonus</b> points one user can draw from this boost.</summary>
    public int? MaxBonusPointsPerUser { get; set; }

    /// <summary>Budget cap on the bonus points the boost may give away in total.</summary>
    public int? MaxTotalBonusPoints { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<LoyaltyBoostProvider> Providers { get; set; } =
        new List<LoyaltyBoostProvider>();
}
