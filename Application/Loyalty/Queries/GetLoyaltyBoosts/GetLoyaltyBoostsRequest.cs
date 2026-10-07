using Application.Common.Interfaces;
using Application.Common.Security;
using Application.Loyalty.Commands.ManageBoosts;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Queries.GetLoyaltyBoosts;

/// <param name="ActiveOnly">Only boosts flagged active.</param>
/// <param name="CurrentOnly">Only boosts whose date window contains now (UTC).</param>
public record GetLoyaltyBoostsRequest(bool ActiveOnly = false, bool CurrentOnly = false)
    : IRequest<List<LoyaltyBoostDto>>;

public class GetLoyaltyBoostsRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetLoyaltyBoostsRequest, List<LoyaltyBoostDto>>
{
    public async Task<List<LoyaltyBoostDto>> Handle(
        GetLoyaltyBoostsRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var now = DateTime.UtcNow;

        var query = applicationDbContext.LoyaltyBoosts
            .AsNoTracking()
            .Include(b => b.Providers.Where(p => !p.IsDeleted))
            .Where(b => !b.IsDeleted);

        if (request.ActiveOnly)
            query = query.Where(b => b.IsActive);

        if (request.CurrentOnly)
            query = query.Where(b => b.StartsAt <= now && b.EndsAt > now);

        var boosts = await query
            .OrderByDescending(b => b.StartsAt)
            .ToListAsync(cancellationToken);

        if (boosts.Count == 0)
            return [];

        // Spend to date, from the transactions themselves — the bonus is the
        // difference between what was awarded and the unboosted baseline, so no
        // separate counter can fall out of step with reality.
        var ids = boosts.Select(b => b.Id).ToList();

        var spend = await applicationDbContext.PartnerTransactions
            .AsNoTracking()
            .Where(t => t.AppliedBoostId != null
                        && ids.Contains(t.AppliedBoostId.Value)
                        && !t.IsDeleted
                        && t.Status == (int)PartnerTransactionStatus.Completed)
            .GroupBy(t => t.AppliedBoostId!.Value)
            .Select(g => new
            {
                BoostId = g.Key,
                Bonus = g.Sum(t => (t.PointsAwarded ?? 0) - (t.BasePoints ?? 0)),
                Count = g.Count()
            })
            .ToDictionaryAsync(x => x.BoostId, cancellationToken);

        return boosts.Select(b => new LoyaltyBoostDto(
            b.Id, b.Name, b.NameAr, b.Description, b.Multiplier,
            b.StartsAt, b.EndsAt,
            b.DailyStartMinute, b.DailyEndMinute, b.DaysOfWeekMask,
            b.AppliesToAllProviders, b.Priority,
            b.MaxBonusPointsPerUser, b.MaxTotalBonusPoints,
            b.IsActive, b.CreatedAt,
            b.Providers.Select(p => new BoostProviderDto(p.ProviderType, p.ProviderId)).ToList(),
            spend.TryGetValue(b.Id, out var s) ? s.Bonus : 0,
            spend.TryGetValue(b.Id, out var c) ? c.Count : 0
        )).ToList();
    }
}

public record GetLoyaltyBoostByIdRequest(int Id) : IRequest<LoyaltyBoostDto>;

public class GetLoyaltyBoostByIdRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    IMediator mediator)
    : IRequestHandler<GetLoyaltyBoostByIdRequest, LoyaltyBoostDto>
{
    public async Task<LoyaltyBoostDto> Handle(
        GetLoyaltyBoostByIdRequest request, CancellationToken cancellationToken)
    {
        var all = await mediator.Send(new GetLoyaltyBoostsRequest(), cancellationToken);

        return all.FirstOrDefault(b => b.Id == request.Id)
               ?? throw new NotFoundException($"Loyalty boost {request.Id} not found");
    }
}
