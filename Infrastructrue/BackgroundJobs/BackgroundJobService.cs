using Application.Common.Interfaces;
using Cable.Core.Emuns;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Application.Settings;
using Application.Subscriptions;
using Cable.Core.Enums;
using System.Text.Json;
using Application.NotificationInbox.Helpers;
using Cable.Core.Constants;

namespace Infrastructrue.BackgroundJobs;

public class BackgroundJobService(
    IApplicationDbContext applicationDbContext,
    INotificationService notificationService,
    IOcppCommandClient ocppCommands,
    ILogger<BackgroundJobService> logger) : IBackgroundJobService
{
    public async Task<int> ExpireOfferTransactionCodesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var expiredTransactions = await applicationDbContext.OfferTransactions
            .Where(x => x.Status == (int)OfferTransactionStatus.Initiated
                         && x.CodeExpiresAt < now
                         && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        // Group by offer to decrement usage counters
        var offerGroups = expiredTransactions.GroupBy(x => x.ProviderOfferId).ToList();

        foreach (var transaction in expiredTransactions)
        {
            transaction.Status = (int)OfferTransactionStatus.Expired;
        }

        // Decrement offer usage counters (were incremented at initiation)
        foreach (var group in offerGroups)
        {
            var count = group.Count();
            await applicationDbContext.ProviderOffers
                .Where(x => x.Id == group.Key && x.CurrentTotalUses >= count)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(o => o.CurrentTotalUses, o => o.CurrentTotalUses - count), cancellationToken);
        }

        if (expiredTransactions.Count > 0)
            await applicationDbContext.SaveChanges(cancellationToken);

        logger.LogInformation("ExpireOfferTransactionCodes: Expired {Count} offer transactions", expiredTransactions.Count);

        return expiredTransactions.Count;
    }

    public async Task<int> ExpirePartnerTransactionCodesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var expiredTransactions = await applicationDbContext.PartnerTransactions
            .Where(x => x.Status == (int)PartnerTransactionStatus.Initiated
                         && x.CodeExpiresAt < now
                         && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var transaction in expiredTransactions)
        {
            transaction.Status = (int)PartnerTransactionStatus.Expired;
        }

        // Refund reserved commission to provider balances
        var grouped = expiredTransactions
            .Where(t => t.CommissionAmount is > 0)
            .GroupBy(t => new { t.ProviderType, t.ProviderId });

        foreach (var group in grouped)
        {
            var totalRefund = group.Sum(t => t.CommissionAmount!.Value);
            var transactionIds = string.Join(", ", group.Select(t => t.Id));

            if (group.Key.ProviderType == "ChargingPoint")
            {
                var cp = await applicationDbContext.ChargingPoints
                    .FirstOrDefaultAsync(x => x.Id == group.Key.ProviderId && !x.IsDeleted, cancellationToken);
                if (cp != null)
                {
                    cp.WalletBalance += totalRefund;
                    applicationDbContext.ProviderWalletTransactions.Add(new ProviderWalletTransaction
                    {
                        ProviderType = group.Key.ProviderType,
                        ProviderId = group.Key.ProviderId,
                        TransactionType = (int)WalletTransactionType.CommissionRefund,
                        Amount = totalRefund,
                        BalanceAfter = cp.WalletBalance,
                        ReferenceType = "ExpiredPartnerTransactions",
                        Note = $"Batch refund for {group.Count()} expired transactions (IDs: {transactionIds})",
                        RecordedByUserId = cp.OwnerId
                    });
                }
            }
            else if (group.Key.ProviderType == "ServiceProvider")
            {
                var sp = await applicationDbContext.ServiceProviders
                    .FirstOrDefaultAsync(x => x.Id == group.Key.ProviderId && !x.IsDeleted, cancellationToken);
                if (sp != null)
                {
                    sp.WalletBalance += totalRefund;
                    applicationDbContext.ProviderWalletTransactions.Add(new ProviderWalletTransaction
                    {
                        ProviderType = group.Key.ProviderType,
                        ProviderId = group.Key.ProviderId,
                        TransactionType = (int)WalletTransactionType.CommissionRefund,
                        Amount = totalRefund,
                        BalanceAfter = sp.WalletBalance,
                        ReferenceType = "ExpiredPartnerTransactions",
                        Note = $"Batch refund for {group.Count()} expired transactions (IDs: {transactionIds})",
                        RecordedByUserId = sp.OwnerId
                    });
                }
            }
        }

        if (expiredTransactions.Count > 0)
            await applicationDbContext.SaveChanges(cancellationToken);

        logger.LogInformation("ExpirePartnerTransactionCodes: Expired {Count} partner transactions", expiredTransactions.Count);

        return expiredTransactions.Count;
    }

    // ==========================================
    // CRITICAL: Security Cleanup
    // ==========================================

    public async Task<int> CleanupExpiredPhoneVerificationsAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);

        var expiredRecords = await applicationDbContext.PhoneVerifications
            .Where(x => !x.IsDeleted
                         && (x.ExpiresAt < cutoff || (x.IsUsed && x.ExpiresAt < DateTime.UtcNow)))
            .ToListAsync(cancellationToken);

        foreach (var record in expiredRecords)
        {
            record.IsDeleted = true;
        }

        if (expiredRecords.Count > 0)
            await applicationDbContext.SaveChanges(cancellationToken);

        logger.LogInformation("CleanupExpiredPhoneVerifications: Cleaned up {Count} records", expiredRecords.Count);

        return expiredRecords.Count;
    }

    public async Task<int> CleanupExpiredPasswordResetsAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);

        var expiredRecords = await applicationDbContext.PasswordResets
            .Where(x => x.ExpiresAt < cutoff || (x.IsUsed && x.ExpiresAt < DateTime.UtcNow))
            .ToListAsync(cancellationToken);

        if (expiredRecords.Count > 0)
        {
            applicationDbContext.PasswordResets.RemoveRange(expiredRecords);
            await applicationDbContext.SaveChanges(cancellationToken);
        }

        logger.LogInformation("CleanupExpiredPasswordResets: Cleaned up {Count} records", expiredRecords.Count);

        return expiredRecords.Count;
    }

    public async Task<int> CleanupExpiredOtpRateLimitsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.AddHours(-24);

        // Unblock numbers where block period has passed
        var blockedRecords = await applicationDbContext.OtpRateLimits
            .Where(x => x.IsBlocked && x.BlockedUntil != null && x.BlockedUntil < now)
            .ToListAsync(cancellationToken);

        foreach (var record in blockedRecords)
        {
            record.IsBlocked = false;
            record.BlockedUntil = null;
        }

        // Delete old rate limit records (older than 24 hours)
        var staleRecords = await applicationDbContext.OtpRateLimits
            .Where(x => x.WindowStart < cutoff && !x.IsBlocked)
            .ToListAsync(cancellationToken);

        if (staleRecords.Count > 0)
            applicationDbContext.OtpRateLimits.RemoveRange(staleRecords);

        var totalAffected = blockedRecords.Count + staleRecords.Count;

        if (totalAffected > 0)
            await applicationDbContext.SaveChanges(cancellationToken);

        logger.LogInformation(
            "CleanupExpiredOtpRateLimits: Unblocked {Unblocked}, deleted {Deleted} stale records",
            blockedRecords.Count, staleRecords.Count);

        return totalAffected;
    }

    // ==========================================
    // IMPORTANT: Business Expiry
    // ==========================================

    public async Task<int> DeactivateExpiredOffersAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var expiredOffers = await applicationDbContext.ProviderOffers
            .Where(x => x.IsActive
                         && x.ValidTo != null
                         && x.ValidTo < now
                         && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var offer in expiredOffers)
        {
            offer.IsActive = false;
        }

        if (expiredOffers.Count > 0)
            await applicationDbContext.SaveChanges(cancellationToken);

        logger.LogInformation("DeactivateExpiredOffers: Deactivated {Count} offers", expiredOffers.Count);

        return expiredOffers.Count;
    }

    public async Task<int> DeactivateExpiredSharedLinksAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var expiredLinks = await applicationDbContext.SharedLinks
            .Where(x => x.IsActive
                         && !x.IsDeleted
                         && ((x.ExpiresAt != null && x.ExpiresAt < now)
                              || x.CurrentUsage >= x.MaxUsage))
            .ToListAsync(cancellationToken);

        foreach (var link in expiredLinks)
        {
            link.IsActive = false;
        }

        if (expiredLinks.Count > 0)
            await applicationDbContext.SaveChanges(cancellationToken);

        logger.LogInformation("DeactivateExpiredSharedLinks: Deactivated {Count} links", expiredLinks.Count);

        return expiredLinks.Count;
    }

    public async Task<int> EndExpiredLoyaltySeasonsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var expiredSeasons = await applicationDbContext.LoyaltySeasons
            .Where(x => x.IsActive
                         && x.EndDate < now
                         && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var season in expiredSeasons)
        {
            season.IsActive = false;
        }

        if (expiredSeasons.Count > 0)
            await applicationDbContext.SaveChanges(cancellationToken);

        logger.LogInformation("EndExpiredLoyaltySeasons: Ended {Count} seasons", expiredSeasons.Count);

        return expiredSeasons.Count;
    }

    public async Task<int> ExpireLoyaltyPointsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // Find Earn transactions with ExpiresAt that have passed and haven't been expired yet
        var expiredTransactions = await applicationDbContext.LoyaltyPointTransactions
            .Where(x => x.ExpiresAt != null
                         && x.ExpiresAt < now
                         && x.TransactionType == (int)TransactionType.Earn
                         && x.Points > 0
                         && !x.IsDeleted)
            .Include(x => x.Account)
            .ToListAsync(cancellationToken);

        var expiredCount = 0;

        foreach (var transaction in expiredTransactions)
        {
            var account = transaction.Account;
            if (account == null || account.IsDeleted) continue;

            // Deduct expired points from balance (don't go below 0)
            var pointsToExpire = Math.Min(transaction.Points, account.CurrentBalance);
            if (pointsToExpire <= 0)
            {
                // Mark as expired even if no balance to deduct (prevent reprocessing)
                transaction.TransactionType = (int)TransactionType.Expired;
                transaction.Points = 0;
                expiredCount++;
                continue;
            }

            account.CurrentBalance -= pointsToExpire;
            account.TotalPointsRedeemed += pointsToExpire;

            // Mark original transaction as expired
            transaction.TransactionType = (int)TransactionType.Expired;
            transaction.Points = 0;

            // Create audit trail
            var expiryTransaction = new LoyaltyPointTransaction
            {
                UserLoyaltyAccountId = account.Id,
                TransactionType = (int)TransactionType.Expired,
                Points = -pointsToExpire,
                BalanceAfter = account.CurrentBalance,
                ReferenceType = "PointExpiry",
                ReferenceId = transaction.Id,
                Note = $"Points expired from transaction #{transaction.Id}",
                LoyaltySeasonId = transaction.LoyaltySeasonId
            };

            applicationDbContext.LoyaltyPointTransactions.Add(expiryTransaction);
            expiredCount++;
        }

        if (expiredCount > 0)
            await applicationDbContext.SaveChanges(cancellationToken);

        logger.LogInformation("ExpireLoyaltyPoints: Expired {Count} point transactions", expiredCount);

        return expiredCount;
    }

    public async Task<int> DeactivateExpiredRewardsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var expiredRewards = await applicationDbContext.LoyaltyRewards
            .Where(x => x.IsActive
                         && !x.IsDeleted
                         && ((x.ValidTo != null && x.ValidTo < now)
                              || (x.MaxRedemptions != null && x.CurrentRedemptions >= x.MaxRedemptions)))
            .ToListAsync(cancellationToken);

        foreach (var reward in expiredRewards)
        {
            reward.IsActive = false;
        }

        if (expiredRewards.Count > 0)
            await applicationDbContext.SaveChanges(cancellationToken);

        logger.LogInformation("DeactivateExpiredRewards: Deactivated {Count} rewards", expiredRewards.Count);

        return expiredRewards.Count;
    }

    public async Task<int> UnblockExpiredLoyaltyBlocksAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var expiredBlocks = await applicationDbContext.UserLoyaltyAccounts
            .Where(x => x.IsBlocked
                         && x.BlockedUntil != null
                         && x.BlockedUntil < now
                         && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var account in expiredBlocks)
        {
            account.IsBlocked = false;
            account.BlockedAt = null;
            account.BlockedUntil = null;
            account.BlockReason = null;
            account.BlockedByUserId = null;
            account.ModifiedAt = now;
        }

        if (expiredBlocks.Count > 0)
            await applicationDbContext.SaveChanges(cancellationToken);

        logger.LogInformation("UnblockExpiredLoyaltyBlocks: Unblocked {Count} accounts", expiredBlocks.Count);

        return expiredBlocks.Count;
    }

    public async Task<int> UnblockExpiredProviderLoyaltyBlocksAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var count = 0;

        // ChargingPoints
        var blockedCps = await applicationDbContext.ChargingPoints
            .Where(x => x.IsLoyaltyBlocked
                         && x.LoyaltyBlockedUntil != null
                         && x.LoyaltyBlockedUntil < now
                         && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var cp in blockedCps)
        {
            cp.IsLoyaltyBlocked = false;
            cp.LoyaltyBlockedAt = null;
            cp.LoyaltyBlockedUntil = null;
            cp.LoyaltyBlockReason = null;
            cp.LoyaltyBlockedByUserId = null;
            cp.ModifiedAt = now;
        }

        // ServiceProviders
        var blockedSps = await applicationDbContext.ServiceProviders
            .Where(x => x.IsLoyaltyBlocked
                         && x.LoyaltyBlockedUntil != null
                         && x.LoyaltyBlockedUntil < now
                         && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var sp in blockedSps)
        {
            sp.IsLoyaltyBlocked = false;
            sp.LoyaltyBlockedAt = null;
            sp.LoyaltyBlockedUntil = null;
            sp.LoyaltyBlockReason = null;
            sp.LoyaltyBlockedByUserId = null;
            sp.ModifiedAt = now;
        }

        count = blockedCps.Count + blockedSps.Count;

        if (count > 0)
            await applicationDbContext.SaveChanges(cancellationToken);

        logger.LogInformation("UnblockExpiredProviderLoyaltyBlocks: Unblocked {Count} providers", count);

        return count;
    }


    public async Task<int> ApplySubscriptionGraceAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var global = await AppSettingsProvider.GetSubscriptionGraceAsync(applicationDbContext, cancellationToken);

        var lapsed = await applicationDbContext.Subscriptions
            .Where(s => !s.IsDeleted && !s.IsSwitchedOff && s.ExpiresAt < now)
            .ToListAsync(cancellationToken);

        var switchedOff = 0;
        foreach (var s in lapsed)
        {
            var grace = SubscriptionPeriodCalculator.EffectiveGrace(s, global);
            var autoOff = SubscriptionPeriodCalculator.AutoOffAt(s, grace);
            if (autoOff is null || now <= autoOff.Value) continue;   // manual, or still inside the window

            s.IsSwitchedOff = true;
            s.SwitchedOffAt = now;
            s.SwitchedOffByUserId = null;                            // the job, not a person
            await SubscriptionEntitySync.ApplyAsync(applicationDbContext, s, on: false, now, cancellationToken);
            switchedOff++;
        }

        if (switchedOff > 0)
            await applicationDbContext.SaveChanges(cancellationToken);

        logger.LogInformation("ApplySubscriptionGrace: switched off {Count} lapsed subscription(s)", switchedOff);
        return switchedOff;
    }

    // ==========================================
    // Cable Connect (OCPP)
    // ==========================================

    public async Task<int> MarkStaleOcppTransactionsAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - OcppLimits.StaleTransactionAfter;

        // R6: never close them — a late StopTransaction may still arrive. Just flag for the admin.
        var marked = await applicationDbContext.OcppTransactions
            .Where(t => t.IsOpen && !t.IsStale && t.StartedAt < cutoff)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.IsStale, true), cancellationToken);

        if (marked > 0)
            logger.LogInformation("MarkStaleOcppTransactions: flagged {Count} session(s) open for more than 24 h", marked);
        return marked;
    }

    public async Task<int> PurgeOcppRawMessagesAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-OcppLimits.RawMessageRetentionDays);
        var total = 0;

        // Batched so a month of 15-second meter samples never becomes one giant delete.
        while (true)
        {
            var batchIds = await applicationDbContext.OcppRawMessages
                .Where(m => m.CreatedAt < cutoff)
                .OrderBy(m => m.Id)
                .Select(m => m.Id)
                .Take(5000)
                .ToListAsync(cancellationToken);
            if (batchIds.Count == 0) break;

            total += await applicationDbContext.OcppRawMessages
                .Where(m => batchIds.Contains(m.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (total > 0)
            logger.LogInformation("PurgeOcppRawMessages: deleted {Count} frame(s) older than {Days} days", total, OcppLimits.RawMessageRetentionDays);
        return total;
    }

    public async Task NotifyOcppFaultAsync(int ocppConnectorId, string errorCode, string? info, CancellationToken cancellationToken = default)
    {
        var connector = await applicationDbContext.OcppConnectors.AsNoTracking()
            .Where(c => c.Id == ocppConnectorId)
            .Select(c => new
            {
                c.ConnectorId,
                c.ChargePoint.ChargePointId,
                c.ChargePoint.DisplayName,
                c.ChargePoint.ChargingPointId,
                StationName = c.ChargePoint.ChargingPoint.Name,
                c.ChargePoint.ChargingPoint.OwnerId,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (connector is null)
        {
            logger.LogWarning("NotifyOcppFault: connector {ConnectorId} no longer exists", ocppConnectorId);
            return;
        }

        var recipients = await applicationDbContext.ProviderManagers.AsNoTracking()
            .Where(m => m.ProviderType == "ChargingPoint" && m.ProviderId == connector.ChargingPointId
                        && m.IsActive && !m.IsDeleted)
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);
        if (connector.OwnerId is int ownerId) recipients.Add(ownerId);
        recipients = recipients.Distinct().ToList();
        if (recipients.Count == 0) return;

        var chargerName = connector.DisplayName ?? connector.ChargePointId;
        var where = connector.ConnectorId == 0 ? "" : $" – المقبس {connector.ConnectorId}";
        // Station names often already start with "محطة"; don't produce "محطة محطة الخليفة".
        var stationName = (connector.StationName ?? "").Trim();
        var stationLabel = stationName.StartsWith("محطة", StringComparison.Ordinal) ? stationName : $"محطة {stationName}";
        var title = $"عطل في الشاحن {chargerName}";
        var body = $"{stationLabel}: الشاحن {chargerName}{where} أبلغ عن عطل ({errorCode})"
                   + (string.IsNullOrWhiteSpace(info) ? "" : $" — {info}");
        var data = JsonSerializer.Serialize(new
        {
            type = "ocpp_fault",
            chargingPointId = connector.ChargingPointId,
            chargePointId = connector.ChargePointId,
            connectorId = connector.ConnectorId,
            errorCode,
        });

        var tokens = await applicationDbContext.NotificationTokens.AsNoTracking()
            .Where(t => recipients.Contains(t.UserId))
            .Select(t => new { t.AppType, t.Token })
            .ToListAsync(cancellationToken);

        foreach (var group in tokens.GroupBy(t => t.AppType))
        {
            try
            {
                await notificationService.SendMessagesAsync(group.Select(x => x.Token).ToList(), title, body, group.Key);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "NotifyOcppFault: FCM send failed for app type {AppType}", group.Key);
            }
        }

        var typeId = await applicationDbContext.NotificationTypes.AsNoTracking()
            .Where(t => t.Name == "charging_point_status_changed")
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(cancellationToken) ?? 1;

        await NotificationInboxHelper.CreateNotificationInboxRecordsAsync(
            applicationDbContext, recipients, typeId, title, body, null, data, cancellationToken);

        logger.LogInformation("NotifyOcppFault: {Charger} connector {Connector} {ErrorCode} → {Count} recipient(s)",
            connector.ChargePointId, connector.ConnectorId, errorCode, recipients.Count);
    }

    public async Task<int> SyncOcppLocalListAsync(int chargingPointId, int? ocppChargePointId = null, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // The station's current allow-list, as the unit should hold it: enabled, not expired.
        // Expiry goes along so the unit refuses the card after that date even while offline.
        var cards = await applicationDbContext.OcppAuthorizedTags.AsNoTracking()
            .Where(t => t.ChargingPointId == chargingPointId && t.IsEnabled && !t.IsDeleted
                        && (t.ExpiresAt == null || t.ExpiresAt > now))
            .OrderBy(t => t.IdTag)
            .Select(t => new { t.IdTag, t.ExpiresAt })
            .ToListAsync(cancellationToken);

        if (cards.Count > OcppLimits.LocalListMaxEntries)
        {
            logger.LogWarning("SyncOcppLocalList: station {StationId} has {Count} cards, above the {Max} the units accept — sending the first {Max}",
                chargingPointId, cards.Count, OcppLimits.LocalListMaxEntries, OcppLimits.LocalListMaxEntries);
            cards = cards.Take(OcppLimits.LocalListMaxEntries).ToList();
        }

        var chargers = await applicationDbContext.OcppChargePoints
            .Where(c => c.ChargingPointId == chargingPointId && !c.IsDeleted && c.IsEnabled
                        && (ocppChargePointId == null || c.Id == ocppChargePointId)
                        && (ocppChargePointId != null || c.LocalListStatus != OcppLocalListStatus.NotSupported))
            .ToListAsync(cancellationToken);
        if (chargers.Count == 0) return 0;

        // Version = Unix seconds of this push: always increasing, no bookkeeping, and a Full
        // update every time so a missed push can never leave the unit with a stale diff.
        var version = (int)Math.Min(new DateTimeOffset(now).ToUnixTimeSeconds(), int.MaxValue);
        var list = cards.Select(c => new Dictionary<string, object>
        {
            ["idTag"] = c.IdTag,
            ["idTagInfo"] = c.ExpiresAt is DateTime exp
                ? new Dictionary<string, object> { ["status"] = "Accepted", ["expiryDate"] = exp }
                : new Dictionary<string, object> { ["status"] = "Accepted" },
        }).ToList();
        var payload = new Dictionary<string, object>
        {
            ["listVersion"] = version,
            ["localAuthorizationList"] = list,
            ["updateType"] = "Full",
        };

        var confirmed = 0;
        foreach (var cp in chargers)
        {
            var outcome = await ocppCommands.SendAsync(cp.ChargePointId, "SendLocalList", payload, cancellationToken);
            var verdict = outcome.Answered ? ReadStatus(outcome.Payload) : null;
            Audit(cp.Id, "SendLocalList", $"{{\"listVersion\":{version},\"updateType\":\"Full\",\"cards\":{cards.Count}}}", outcome, verdict);

            if (outcome.Answered && verdict == "Accepted")
            {
                // The unit may still answer Authorize from cached decisions; drop them so the new list rules.
                var clear = await ocppCommands.SendAsync(cp.ChargePointId, "ClearCache", new Dictionary<string, object>(), cancellationToken);
                Audit(cp.Id, "ClearCache", "{}", clear, clear.Answered ? ReadStatus(clear.Payload) : null);

                cp.LocalListVersion = version;
                cp.LocalListSyncedAt = now;
                cp.LocalListStatus = OcppLocalListStatus.Synced;
                confirmed++;
            }
            else if (verdict == "NotSupported" || outcome.Status == "CallError" && outcome.ErrorCode is "NotImplemented" or "NotSupported")
            {
                cp.LocalListStatus = OcppLocalListStatus.NotSupported;
                logger.LogInformation("SyncOcppLocalList: {ChargePointId} has no local list support — not retrying", cp.ChargePointId);
            }
            else if (outcome.Status == "NotConnected")
            {
                // Pushed again when it boots (Cable.Ocpp enqueues on BootNotification for Pending units).
                cp.LocalListStatus = OcppLocalListStatus.Pending;
            }
            else
            {
                cp.LocalListStatus = OcppLocalListStatus.Failed;
                logger.LogWarning("SyncOcppLocalList: {ChargePointId} answered {Status} / {Verdict} {Error}",
                    cp.ChargePointId, outcome.Status, verdict, outcome.ErrorDescription);
            }
        }

        await applicationDbContext.SaveChanges(cancellationToken);
        logger.LogInformation("SyncOcppLocalList: station {StationId}, {Cards} card(s) v{Version} → {Confirmed}/{Total} charger(s) confirmed",
            chargingPointId, cards.Count, version, confirmed, chargers.Count);
        return confirmed;

        void Audit(int cpId, string action, string request, OcppCommandOutcome o, string? resultStatus) =>
            applicationDbContext.OcppCommands.Add(new OcppCommand
            {
                OcppChargePointId = cpId,
                Action = action,
                RequestPayload = request,
                Status = o.Status,
                ResultStatus = resultStatus,
                ResponsePayload = o.Payload,
                ErrorCode = o.ErrorCode,
                ErrorDescription = o.ErrorDescription is { Length: > 500 } d ? d[..500] : o.ErrorDescription,
                DurationMs = (int)Math.Min(o.ElapsedMs, int.MaxValue),
            });

        static string? ReadStatus(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String
                    ? st.GetString() : null;
            }
            catch (JsonException) { return null; }
        }
    }

    public async Task<int> CheckOcppAlertsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var opened = 0;

        var open = await applicationDbContext.OcppAlerts
            .Where(a => a.ResolvedAt == null && !a.IsDeleted)
            .ToListAsync(cancellationToken);

        // ---- 1. Charger offline too long: socket gone, or open but silent (no message) for the threshold.
        var offlineCutoff = now - OcppLimits.OfflineAlertAfter;
        var offline = await applicationDbContext.OcppChargePoints.AsNoTracking()
            .Where(c => !c.IsDeleted && c.IsEnabled && c.LastBootAt != null
                        && ((!c.IsConnected && c.DisconnectedAt != null && c.DisconnectedAt < offlineCutoff)
                            || (c.IsConnected && c.LastMessageAt != null && c.LastMessageAt < offlineCutoff)))
            .Select(c => new AlertTarget(c.Id, c.ChargePointId, c.DisplayName, c.ChargingPointId, c.ChargingPoint.Name, c.ChargingPoint.OwnerId,
                null, null, (c.IsConnected ? c.LastMessageAt : c.DisconnectedAt)!.Value, null))
            .ToListAsync(cancellationToken);

        foreach (var t in offline)
        {
            if (open.Any(a => a.Type == OcppAlertType.ChargerOffline && a.OcppChargePointId == t.ChargePointRowId)) continue;
            var minutes = (int)(now - t.Since).TotalMinutes;
            var recipients = await NotifyOcppAlertAsync(t, OcppAlertType.ChargerOffline,
                $"الشاحن {t.Name} غير متصل",
                $"{t.StationLabel}: الشاحن {t.Name} منقطع عن كيبل منذ {minutes} دقيقة", cancellationToken);
            open.Add(AddAlert(t, OcppAlertType.ChargerOffline, $"offline {minutes} min", recipients, now));
            opened++;
        }

        // Back online → close + tell them.
        var offlineIds = offline.Select(t => t.ChargePointRowId).ToHashSet();
        foreach (var a in open.Where(a => a.Type == OcppAlertType.ChargerOffline && !offlineIds.Contains(a.OcppChargePointId)).ToList())
        {
            var t = await LoadTargetAsync(a.OcppChargePointId, cancellationToken);
            if (t is not null)
                await NotifyOcppAlertAsync(t, OcppAlertType.ChargerOffline, $"عاد الشاحن {t.Name} للاتصال",
                    $"{t.StationLabel}: الشاحن {t.Name} متصل بكيبل من جديد", cancellationToken, resolved: true);
            a.ResolvedAt = now;
        }

        // ---- 2. Plug Faulted too long (the immediate fault push already went out; this is the escalation).
        var faultCutoff = now - OcppLimits.FaultedAlertAfter;
        var faulted = await applicationDbContext.OcppConnectors.AsNoTracking()
            .Where(k => k.Status == OcppConnectorStatus.Faulted && !k.ChargePoint.IsDeleted && k.ChargePoint.IsEnabled
                        && (k.StatusUpdatedAt ?? k.StatusReceivedAt) < faultCutoff)
            .Select(k => new AlertTarget(k.ChargePoint.Id, k.ChargePoint.ChargePointId, k.ChargePoint.DisplayName, k.ChargePoint.ChargingPointId,
                k.ChargePoint.ChargingPoint.Name, k.ChargePoint.ChargingPoint.OwnerId,
                k.ConnectorId, null, k.StatusUpdatedAt ?? k.StatusReceivedAt, k.ErrorCode))
            .ToListAsync(cancellationToken);

        foreach (var t in faulted)
        {
            if (open.Any(a => a.Type == OcppAlertType.ConnectorFaulted && a.OcppChargePointId == t.ChargePointRowId && a.ConnectorId == t.ConnectorId)) continue;
            var minutes = (int)(now - t.Since).TotalMinutes;
            var recipients = await NotifyOcppAlertAsync(t, OcppAlertType.ConnectorFaulted,
                $"عطل مستمر في الشاحن {t.Name}",
                $"{t.StationLabel}: الشاحن {t.Name}{t.Where} في حالة عطل ({t.Detail}) منذ {minutes} دقيقة", cancellationToken);
            open.Add(AddAlert(t, OcppAlertType.ConnectorFaulted, t.Detail, recipients, now));
            opened++;
        }

        var faultedKeys = faulted.Select(t => (t.ChargePointRowId, t.ConnectorId)).ToHashSet();
        foreach (var a in open.Where(a => a.Type == OcppAlertType.ConnectorFaulted && !faultedKeys.Contains((a.OcppChargePointId, a.ConnectorId))).ToList())
        {
            var t = await LoadTargetAsync(a.OcppChargePointId, cancellationToken, a.ConnectorId);
            if (t is not null)
                await NotifyOcppAlertAsync(t, OcppAlertType.ConnectorFaulted, $"انتهى العطل في الشاحن {t.Name}",
                    $"{t.StationLabel}: الشاحن {t.Name}{t.Where} عاد للعمل", cancellationToken, resolved: true);
            a.ResolvedAt = now;
        }

        // ---- 3. Session open too long (a car forgotten on the plug, or a charger that never sent Stop).
        var sessionCutoff = now - OcppLimits.LongSessionAlertAfter;
        var longSessions = await applicationDbContext.OcppTransactions.AsNoTracking()
            .Where(x => x.IsOpen && !x.IsStale && !x.WasRejected && x.StartedAt < sessionCutoff && !x.ChargePoint.IsDeleted)
            .Select(x => new AlertTarget(x.ChargePoint.Id, x.ChargePoint.ChargePointId, x.ChargePoint.DisplayName, x.ChargePoint.ChargingPointId,
                x.ChargePoint.ChargingPoint.Name, x.ChargePoint.ChargingPoint.OwnerId,
                x.ConnectorId, x.Id, x.StartedAt, x.IdTag))
            .ToListAsync(cancellationToken);

        foreach (var t in longSessions)
        {
            if (open.Any(a => a.Type == OcppAlertType.SessionTooLong && a.OcppTransactionId == t.TransactionId)) continue;
            var hours = (int)(now - t.Since).TotalHours;
            var recipients = await NotifyOcppAlertAsync(t, OcppAlertType.SessionTooLong,
                $"جلسة شحن طويلة على الشاحن {t.Name}",
                $"{t.StationLabel}: الشاحن {t.Name}{t.Where} يشحن منذ {hours} ساعة (البطاقة {t.Detail})", cancellationToken);
            open.Add(AddAlert(t, OcppAlertType.SessionTooLong, $"idTag {t.Detail}, {hours} h", recipients, now));
            opened++;
        }

        var longIds = longSessions.Select(t => t.TransactionId).ToHashSet();
        foreach (var a in open.Where(a => a.Type == OcppAlertType.SessionTooLong && !longIds.Contains(a.OcppTransactionId)).ToList())
            a.ResolvedAt = now;   // the session ended (or went stale) — no push for that

        // ---- 4. Charging finished but the car is still plugged in (Finishing / SuspendedEV): the plug is
        //         blocked for the next driver. Stage 1 → the driver, if the card is linked to a user;
        //         stage 2 (or stage 1 when no driver is known) → the station owner / managers.
        var parkedCutoff = now - OcppLimits.ParkedAlertAfter;
        var parked = await applicationDbContext.OcppConnectors.AsNoTracking()
            .Where(k => k.ConnectorId > 0 && !k.ChargePoint.IsDeleted && k.ChargePoint.IsEnabled
                        && (k.Status == OcppConnectorStatus.Finishing || k.Status == OcppConnectorStatus.SuspendedEV)
                        && (k.StatusUpdatedAt ?? k.StatusReceivedAt) < parkedCutoff)
            .Select(k => new AlertTarget(k.ChargePoint.Id, k.ChargePoint.ChargePointId, k.ChargePoint.DisplayName, k.ChargePoint.ChargingPointId,
                k.ChargePoint.ChargingPoint.Name, k.ChargePoint.ChargingPoint.OwnerId,
                k.ConnectorId, null, k.StatusUpdatedAt ?? k.StatusReceivedAt, k.Status))
            .ToListAsync(cancellationToken);

        foreach (var t in parked)
        {
            if (open.Any(a => a.Type == OcppAlertType.ParkedAfterCharging && a.OcppChargePointId == t.ChargePointRowId && a.ConnectorId == t.ConnectorId)) continue;

            // Whose car? The latest session on that plug → its idTag → OcppUserIdTag.
            var lastTag = await applicationDbContext.OcppTransactions.AsNoTracking()
                .Where(x => x.OcppChargePointId == t.ChargePointRowId && x.ConnectorId == t.ConnectorId && !x.WasRejected)
                .OrderByDescending(x => x.StartedAt)
                .Select(x => x.IdTag)
                .FirstOrDefaultAsync(cancellationToken);
            var driverId = lastTag is null ? null : await applicationDbContext.OcppUserIdTags.AsNoTracking()
                .Where(u => u.IdTag == lastTag && u.IsEnabled && !u.IsDeleted)
                .Select(u => (int?)u.UserId)
                .FirstOrDefaultAsync(cancellationToken);

            var minutes = (int)(now - t.Since).TotalMinutes;
            int recipients;
            DateTime? escalatedAt = null;
            if (driverId is int driver)
            {
                recipients = await NotifyOcppAlertAsync(t, OcppAlertType.ParkedAfterCharging,
                    "سيارتك خلصت شحن",
                    $"{t.StationLabel}: انتهى شحن سيارتك على {t.Name}{t.Where} منذ {minutes} دقيقة — ممكن تفصلها لتفسح المقبس للسواق التالي؟",
                    cancellationToken, onlyUsers: [driver], appType: FirebaseAppType.UserApp);
            }
            else
            {
                recipients = await NotifyOcppAlertAsync(t, OcppAlertType.ParkedAfterCharging,
                    $"مقبس محجوز بدون شحن على {t.Name}",
                    $"{t.StationLabel}: {t.Name}{t.Where} خلص الشحن والسيارة ما زالت موصولة منذ {minutes} دقيقة",
                    cancellationToken, includeAdmins: false);
                escalatedAt = now;
            }
            var row = AddAlert(t, OcppAlertType.ParkedAfterCharging, $"{t.Detail} {minutes} min, tag {lastTag ?? "?"}", recipients, now);
            row.DriverUserId = driverId;
            row.EscalatedAt = escalatedAt;
            open.Add(row);
            opened++;
        }

        // Stage 2: the driver was told, the car is still there → the station.
        var parkedKeys = parked.Select(t => (t.ChargePointRowId, t.ConnectorId)).ToHashSet();
        foreach (var a in open.Where(a => a.Type == OcppAlertType.ParkedAfterCharging && a.EscalatedAt == null
                                          && a.NotifiedAt <= now - OcppLimits.ParkedEscalateAfter
                                          && parkedKeys.Contains((a.OcppChargePointId, a.ConnectorId))).ToList())
        {
            var t = parked.First(x => x.ChargePointRowId == a.OcppChargePointId && x.ConnectorId == a.ConnectorId);
            var minutes = (int)(now - t.Since).TotalMinutes;
            a.Recipients += await NotifyOcppAlertAsync(t, OcppAlertType.ParkedAfterCharging,
                $"مقبس محجوز بدون شحن على {t.Name}",
                $"{t.StationLabel}: {t.Name}{t.Where} خلص الشحن والسيارة ما زالت موصولة منذ {minutes} دقيقة (تم تنبيه السواق قبل {(int)OcppLimits.ParkedEscalateAfter.TotalMinutes} دقيقة)",
                cancellationToken, includeAdmins: false);
            a.EscalatedAt = now;
        }

        // Cable removed (or a new session started): close silently.
        foreach (var a in open.Where(a => a.Type == OcppAlertType.ParkedAfterCharging && !parkedKeys.Contains((a.OcppChargePointId, a.ConnectorId))).ToList())
            a.ResolvedAt = now;

        await applicationDbContext.SaveChanges(cancellationToken);
        if (opened > 0)
            logger.LogInformation("CheckOcppAlerts: opened {Opened} alert(s); {Open} open in total", opened, open.Count(a => a.ResolvedAt == null));
        return opened;

        OcppAlert AddAlert(AlertTarget t, string type, string? details, int recipients, DateTime at)
        {
            var row = new OcppAlert
            {
                OcppChargePointId = t.ChargePointRowId,
                Type = type,
                ConnectorId = t.ConnectorId,
                OcppTransactionId = t.TransactionId,
                ConditionSince = t.Since,
                NotifiedAt = at,
                Details = details is { Length: > 300 } d ? d[..300] : details,
                Recipients = recipients,
            };
            applicationDbContext.OcppAlerts.Add(row);
            return row;
        }
    }

    public async Task<int> ComputeOcppReliabilityAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var windowStart = now.AddDays(-OcppLimits.ReliabilityWindowDays);
        var minutes = (int)(now - windowStart).TotalMinutes;

        var chargers = await applicationDbContext.OcppChargePoints
            .Where(c => !c.IsDeleted)
            .ToListAsync(cancellationToken);
        var computed = 0;

        foreach (var cp in chargers)
        {
            // ---- events in the window, oldest first
            var sys = await applicationDbContext.OcppRawMessages.AsNoTracking()
                .Where(m => m.ChargePointId == cp.ChargePointId && m.Direction == "sys" && m.CreatedAt >= windowStart
                            && (m.Action == "CONNECT" || m.Action == "DISCONNECT"))
                .OrderBy(m => m.Id)
                .Select(m => new { m.Action, m.Payload, m.CreatedAt })
                .ToListAsync(cancellationToken);
            var statuses = await applicationDbContext.OcppRawMessages.AsNoTracking()
                .Where(m => m.ChargePointId == cp.ChargePointId && m.Direction == "in " && m.Action == "StatusNotification" && m.CreatedAt >= windowStart)
                .OrderBy(m => m.Id)
                .Select(m => new { m.Payload, m.CreatedAt })
                .ToListAsync(cancellationToken);

            // A unit that never connected in the window and never booted at all has no score.
            if (sys.Count == 0 && cp.LastBootAt == null)
            {
                cp.ReliabilityPct = null; cp.ReliabilityOnlinePct = null; cp.ReliabilityFaultFreePct = null;
                cp.ReliabilityOfflineIncidents = null; cp.ReliabilityFaultIncidents = null; cp.ReliabilityComputedAt = now;
                continue;
            }

            // ---- state at the window start: the last connect/disconnect before it
            var before = await applicationDbContext.OcppRawMessages.AsNoTracking()
                .Where(m => m.ChargePointId == cp.ChargePointId && m.Direction == "sys" && m.CreatedAt < windowStart
                            && (m.Action == "CONNECT" || m.Action == "DISCONNECT"))
                .OrderByDescending(m => m.Id)
                .Select(m => m.Action)
                .FirstOrDefaultAsync(cancellationToken);
            var online = before == "CONNECT";
            var excluded = false;   // inside one of our own restarts

            var onlineMin = new bool[minutes];
            var excludedMin = new bool[minutes];
            var faultedMin = new bool[minutes];
            int Idx(DateTime t) => Math.Clamp((int)(t - windowStart).TotalMinutes, 0, minutes - 1);

            var cursor = 0;
            var offlineIncidents = 0;
            foreach (var e in sys)
            {
                var at = Idx(e.CreatedAt);
                for (var i = cursor; i < at; i++) { onlineMin[i] = online; excludedMin[i] = excluded; }
                cursor = at;
                if (e.Action == "CONNECT") { online = true; excluded = false; }
                else
                {
                    online = false;
                    // "server shutting down" = our process stopped, not the charger: the gap until it reconnects is not counted.
                    excluded = e.Payload != null && e.Payload.Contains("server shutting down", StringComparison.OrdinalIgnoreCase);
                    if (!excluded) offlineIncidents++;
                }
            }
            for (var i = cursor; i < minutes; i++) { onlineMin[i] = online; excludedMin[i] = excluded; }

            // ---- faulted plugs: StatusNotification Faulted … until the next status for that connector
            var faultSince = new Dictionary<int, int>();
            var faultIncidents = 0;
            foreach (var st in statuses)
            {
                int connectorId; string? status;
                try
                {
                    using var doc = JsonDocument.Parse(st.Payload ?? "[]");
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 4) continue;
                    var body = root[3];
                    connectorId = body.TryGetProperty("connectorId", out var c) && c.TryGetInt32(out var cv) ? cv : 0;
                    status = body.TryGetProperty("status", out var sv) ? sv.GetString() : null;
                }
                catch (JsonException) { continue; }

                var at = Idx(st.CreatedAt);
                if (status == OcppConnectorStatus.Faulted)
                {
                    if (!faultSince.ContainsKey(connectorId)) { faultSince[connectorId] = at; faultIncidents++; }
                }
                else if (faultSince.Remove(connectorId, out var from))
                {
                    for (var i = from; i < at; i++) faultedMin[i] = true;
                }
            }
            foreach (var from in faultSince.Values)           // still faulted now
                for (var i = from; i < minutes; i++) faultedMin[i] = true;

            // ---- the numbers
            int counted = 0, onlineCount = 0, faultFree = 0, good = 0;
            for (var i = 0; i < minutes; i++)
            {
                if (excludedMin[i]) continue;
                counted++;
                if (onlineMin[i]) onlineCount++;
                if (!faultedMin[i]) faultFree++;
                if (onlineMin[i] && !faultedMin[i]) good++;
            }
            if (counted == 0) continue;

            cp.ReliabilityPct = Math.Round(100m * good / counted, 2);
            cp.ReliabilityOnlinePct = Math.Round(100m * onlineCount / counted, 2);
            cp.ReliabilityFaultFreePct = Math.Round(100m * faultFree / counted, 2);
            cp.ReliabilityOfflineIncidents = offlineIncidents;
            cp.ReliabilityFaultIncidents = faultIncidents;
            cp.ReliabilityComputedAt = now;
            computed++;
        }

        await applicationDbContext.SaveChanges(cancellationToken);
        logger.LogInformation("ComputeOcppReliability: {Count} charger(s) scored over {Days} days", computed, OcppLimits.ReliabilityWindowDays);
        return computed;
    }

    /// <summary>Who, what, since when — one shape for all three alert rules.</summary>
    private sealed record AlertTarget(int ChargePointRowId, string ChargePointId, string? DisplayName, int ChargingPointId, string? StationName, int? OwnerId,
        int? ConnectorId, int? TransactionId, DateTime Since, string? Detail)
    {
        public string Name => DisplayName ?? ChargePointId;
        public string Where => ConnectorId is int c and > 0 ? $" – المقبس {c}" : "";
        public string StationLabel
        {
            get
            {
                var n = (StationName ?? "").Trim();
                return n.StartsWith("محطة", StringComparison.Ordinal) ? n : $"محطة {n}";
            }
        }
    }

    private async Task<AlertTarget?> LoadTargetAsync(int ocppChargePointId, CancellationToken ct, int? connectorId = null) =>
        await applicationDbContext.OcppChargePoints.AsNoTracking()
            .Where(c => c.Id == ocppChargePointId)
            .Select(c => new AlertTarget(c.Id, c.ChargePointId, c.DisplayName, c.ChargingPointId, c.ChargingPoint.Name, c.ChargingPoint.OwnerId,
                connectorId, null, DateTime.UtcNow, null))
            .FirstOrDefaultAsync(ct);

    /// <summary>Push + inbox to the station owner, its active managers and every active admin. Returns how many people.</summary>
    /// <summary>
    /// Push + inbox. Default audience: the station owner, its active managers and every active
    /// admin (station app). <paramref name="onlyUsers"/> replaces that audience (e.g. the driver,
    /// through the user app); <paramref name="includeAdmins"/> = false keeps operational
    /// nudges off the admins' phones.
    /// </summary>
    private async Task<int> NotifyOcppAlertAsync(AlertTarget t, string alertType, string title, string body, CancellationToken ct,
        bool resolved = false, bool includeAdmins = true, IReadOnlyCollection<int>? onlyUsers = null, FirebaseAppType? appType = null)
    {
        List<int> recipients;
        if (onlyUsers is not null)
        {
            recipients = onlyUsers.Distinct().ToList();
        }
        else
        {
            recipients = await applicationDbContext.ProviderManagers.AsNoTracking()
                .Where(m => m.ProviderType == "ChargingPoint" && m.ProviderId == t.ChargingPointId && m.IsActive && !m.IsDeleted)
                .Select(m => m.UserId)
                .ToListAsync(ct);
            if (includeAdmins)
                recipients.AddRange(await applicationDbContext.UserAccounts.AsNoTracking()
                    .Where(u => u.RoleId == 2 && u.IsActive && !u.IsDeleted)
                    .Select(u => u.Id)
                    .ToListAsync(ct));
            if (t.OwnerId is int ownerId) recipients.Add(ownerId);
            recipients = recipients.Distinct().ToList();
        }
        if (recipients.Count == 0) return 0;

        var data = JsonSerializer.Serialize(new
        {
            type = "ocpp_alert",
            alertType,
            resolved,
            chargingPointId = t.ChargingPointId,
            chargePointId = t.ChargePointId,
            connectorId = t.ConnectorId,
        });

        var tokens = await applicationDbContext.NotificationTokens.AsNoTracking()
            .Where(x => recipients.Contains(x.UserId) && (appType == null || x.AppType == appType))
            .Select(x => new { x.AppType, x.Token })
            .ToListAsync(ct);
        foreach (var group in tokens.GroupBy(x => x.AppType))
        {
            try { await notificationService.SendMessagesAsync(group.Select(x => x.Token).ToList(), title, body, group.Key); }
            catch (Exception ex) { logger.LogWarning(ex, "OcppAlert: FCM send failed for app type {AppType}", group.Key); }
        }

        var typeId = await applicationDbContext.NotificationTypes.AsNoTracking()
            .Where(x => x.Name == "charging_point_status_changed")
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(ct) ?? 1;
        await NotificationInboxHelper.CreateNotificationInboxRecordsAsync(applicationDbContext, recipients, typeId, title, body, null, data, ct);
        return recipients.Count;
    }
}
