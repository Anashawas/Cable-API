using Cable.Core;
using Cable.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Promos.Queries.GetMyPromos;

/// <summary>
/// GET /api/promos/my?status=Available|Used|Expired — "My codes" by tab (default Available). Available = usable now;
/// Expired includes promos past their date the daily job has not marked yet. Used cards carry date, partner and saving.
/// </summary>
public record GetMyPromosRequest(UserPromoStatus? Status) : IRequest<List<MyPromoDto>>;

public class GetMyPromosRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetMyPromosRequest, List<MyPromoDto>>
{
    public async Task<List<MyPromoDto>> Handle(GetMyPromosRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var now = DateTime.UtcNow;
        var status = request.Status ?? UserPromoStatus.Available;

        var query = db.UserPromos.AsNoTracking().Include(u => u.Campaign)
            .Where(u => u.UserId == userId && !u.IsDeleted && !u.Campaign.IsDeleted);

        query = status switch
        {
            UserPromoStatus.Available => query.Where(u => u.Status == UserPromoStatus.Available
                                                          && (u.ExpiresAt == null || u.ExpiresAt > now)
                                                          && u.Campaign.IsActive),
            UserPromoStatus.Expired => query.Where(u => u.Status == UserPromoStatus.Expired
                                                        || (u.Status == UserPromoStatus.Available && u.ExpiresAt != null && u.ExpiresAt <= now)),
            _ => query.Where(u => u.Status == status)
        };

        var promos = await query.OrderByDescending(u => u.Id).Take(500).ToListAsync(cancellationToken);
        promos = status == UserPromoStatus.Available
            ? promos.OrderBy(u => u.ExpiresAt ?? DateTime.MaxValue).ThenByDescending(u => u.ClaimedAt).ToList()   // soonest to expire first
            : promos.OrderByDescending(u => u.UsedAt ?? u.ExpiresAt ?? u.ClaimedAt).ToList();

        Dictionary<int, PromoUsage> usages = new();
        Dictionary<(string, int), string> providerNames = new();
        if (status == UserPromoStatus.Used && promos.Count > 0)
        {
            var ids = promos.Select(p => p.Id).ToList();
            usages = await db.PromoUsages.AsNoTracking()
                .Where(u => ids.Contains(u.UserPromoId))
                .ToDictionaryAsync(u => u.UserPromoId, cancellationToken);

            var cpIds = usages.Values.Where(u => u.ProviderType == PromoRules.ChargingPoint).Select(u => u.ProviderId).Distinct().ToList();
            var spIds = usages.Values.Where(u => u.ProviderType == PromoRules.ServiceProvider).Select(u => u.ProviderId).Distinct().ToList();
            foreach (var cp in await db.ChargingPoints.AsNoTracking().Where(x => cpIds.Contains(x.Id)).Select(x => new { x.Id, x.Name }).ToListAsync(cancellationToken))
                providerNames[(PromoRules.ChargingPoint, cp.Id)] = cp.Name;
            foreach (var sp in await db.ServiceProviders.AsNoTracking().Where(x => spIds.Contains(x.Id)).Select(x => new { x.Id, x.Name }).ToListAsync(cancellationToken))
                providerNames[(PromoRules.ServiceProvider, sp.Id)] = sp.Name;
        }

        return promos.Select(p =>
        {
            usages.TryGetValue(p.Id, out var usage);
            var name = usage is null ? null : providerNames.GetValueOrDefault((usage.ProviderType, usage.ProviderId));
            return PromoViews.ToDto(p, p.Campaign, now, usage, name);
        }).ToList();
    }
}

public record MyPromosCountDto(int Available);

/// <summary>GET /api/promos/my/count — the dot on "My codes".</summary>
public record GetMyPromosCountRequest : IRequest<MyPromosCountDto>;

public class GetMyPromosCountRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetMyPromosCountRequest, MyPromosCountDto>
{
    public async Task<MyPromosCountDto> Handle(GetMyPromosCountRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var now = DateTime.UtcNow;
        return new MyPromosCountDto(await db.UserPromos.CountAsync(u => u.UserId == userId && !u.IsDeleted
                                                                       && u.Status == UserPromoStatus.Available
                                                                       && (u.ExpiresAt == null || u.ExpiresAt > now)
                                                                       && u.Campaign.IsActive && !u.Campaign.IsDeleted,
            cancellationToken));
    }
}
