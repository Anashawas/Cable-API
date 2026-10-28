using Application.Common.Extensions;
using Application.Common.Interfaces;
using Application.Pricing;
using Application.Settings;
using Cable.Core.Constants;
using Cable.Core.Emuns;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Ocpp.Points;

// ---------------------------------------------------------------------------
// 4.7 — automatic loyalty points when a driver's Cable Connect session ends, with
// no QR scan. Behind Ocpp.AutoPoints.Enabled (off by default). The session is
// treated exactly like a partner QR scan for its cost: the station's active
// PartnerAgreement gives the reward %, the points rate and the commission; the
// commission comes off the station wallet; the settlement, welcome bonus and
// boosts are the very same services. It is recorded as a completed
// PartnerTransaction (code OCPP-{transactionId}) so wallet, settlement and
// reports include it. Idempotent per session: the session row is locked and
// PointsProcessedAt is the "already decided" marker; the unique transaction
// code is the backstop.
// ---------------------------------------------------------------------------

public interface IOcppSessionPointsService
{
    /// <summary>
    /// Decide (once) the automatic points of one closed session. Returns the points awarded (0 = decided, none),
    /// or null when nothing was decided yet (switched off, still open, not priced, ended before the switch).
    /// Enqueued by Cable.Ocpp after the Stop; safe to run twice.
    /// </summary>
    Task<int?> AwardAsync(int transactionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The Hangfire job Cable.Ocpp enqueues after a driver's Stop: <see cref="AwardAsync"/> that never fails — the
    /// "charging finished" push is chained after it, so a failure must not hold the push back (the catch-up retries).
    /// </summary>
    Task AwardAfterStopAsync(int transactionId, CancellationToken cancellationToken = default);

    /// <summary>Catch-up for sessions the after-stop job missed (run by the price-ocpp-sessions job). Returns sessions awarded points.</summary>
    Task<int> AwardPendingAsync(CancellationToken cancellationToken = default);
}

/// <summary>The pure rules, mirroring the partner QR flow (InitiatePartnerTransaction + ScanPartnerCode).</summary>
public static class OcppAutoPointsRules
{
    public const string TransactionCodePrefix = "OCPP-";
    public const string ProviderType = "ChargingPoint";
    public const string Currency = "JOD";

    /// <summary>The catch-up never reaches further back than this, whatever the switch date.</summary>
    public static readonly TimeSpan CatchUpWindow = TimeSpan.FromDays(2);

    public static string TransactionCode(int transactionId) => TransactionCodePrefix + transactionId;

    /// <summary>Why a session earns no automatic points (stored on OcppTransaction.PointsSkipReason).</summary>
    public static class SkipReasons
    {
        public const string NotDriverSession = "notDriverSession";
        public const string ZeroCost = "zeroCost";
        public const string NoAgreement = "noAgreement";
        public const string StationBlocked = "stationBlocked";
        public const string UserBlocked = "userBlocked";
        public const string BelowMinimumAmount = "belowMinimumAmount";
        public const string NoPoints = "noPoints";
        public const string CreditLimit = "creditLimit";
    }

    /// <summary>
    /// Only a driver's own session: started by a user through the app or with a card linked to them.
    /// Never an operator / staff start (credited to the staff member), a rejected start, an orphan stop.
    /// </summary>
    public static bool IsDriverSession(int? startedByUserId, string startSource, bool wasRejected, bool isOrphan) =>
        startedByUserId is not null && !wasRejected && !isOrphan
        && (startSource == OcppStartSource.App || startSource == OcppStartSource.Card);

    public sealed record Figures(decimal Amount, decimal Commission, decimal EligibleAmount, int BasePoints);

    /// <summary>Exactly the partner QR arithmetic: commission and points-eligible share of the amount, points floored.</summary>
    public static Figures Compute(decimal amountJod, double commissionPercentage, double rewardPercentage, double pointsPerJod)
    {
        var commission = amountJod * (decimal)(commissionPercentage / 100.0);
        var eligible = amountJod * (decimal)(rewardPercentage / 100.0);
        var points = (int)Math.Floor((double)eligible * pointsPerJod);
        return new Figures(amountJod, commission, eligible, Math.Max(points, 0));
    }

