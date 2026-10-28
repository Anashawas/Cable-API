namespace Application.Common.Interfaces;

/// <summary>
/// Hangfire jobs of Cable gifts and promo codes (Sprint 5). Kept apart from <see cref="IBackgroundJobService"/>:
/// handlers enqueue them after their transaction commits, so a failed push never undoes an order or a promo.
/// </summary>
public interface IGiftsAndPromosJobs
{
    /// <summary>Push (device language) + inbox to the buyer when an admin fulfils or cancels the order. kind = Fulfilled | Cancelled.</summary>
    Task NotifyGiftOrderAsync(int giftOrderId, string kind, CancellationToken cancellationToken = default);

    /// <summary>"A new code from Cable" push + inbox to every user of one admin assignment.</summary>
    Task NotifyPromoAssignedAsync(Guid assignmentBatchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Daily: marks Available promos past their expiry as Expired, and sends the "expires within two days" push once
    /// per promo. Returns the pushes sent.
    /// </summary>
    Task<int> RunPromoExpiryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueued when a scan or preview found the campaign budget too small for one more use: switches the campaign
    /// off (DeactivatedReason = BudgetExhausted) if that is still true. Runs outside the refused request, whose
    /// transaction rolls back.
    /// </summary>
    Task DeactivateCampaignIfBudgetShortAsync(int promoCampaignId, decimal neededCableShareJod, CancellationToken cancellationToken = default);
}
