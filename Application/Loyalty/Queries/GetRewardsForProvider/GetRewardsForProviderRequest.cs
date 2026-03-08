using Application.Loyalty.Queries.GetAvailableRewards;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Queries.GetRewardsForProvider;

public record GetRewardsForProviderRequest(
    string ProviderType,
    int ProviderId
) : IRequest<List<RewardDto>>;

public class GetRewardsForProviderRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetRewardsForProviderRequest, List<RewardDto>>
{
    public async Task<List<RewardDto>> Handle(GetRewardsForProviderRequest request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        return await applicationDbContext.LoyaltyRewards
            .Where(r => r.IsActive && !r.IsDeleted
                         && r.ValidFrom <= now
                         && (r.ValidTo == null || r.ValidTo >= now)
                         && (r.MaxRedemptions == null || r.CurrentRedemptions < r.MaxRedemptions)
                         && r.ProviderType == request.ProviderType
                         && (r.ProviderId == request.ProviderId || r.ProviderId == null))
            .OrderBy(r => r.PointsCost)
            .Select(r => new RewardDto(
                r.Id, r.Name, r.Description, r.PointsCost, r.RewardType,
                r.RewardValue, r.ProviderType, r.ProviderId, r.ServiceCategoryId,
                r.MaxRedemptions, r.CurrentRedemptions, r.ImageUrl,
                r.ValidFrom, r.ValidTo))
            .ToListAsync(cancellationToken);
    }
}
