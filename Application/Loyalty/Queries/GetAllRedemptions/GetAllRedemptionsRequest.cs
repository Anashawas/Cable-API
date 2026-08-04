using Application.Common.Models;
using Application.Common.Security;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Queries.GetAllRedemptions;

/// <summary>RedemptionDto enriched with the redeeming user for the admin portal.</summary>
public record AdminRedemptionDto(
    int Id,
    int UserId,
    string? UserName,
    string RewardName,
    int PointsSpent,
    int Status,
    string? RedemptionCode,
    string? ProviderType,
    int? ProviderId,
    string? ProviderName,
    DateTime RedeemedAt,
    DateTime? FulfilledAt
);

/// <summary>
/// D1 — all reward redemptions across users and providers. Admin role required.
/// </summary>
public record GetAllRedemptionsRequest(
    int? Status = null,
    string? ProviderType = null,
    int? ProviderId = null,
    int? UserId = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 20
) : IRequest<PagedResult<AdminRedemptionDto>>;

public class GetAllRedemptionsRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetAllRedemptionsRequest, PagedResult<AdminRedemptionDto>>
{
    public async Task<PagedResult<AdminRedemptionDto>> Handle(GetAllRedemptionsRequest request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var query = applicationDbContext.UserRewardRedemptions
            .AsNoTracking()
            .Where(r => !r.IsDeleted);

        if (request.Status.HasValue)
            query = query.Where(r => r.Status == request.Status.Value);

        if (!string.IsNullOrEmpty(request.ProviderType))
            query = query.Where(r => r.ProviderType == request.ProviderType);

        if (request.ProviderId.HasValue)
            query = query.Where(r => r.ProviderId == request.ProviderId.Value);

        if (request.UserId.HasValue)
            query = query.Where(r => r.UserId == request.UserId.Value);

        if (request.From.HasValue)
            query = query.Where(r => r.RedeemedAt >= request.From.Value);

        if (request.To.HasValue)
            query = query.Where(r => r.RedeemedAt <= request.To.Value);

        var paged = await query
            .OrderByDescending(r => r.RedeemedAt)
            .Select(r => new
            {
                r.Id, r.UserId, UserName = (string?)r.User.Name,
                RewardName = r.Reward.Name, r.PointsSpent, r.Status,
                r.RedemptionCode, r.ProviderType, r.ProviderId,
                r.RedeemedAt, r.FulfilledAt
            })
            .ToPaginatedAsync(request.Page, request.PageSize, maxPageSize: 200, cancellationToken: cancellationToken);

        var rows = paged.Items;

        // Batch resolve provider names.
        var cpIds = rows.Where(r => r.ProviderType == "ChargingPoint" && r.ProviderId.HasValue)
            .Select(r => r.ProviderId!.Value).Distinct().ToList();
        var spIds = rows.Where(r => r.ProviderType == "ServiceProvider" && r.ProviderId.HasValue)
            .Select(r => r.ProviderId!.Value).Distinct().ToList();

        var cpNames = cpIds.Count > 0
            ? await applicationDbContext.ChargingPoints
                .Where(x => cpIds.Contains(x.Id)).Select(x => new { x.Id, x.Name })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken)
            : new Dictionary<int, string>();
        var spNames = spIds.Count > 0
            ? await applicationDbContext.ServiceProviders
                .Where(x => spIds.Contains(x.Id)).Select(x => new { x.Id, x.Name })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken)
            : new Dictionary<int, string>();

        var items = rows.Select(r =>
        {
            string? providerName = null;
            if (r.ProviderId.HasValue)
            {
                if (r.ProviderType == "ChargingPoint")
                    cpNames.TryGetValue(r.ProviderId.Value, out providerName);
                else if (r.ProviderType == "ServiceProvider")
                    spNames.TryGetValue(r.ProviderId.Value, out providerName);
            }

            return new AdminRedemptionDto(
                r.Id, r.UserId, r.UserName, r.RewardName, r.PointsSpent, r.Status,
                r.RedemptionCode, r.ProviderType, r.ProviderId, providerName,
                r.RedeemedAt, r.FulfilledAt);
        }).ToList();

        return paged.As(items);
    }
}
