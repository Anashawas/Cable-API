using Application.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Queries.GetProviderRedemptions;

public record ProviderRedemptionDto(
    int Id,
    int UserId,
    string? UserName,
    string RewardName,
    int PointsSpent,
    int Status,
    string? RedemptionCode,
    DateTime RedeemedAt,
    DateTime? FulfilledAt
);

public record GetProviderRedemptionsRequest(
    string? ProviderType,
    int? ProviderId,
    int? Page = null,
    int? PageSize = null
) : IRequest<PagedResult<ProviderRedemptionDto>>;

public class GetProviderRedemptionsRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetProviderRedemptionsRequest, PagedResult<ProviderRedemptionDto>>
{
    public async Task<PagedResult<ProviderRedemptionDto>> Handle(GetProviderRedemptionsRequest request, CancellationToken cancellationToken)
    {
        var query = applicationDbContext.UserRewardRedemptions
            .Include(r => r.User)
            .Include(r => r.Reward)
            .Where(r => !r.IsDeleted);

        if (!string.IsNullOrEmpty(request.ProviderType))
            query = query.Where(r => r.ProviderType == request.ProviderType);

        if (request.ProviderId.HasValue)
            query = query.Where(r => r.ProviderId == request.ProviderId.Value);

        return await query
            .OrderByDescending(r => r.RedeemedAt)
            .Select(r => new ProviderRedemptionDto(
                r.Id, r.UserId, r.User.Name, r.Reward.Name, r.PointsSpent,
                r.Status, r.RedemptionCode, r.RedeemedAt, r.FulfilledAt))
            .ToOptionallyPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);
    }
}
