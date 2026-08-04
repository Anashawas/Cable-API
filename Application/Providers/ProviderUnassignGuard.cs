using Application.Common.Interfaces;
using Cable.Core;
using Microsoft.EntityFrameworkCore;

namespace Application.Providers;

/// <summary>
/// Shared guard: a provider (charging point / service provider) cannot be
/// unassigned while it has live business attachments — active offers would
/// become unredeemable zombies and active partner agreements would have no
/// counterparty for commissions/settlements.
/// </summary>
public static class ProviderUnassignGuard
{
    public static async Task EnsureCanUnassignAsync(
        IApplicationDbContext db,
        string providerType,
        int providerId,
        CancellationToken cancellationToken)
    {
        var activeOffers = await db.ProviderOffers
            .CountAsync(o => o.ProviderType == providerType
                             && o.ProviderId == providerId
                             && o.IsActive && !o.IsDeleted, cancellationToken);

        var activeAgreements = await db.PartnerAgreements
            .CountAsync(a => a.ProviderType == providerType
                             && a.ProviderId == providerId
                             && a.IsActive && !a.IsDeleted, cancellationToken);

        if (activeOffers > 0 || activeAgreements > 0)
            throw new DataValidationException("NewOwnerId",
                $"Cannot unassign: this provider has {activeOffers} active offer(s) and " +
                $"{activeAgreements} active partner agreement(s). Deactivate them first.");
    }
}
