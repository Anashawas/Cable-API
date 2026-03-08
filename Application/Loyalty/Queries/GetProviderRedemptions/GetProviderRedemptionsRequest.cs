using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Queries.GetProviderRedemptions;

public record ProviderRedemptionDto(
    int Id,
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
    int? ProviderId
) : IRequest<List<ProviderRedemptionDto>>;

public class GetProviderRedemptionsRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetProviderRedemptionsRequest, List<ProviderRedemptionDto>>
{
    public async Task<List<ProviderRedemptionDto>> Handle(GetProviderRedemptionsRequest request, CancellationToken cancellationToken)
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
                r.Id, r.User.Name, r.Reward.Name, r.PointsSpent,
                r.Status, r.RedemptionCode, r.RedeemedAt, r.FulfilledAt))
            .ToListAsync(cancellationToken);
    }
}
