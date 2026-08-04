using Application.Common.Models;
using Cable.Core;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Queries.GetMyRedemptions;

public record RedemptionDto(
    int Id,
    string RewardName,
    int PointsSpent,
    int Status,
    string? RedemptionCode,
    string? ProviderType,
    int? ProviderId,
    DateTime RedeemedAt,
    DateTime? FulfilledAt
);

public record GetMyRedemptionsRequest(int? Page = null, int? PageSize = null) : IRequest<PagedResult<RedemptionDto>>;

public class GetMyRedemptionsRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetMyRedemptionsRequest, PagedResult<RedemptionDto>>
{
    public async Task<PagedResult<RedemptionDto>> Handle(GetMyRedemptionsRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        return await applicationDbContext.UserRewardRedemptions
            .Include(r => r.Reward)
            .Where(r => r.UserId == userId && !r.IsDeleted)
            .OrderByDescending(r => r.RedeemedAt)
            .Select(r => new RedemptionDto(
                r.Id, r.Reward.Name, r.PointsSpent, r.Status,
                r.RedemptionCode, r.ProviderType, r.ProviderId,
                r.RedeemedAt, r.FulfilledAt))
            .ToOptionallyPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);
    }
}
