namespace Cable.Core.Utilities;

/// <summary>
/// Converts the dates clients send into the UTC the database and every
/// comparison expects.
///
/// WHY THIS EXISTS
///
/// Every scheduling window in this system — offer validity, announcements, ad
/// campaigns, loyalty seasons and rewards, points boosts — is stored as UTC and
/// compared against <see cref="DateTime.UtcNow"/>. But the people filling those
/// forms are in Amman and type Amman wall-clock time, and JSON dates arrive
/// without an offset, so the raw value was being stored as if it were already
/// UTC.
///
/// The result was silent and consistent: an offer set to start "now" did not
/// start for three hours, and its operator was told "This offer is not yet
/// available" with nothing to explain why. Production carried four such offers
/// before this was found, each off by exactly the UTC+3 offset.
///
/// So a date with no offset is interpreted as Jordan local, which is what the
/// person entering it meant. A date that carries an explicit offset or a Z is
/// already unambiguous and is honoured as sent — clients that do the right
/// thing are never second-guessed.
/// </summary>
public static class JordanTime
{
    /// <summary>
    /// Jordan sits on permanent UTC+3 today, so a hardcoded offset would be
    /// correct right now. It is resolved through <see cref="TimeZoneInfo"/>
    /// anyway: if that policy is ever reversed, an offset would shift every
    /// window in the system by an hour with nothing to indicate it had.
    /// </summary>
    public static TimeZoneInfo Zone { get; } = ResolveZone();

    /// <summary>
    /// Normalises an incoming date to UTC.
    ///
    /// <list type="bullet">
    /// <item><see cref="DateTimeKind.Utc"/> — already UTC, returned unchanged.</item>
    /// <item><see cref="DateTimeKind.Local"/> — converted from the server's zone.</item>
    /// <item><see cref="DateTimeKind.Unspecified"/> — the common case for JSON
    /// without an offset. Interpreted as Jordan local time.</item>
    /// </list>
    /// </summary>
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        // TimeZoneInfo.ConvertTimeToUtc requires Unspecified for a non-local
        // zone, which is exactly what we have here.
        _ => ConvertUnspecified(value)
    };

    /// <summary>Null-tolerant overload for optional windows such as ValidTo.</summary>
    public static DateTime? ToUtc(DateTime? value)
        => value.HasValue ? ToUtc(value.Value) : null;

    /// <summary>
    /// UTC back to Jordan wall-clock, for reporting and for the time-of-day
    /// windows on loyalty boosts.
    /// </summary>
    public static DateTime FromUtc(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(
            utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc),
            Zone);

    private static DateTime ConvertUnspecified(DateTime value)
    {
        var unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

        // Twice a year a local time can be invalid (skipped by a DST jump) or
        // ambiguous (repeated). Jordan does not currently observe DST so neither
        // occurs, but the zone is resolved dynamically and could change —
        // throwing here would reject a legitimate form submission, so both cases
        // resolve to a real instant instead.
        if (Zone.IsInvalidTime(unspecified))
            return TimeZoneInfo.ConvertTimeToUtc(unspecified.AddHours(1), Zone);

        if (Zone.IsAmbiguousTime(unspecified))
            return unspecified - Zone.GetUtcOffset(unspecified);

        return TimeZoneInfo.ConvertTimeToUtc(unspecified, Zone);
    }

    private static TimeZoneInfo ResolveZone()
    {
        // .NET 6+ accepts IANA ids on Windows, but the Windows id is kept as a
        // fallback for hosts without ICU. Falling back to UTC would silently
        // shift every window by three hours — the exact bug this class exists
        // to prevent — so an unresolvable zone is a hard failure.
        foreach (var id in new[] { "Asia/Amman", "Jordan Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Try the next id.
            }
            catch (InvalidTimeZoneException)
            {
                // Corrupt zone data — try the next id.
            }
        }

        throw new InvalidOperationException(
            "Jordan time zone not found ('Asia/Amman' or 'Jordan Standard Time'). " +
            "Scheduling windows are entered in local time and cannot be normalised without it.");
    }
}
