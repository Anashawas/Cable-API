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

public record GetMyRedemptionsRequest : IRequest<List<RedemptionDto>>;

public class GetMyRedemptionsRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetMyRedemptionsRequest, List<RedemptionDto>>
{
    public async Task<List<RedemptionDto>> Handle(GetMyRedemptionsRequest request, CancellationToken cancellationToken)
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
            .ToListAsync(cancellationToken);
    }
}
