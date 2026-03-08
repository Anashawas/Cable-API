using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Queries.GetAvailableRewards;

public record RewardDto(
    int Id,
    string Name,
    string? Description,
    int PointsCost,
    int RewardType,
    string? RewardValue,
    string? ProviderType,
    int? ProviderId,
    int? ServiceCategoryId,
    int? MaxRedemptions,
    int CurrentRedemptions,
    string? ImageUrl,
    DateTime ValidFrom,
    DateTime? ValidTo
);

public record GetAvailableRewardsRequest(
    string? ProviderType,
    int? ProviderId,
    int? CategoryId
) : IRequest<List<RewardDto>>;

public class GetAvailableRewardsRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetAvailableRewardsRequest, List<RewardDto>>
{
    public async Task<List<RewardDto>> Handle(GetAvailableRewardsRequest request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var query = applicationDbContext.LoyaltyRewards
            .Where(r => r.IsActive && !r.IsDeleted
                         && r.ValidFrom <= now
                         && (r.ValidTo == null || r.ValidTo >= now)
                         && (r.MaxRedemptions == null || r.CurrentRedemptions < r.MaxRedemptions));

        if (!string.IsNullOrEmpty(request.ProviderType))
            query = query.Where(r => r.ProviderType == request.ProviderType);

        if (request.ProviderId.HasValue)
            query = query.Where(r => r.ProviderId == request.ProviderId.Value);

        if (request.CategoryId.HasValue)
            query = query.Where(r => r.ServiceCategoryId == request.CategoryId.Value);

        return await query
            .OrderBy(r => r.PointsCost)
            .Select(r => new RewardDto(
                r.Id, r.Name, r.Description, r.PointsCost, r.RewardType,
                r.RewardValue, r.ProviderType, r.ProviderId, r.ServiceCategoryId,
                r.MaxRedemptions, r.CurrentRedemptions, r.ImageUrl,
                r.ValidFrom, r.ValidTo))
            .ToListAsync(cancellationToken);
    }
}