    public static int Boosted(int basePoints, double? multiplier) =>
        multiplier is double m ? (int)Math.Floor(basePoints * m) : basePoints;

    /// <summary>A temporary block that has run out counts as unblocked (same as LoyaltyPointService).</summary>
    public static bool IsBlocked(bool isBlocked, DateTime? blockedUntil, DateTime nowUtc) =>
        isBlocked && (blockedUntil is null || blockedUntil.Value >= nowUtc);

    /// <summary>The station wallet would go past its credit limit — the QR flow refuses such a code too.</summary>
    public static bool ExceedsCreditLimit(decimal walletBalance, decimal? creditLimit, decimal commission) =>
        creditLimit is decimal limit && walletBalance - commission < -limit;
}

public class OcppSessionPointsService(
    IApplicationDbContext db,
    ILoyaltyPointService loyaltyPointService,
    ILoyaltyBoostService loyaltyBoostService,
    ISettlementService settlementService,
    ILogger<OcppSessionPointsService> logger) : IOcppSessionPointsService
{
    public async Task<int?> AwardAsync(int transactionId, CancellationToken cancellationToken = default)
    {
        var settings = await AppSettingsProvider.GetOcppAutoPointsAsync(db, cancellationToken);
        if (!settings.Enabled) return null;

        await using var dbTx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // The row lock serialises the after-stop job and the catch-up for the same session.
            var tx = await db.OcppTransactions
                .FromSqlRaw("SELECT * FROM [OcppTransaction] WITH (UPDLOCK, ROWLOCK) WHERE Id = {0}", transactionId)
                .FirstOrDefaultAsync(cancellationToken);
            if (tx is null) { await dbTx.RollbackAsync(cancellationToken); return null; }
            if (tx.PointsProcessedAt is not null) { await dbTx.CommitAsync(cancellationToken); return tx.PointsAwarded; }
            if (tx.IsOpen || tx.StoppedAt is null) { await dbTx.RollbackAsync(cancellationToken); return null; }

            var now = DateTime.UtcNow;
            // Nothing changes for sessions that ended before the switch was turned on (or long ago).
            if ((settings.Since is DateTime since && tx.StoppedAt < since) || tx.StoppedAt < now - OcppAutoPointsRules.CatchUpWindow)
            { await dbTx.RollbackAsync(cancellationToken); return null; }

            if (!OcppAutoPointsRules.IsDriverSession(tx.StartedByUserId, tx.StartSource, tx.WasRejected, tx.IsOrphan))
                return await SkipAsync(tx, OcppAutoPointsRules.SkipReasons.NotDriverSession, now, dbTx, cancellationToken);

            if (tx.CostFils is null && await SessionPricingService.PriceAsync(db, tx, null, cancellationToken))
                await db.SaveChanges(cancellationToken);
            if (tx.CostFils is null) { await dbTx.RollbackAsync(cancellationToken); return null; }   // not priced yet: the catch-up retries
            if (tx.CostFils <= 0)
                return await SkipAsync(tx, OcppAutoPointsRules.SkipReasons.ZeroCost, now, dbTx, cancellationToken);

            var userId = tx.StartedByUserId!.Value;
            var stationId = await db.OcppChargePoints.AsNoTracking()
                .Where(c => c.Id == tx.OcppChargePointId).Select(c => c.ChargingPointId).FirstAsync(cancellationToken);

            var agreement = await db.PartnerAgreements.Include(a => a.ConversionRate)
                .Where(a => a.ProviderType == OcppAutoPointsRules.ProviderType && a.ProviderId == stationId && a.IsActive && !a.IsDeleted)
                .OrderByDescending(a => a.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (agreement is null)
                return await SkipAsync(tx, OcppAutoPointsRules.SkipReasons.NoAgreement, now, dbTx, cancellationToken);

            var wallet = await db.UserLoyaltyAccounts.AsNoTracking()
                .Where(w => w.UserId == userId && !w.IsDeleted)
                .Select(w => new { w.IsBlocked, w.BlockedUntil })
                .FirstOrDefaultAsync(cancellationToken);
            if (wallet is not null && OcppAutoPointsRules.IsBlocked(wallet.IsBlocked, wallet.BlockedUntil, now))
                return await SkipAsync(tx, OcppAutoPointsRules.SkipReasons.UserBlocked, now, dbTx, cancellationToken);

            var station = await db.ChargingPoints.FindWithLockAsync("ChargingPoint", stationId, cancellationToken);
            if (station is null || OcppAutoPointsRules.IsBlocked(station.IsLoyaltyBlocked, station.LoyaltyBlockedUntil, now))
                return await SkipAsync(tx, OcppAutoPointsRules.SkipReasons.StationBlocked, now, dbTx, cancellationToken);

            var amount = tx.CostFils.Value / 1000m;
            if (agreement.MinimumTransactionAmount is decimal min && amount < min)
                return await SkipAsync(tx, OcppAutoPointsRules.SkipReasons.BelowMinimumAmount, now, dbTx, cancellationToken);

            var rate = agreement.ConversionRate?.PointsPerUnit
                       ?? (await db.PointsConversionRates.AsNoTracking()
                           .Where(x => x.IsDefault && x.IsActive && !x.IsDeleted)
                           .Select(x => (double?)x.PointsPerUnit).FirstOrDefaultAsync(cancellationToken) ?? 0);

            var f = OcppAutoPointsRules.Compute(amount, agreement.CommissionPercentage, agreement.PointsRewardPercentage, rate);
            if (f.BasePoints <= 0)
                return await SkipAsync(tx, OcppAutoPointsRules.SkipReasons.NoPoints, now, dbTx, cancellationToken);
            if (OcppAutoPointsRules.ExceedsCreditLimit(station.WalletBalance, station.WalletCreditLimit, f.Commission))
                return await SkipAsync(tx, OcppAutoPointsRules.SkipReasons.CreditLimit, now, dbTx, cancellationToken);

            // Commission off the station wallet, as at code creation in the QR flow.
            var walletCovered = Math.Min(Math.Max(station.WalletBalance, 0), f.Commission);
            station.WalletBalance -= f.Commission;

            var code = OcppAutoPointsRules.TransactionCode(tx.Id);
            var pt = new PartnerTransaction
            {
                PartnerAgreementId = agreement.Id,
                UserId = userId,
                TransactionCode = code,
                Status = (int)PartnerTransactionStatus.Completed,
                ProviderType = OcppAutoPointsRules.ProviderType,
                ProviderId = stationId,
                TransactionAmount = f.Amount,
                CurrencyCode = OcppAutoPointsRules.Currency,
                CommissionPercentage = agreement.CommissionPercentage,
                CommissionAmount = f.Commission,
                PointsRewardPercentage = agreement.PointsRewardPercentage,
                PointsConversionRate = rate,
                PointsEligibleAmount = f.EligibleAmount,
                PointsAwarded = f.BasePoints,
                BasePoints = f.BasePoints,
                ConfirmedByUserId = null,   // no staff: the charger's meter is the confirmation
                CodeExpiresAt = now,
                CompletedAt = now,
                WalletCoveredAmount = walletCovered,
            };
            db.PartnerTransactions.Add(pt);
            await db.SaveChanges(cancellationToken);

            if (f.Commission > 0)
                db.ProviderWalletTransactions.Add(new ProviderWalletTransaction
                {
                    ProviderType = OcppAutoPointsRules.ProviderType,
                    ProviderId = stationId,
                    TransactionType = (int)WalletTransactionType.CommissionDeduction,
                    Amount = -f.Commission,
                    BalanceAfter = station.WalletBalance,
                    ReferenceType = "PartnerTransaction",
                    ReferenceId = pt.Id,
                    Note = $"Commission for Cable Connect session #{tx.Id} ({code})",
                    RecordedByUserId = null,
                });

            // Welcome bonus / campaign boost, the same resolution as a scan (this transaction excluded from the history).
            var boost = await loyaltyBoostService.ResolveAsync(userId, OcppAutoPointsRules.ProviderType, stationId, now,
                excludeTransactionId: pt.Id, cancellationToken: cancellationToken);
            if (boost is not null)
            {
                pt.AppliedBoostId = boost.BoostId;
                pt.BoostMultiplier = boost.Multiplier;
                pt.IsWelcomeBonus = boost.IsWelcomeBonus;
                pt.PointsAwarded = OcppAutoPointsRules.Boosted(f.BasePoints, boost.Multiplier);
            }

            // Settlement reads PointsAwarded, so it runs after the boost (same order as ScanPartnerCode).
            await settlementService.UpsertSettlementForPartnerTransactionAsync(pt, cancellationToken);
            await db.SaveChanges(cancellationToken);

            var points = pt.PointsAwarded ?? 0;
            if (points > 0)
                await loyaltyPointService.AwardPointsFromOfferAsync(userId, points, OcppAutoPointsRules.ProviderType, stationId, pt.Id,
                    note: $"Cable Connect session #{tx.Id} points", cancellationToken: cancellationToken);

            tx.PointsAwarded = points;
            tx.PartnerTransactionId = pt.Id;
            tx.PointsProcessedAt = now;
            tx.PointsSkipReason = null;
            await db.SaveChanges(cancellationToken);
            await dbTx.CommitAsync(cancellationToken);

            logger.LogInformation("OcppAutoPoints: session {TransactionId} → user {UserId}: {Points} point(s) for {Amount} JOD at station {StationId} ({Code})",
                tx.Id, userId, points, f.Amount, stationId, code);
            return points;
        }
        catch (DbUpdateException)
        {
            await dbTx.RollbackAsync(cancellationToken);
            (db as DbContext)?.ChangeTracker.Clear();
            // OCPP-{id} already exists (unique code): another run won the race. Nothing to add.
            var code = OcppAutoPointsRules.TransactionCode(transactionId);
            if (!await db.PartnerTransactions.AsNoTracking().AnyAsync(p => p.TransactionCode == code, cancellationToken)) throw;
            logger.LogWarning("OcppAutoPoints: session {TransactionId} already has its partner transaction {Code} — skipped", transactionId, code);
            return null;
        }
        catch
        {
            await dbTx.RollbackAsync(cancellationToken);
            (db as DbContext)?.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task AwardAfterStopAsync(int transactionId, CancellationToken cancellationToken = default)
    {
        try
        {
            await AwardAsync(transactionId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OcppAutoPoints: session {TransactionId} failed after the stop — the price-ocpp-sessions catch-up retries", transactionId);
        }
    }

    public async Task<int> AwardPendingAsync(CancellationToken cancellationToken = default)
    {
        var settings = await AppSettingsProvider.GetOcppAutoPointsAsync(db, cancellationToken);
        if (!settings.Enabled) return 0;

        var now = DateTime.UtcNow;
        var floor = now - OcppAutoPointsRules.CatchUpWindow;
        if (settings.Since is DateTime since && since > floor) floor = since;

        var ids = await db.OcppTransactions.AsNoTracking()
            .Where(t => !t.IsOpen && !t.IsOrphan && !t.WasRejected && t.StartedByUserId != null
                        && (t.StartSource == OcppStartSource.App || t.StartSource == OcppStartSource.Card)
                        && t.PointsProcessedAt == null && t.CostFils != null && t.StoppedAt >= floor)
            .OrderBy(t => t.Id)
            .Select(t => t.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        var awarded = 0;
        foreach (var id in ids)
        {
            try
            {
                if (await AwardAsync(id, cancellationToken) is > 0) awarded++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "OcppAutoPoints: session {TransactionId} failed — retried on the next run", id);
            }
        }
        if (awarded > 0) logger.LogInformation("OcppAutoPoints catch-up: {Count} session(s) awarded", awarded);
        return awarded;
    }

    private async Task<int> SkipAsync(OcppTransaction tx, string reason, DateTime now,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction dbTx, CancellationToken ct)
    {
        tx.PointsAwarded = 0;
        tx.PointsProcessedAt = now;
        tx.PointsSkipReason = reason;
        await db.SaveChanges(ct);
        await dbTx.CommitAsync(ct);
        logger.LogInformation("OcppAutoPoints: session {TransactionId} earns no points ({Reason})", tx.Id, reason);
        return 0;
    }
}
