using Application.Common.Security;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Queries.GetAllTiers;

public record TierDto(
    int Id,
    string Name,
    int MinPoints,
    double Multiplier,
    int BonusPoints,
    string? IconUrl,
    bool IsActive
);

/// <summary>
/// H1 (read-only) — the tier ladder with thresholds, multipliers and season
/// bonuses. Tiers are product-defined seed data today; CRUD can be added later
/// if they become business-editable. Admin role required.
/// </summary>
public record GetAllTiersRequest() : IRequest<List<TierDto>>;

public class GetAllTiersRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetAllTiersRequest, List<TierDto>>
{
    public async Task<List<TierDto>> Handle(GetAllTiersRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        return await applicationDbContext.LoyaltyTiers
            .AsNoTracking()
            .OrderBy(t => t.MinPoints)
            .Select(t => new TierDto(
                t.Id, t.Name, t.MinPoints, t.Multiplier, t.BonusPoints, t.IconUrl, t.IsActive))
            .ToListAsync(cancellationToken);
    }
}
