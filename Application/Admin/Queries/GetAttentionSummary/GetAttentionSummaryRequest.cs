using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Admin.Queries.GetAttentionSummary;

/// <summary>
/// One number per queue that needs a human — the admin dashboard's
/// "Needs Attention" panel loads this instead of firing 8+ list calls.
/// Each count maps to an existing admin route for the click-through.
/// </summary>
public record AttentionSummaryDto(
    int StationUpdateRequests,
    int OpenComplaints,
    int PendingViewImages,
    int PendingOffers,
    int SettlementsPending,
    int SettlementsDisputed,
    int PremiumExpiringSoon,
    int PremiumExpired,
    int CampaignsEndingSoon,
    int AnnouncementsExpiringSoon,
    int PendingWorkerNotifications);

public record GetAttentionSummaryRequest : IRequest<AttentionSummaryDto>;

public class GetAttentionSummaryRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetAttentionSummaryRequest, AttentionSummaryDto>
{
    /// <summary>"Expiring soon" horizon (§11.3) — hardcoded 7 days for now.</summary>
    private const int ExpiringSoonDays = 7;

    public async Task<AttentionSummaryDto> Handle(GetAttentionSummaryRequest request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var now = DateTime.UtcNow;
        var soon = now.AddDays(ExpiringSoonDays);

        var stationUpdateRequests = await applicationDbContext.ChargingPointUpdateRequests
            .CountAsync(x => !x.IsDeleted && x.RequestStatus == RequestStatus.Pending, cancellationToken);

        // "Open" = anything not closed as NotComplaint (1) or Solved (2).
        var openComplaints = await applicationDbContext.UserComplaints
            .CountAsync(x => !x.IsDeleted && x.Status != 1 && x.Status != 2, cancellationToken);

        var pendingViewImages = await applicationDbContext.ChargingPoints
            .CountAsync(x => !x.IsDeleted && x.ViewImageStatus == "pending", cancellationToken);

        var pendingOffers = await applicationDbContext.ProviderOffers
            .CountAsync(x => !x.IsDeleted && x.ApprovalStatus == 1, cancellationToken);

        var settlementsPending = await applicationDbContext.ProviderSettlements
            .CountAsync(x => !x.IsDeleted && x.SettlementStatus == 1, cancellationToken);
        var settlementsDisputed = await applicationDbContext.ProviderSettlements
            .CountAsync(x => !x.IsDeleted && x.SettlementStatus == 4, cancellationToken);

        // Warn-only premium (§4.3): admin must see who to renew or demote.
        var premiumExpiringSoon = await applicationDbContext.ChargingPoints
            .CountAsync(x => !x.IsDeleted && x.PremiumPaymentDate != null
                             && x.PremiumExpiresAt != null
                             && x.PremiumExpiresAt >= now && x.PremiumExpiresAt <= soon, cancellationToken);
        var premiumExpired = await applicationDbContext.ChargingPoints
            .CountAsync(x => !x.IsDeleted && x.PremiumPaymentDate != null
                             && x.PremiumExpiresAt != null && x.PremiumExpiresAt < now, cancellationToken);

        var campaignsEndingSoon = await applicationDbContext.Campaigns
            .CountAsync(x => !x.IsDeleted && x.Status == 1
                             && x.EndDate != null && x.EndDate >= now && x.EndDate <= soon, cancellationToken);

        var announcementsExpiringSoon = await applicationDbContext.Announcements
            .CountAsync(x => !x.IsDeleted && x.IsActive
                             && x.EndDate != null && x.EndDate >= now && x.EndDate <= soon, cancellationToken);

        // Part B F3: worker fan announcements waiting for an owner/admin decision.
        var pendingWorkerNotifications = await applicationDbContext.ProviderFavoriteNotifications
            .CountAsync(x => !x.IsDeleted && x.Status == "pending", cancellationToken);

        return new AttentionSummaryDto(
            stationUpdateRequests,
            openComplaints,
            pendingViewImages,
            pendingOffers,
            settlementsPending,
            settlementsDisputed,
            premiumExpiringSoon,
            premiumExpired,
            campaignsEndingSoon,
            announcementsExpiringSoon,
            pendingWorkerNotifications);
    }
}
