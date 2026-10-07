using Application.Common.Interfaces;
using Application.Settings;
using Cable.Core.Emuns;
using Cable.Core.Utilities;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Infrastructrue.Loyalty;

public class LoyaltyBoostService(IApplicationDbContext applicationDbContext) : ILoyaltyBoostService
{
    /// <summary>
    /// Campaign day and time-of-day windows are configured in Jordan local
    /// time. Shared with the write path, which normalises incoming dates using
    /// the same zone — one definition, so the two can never disagree.
    /// </summary>
    private static readonly TimeZoneInfo JordanTimeZone = JordanTime.Zone;

    private const int MinutesPerDay = 24 * 60;

    public async Task<BoostResolution?> ResolveAsync(
        int userId,
        string providerType,
        int providerId,
        DateTime nowUtc,
        int? excludeTransactionId = null,
        CancellationToken cancellationToken = default)
    {
        var campaign = await ResolveCampaignAsync(
            userId, providerType, providerId, nowUtc, cancellationToken);

        var welcome = await ResolveWelcomeBonusAsync(
            userId, excludeTransactionId, cancellationToken);

        // Never both. The customer gets the better of the two, so a first-timer
        // who happens to charge during a 3x campaign is not quietly dropped to
        // the 2x welcome rate.
        if (campaign is null)
            return welcome;

        if (welcome is null)
            return campaign;

        return welcome.Multiplier > campaign.Multiplier ? welcome : campaign;
    }

    /// <summary>
    /// Double points the first time a customer ever charges — a property of the
    /// app, not a campaign. It needs no <see cref="LoyaltyBoost"/> row, applies
    /// at every provider, and never expires; an admin can retune or disable it
    /// through the WelcomeBonusMultiplier setting.
    /// </summary>
    private async Task<BoostResolution?> ResolveWelcomeBonusAsync(
        int userId, int? excludeTransactionId, CancellationToken cancellationToken)
    {
        if (await HasChargedBeforeAsync(userId, excludeTransactionId, cancellationToken))
            return null;

        var multiplier = await AppSettingsProvider
            .GetWelcomeBonusMultiplierAsync(applicationDbContext, cancellationToken);

        // 1.0 is how the bonus is switched off; awarding it would write a
        // pointless multiplier onto the transaction.
        return multiplier > 1.0
            ? new BoostResolution(BoostId: null, multiplier, IsWelcomeBonus: true)
            : null;
    }

    private async Task<BoostResolution?> ResolveCampaignAsync(
        int userId,
        string providerType,
        int providerId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // Narrow on the date window in SQL; the day, time-of-day and provider
        // conditions are evaluated in memory below. The candidate set is bounded
        // by the number of live campaigns, which is small by nature.
        var candidates = await applicationDbContext.LoyaltyBoosts
            .AsNoTracking()
            .Include(b => b.Providers.Where(p => !p.IsDeleted))
            .Where(b => b.IsActive
                        && !b.IsDeleted
                        && b.StartsAt <= nowUtc
                        && b.EndsAt > nowUtc)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
            return null;

        var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, JordanTimeZone);

        var applicable = candidates
            .Where(b => MatchesDayOfWeek(b, local))
            .Where(b => MatchesTimeOfDay(b, local))
            .Where(b => MatchesProvider(b, providerType, providerId))
            .ToList();

        if (applicable.Count == 0)
            return null;

        // Highest multiplier wins outright; boosts never compound. Priority then
        // Id break ties so the outcome is deterministic rather than dependent on
        // row order.
        foreach (var boost in applicable
                     .OrderByDescending(b => b.Multiplier)
                     .ThenByDescending(b => b.Priority)
                     .ThenBy(b => b.Id))
        {
            if (await CapReachedAsync(boost, userId, cancellationToken))
                continue;

            return new BoostResolution(boost.Id, boost.Multiplier, IsWelcomeBonus: false);
        }

        return null;
    }

    private static bool MatchesDayOfWeek(LoyaltyBoost boost, DateTime local)
    {
        if (boost.DaysOfWeekMask is not { } mask)
            return true;

        // Bit 0 = Sunday, matching DayOfWeek's own numbering.
        return (mask & (1 << (int)local.DayOfWeek)) != 0;
    }

    private static bool MatchesTimeOfDay(LoyaltyBoost boost, DateTime local)
    {
        if (boost.DailyStartMinute is not { } start || boost.DailyEndMinute is not { } end)
            return true;

        var minuteOfDay = (local.Hour * 60) + local.Minute;

        // Start inclusive, end exclusive, so back-to-back windows never overlap.
        if (start <= end)
            return minuteOfDay >= start && minuteOfDay < end;

        // start > end means the window crosses midnight (22:00–02:00): the day
        // is split into two ranges rather than one.
        return minuteOfDay >= start || minuteOfDay < end;
    }

    private static bool MatchesProvider(LoyaltyBoost boost, string providerType, int providerId)
        => boost.AppliesToAllProviders
           || boost.Providers.Any(p => p.ProviderId == providerId
                                       && p.ProviderType == providerType);

    /// <summary>
    /// Whether the customer has completed a charge before this one, anywhere.
    ///
    /// There is deliberately no provider filter: the welcome bonus is once per
    /// customer for the life of their account, not once per station.
    ///
    /// The transaction being scanned is excluded: the caller has already marked
    /// it Completed, so counting it would make every customer look like a
    /// returning one and the bonus would never fire.
    /// </summary>
    private async Task<bool> HasChargedBeforeAsync(
        int userId, int? excludeTransactionId, CancellationToken cancellationToken)
        => await applicationDbContext.PartnerTransactions
            .AsNoTracking()
            .AnyAsync(t => t.UserId == userId
                           && !t.IsDeleted
                           && t.Status == (int)PartnerTransactionStatus.Completed
                           && (excludeTransactionId == null || t.Id != excludeTransactionId),
                cancellationToken);

    private async Task<bool> CapReachedAsync(
        LoyaltyBoost boost, int userId, CancellationToken cancellationToken)
    {
        if (boost.MaxBonusPointsPerUser is null && boost.MaxTotalBonusPoints is null)
            return false;

        // Bonus is the difference, so no separate counter has to be kept in step.
        var awarded = applicationDbContext.PartnerTransactions
            .AsNoTracking()
            .Where(t => t.AppliedBoostId == boost.Id
                        && !t.IsDeleted
                        && t.Status == (int)PartnerTransactionStatus.Completed);

        if (boost.MaxTotalBonusPoints is { } totalCap)
        {
            var spent = await awarded
                .SumAsync(t => (t.PointsAwarded ?? 0) - (t.BasePoints ?? 0), cancellationToken);

            if (spent >= totalCap)
                return true;
        }

        if (boost.MaxBonusPointsPerUser is { } userCap)
        {
            var spentByUser = await awarded
                .Where(t => t.UserId == userId)
                .SumAsync(t => (t.PointsAwarded ?? 0) - (t.BasePoints ?? 0), cancellationToken);

            if (spentByUser >= userCap)
                return true;
        }

        return false;
    }
}
