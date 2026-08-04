using Application.Common.Models;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Queries.GetProviderActivity;

/// <summary>
/// One row of the unified provider activity feed. ActivityType tags the source:
/// Offer (points → offer), Partner (spend → points), Redemption (reward pickup).
/// Points are signed: earned (+) / spent (-).
/// </summary>
public record ProviderActivityDto(
    string ActivityType,
    int TransactionId,
    int? UserId,
    string? UserName,
    string? Code,
    int Status,
    string StatusName,
    int Points,
    decimal? Amount,
    string? CurrencyCode,
    DateTime CreatedAt,
    DateTime? CompletedAt
);

/// <summary>
/// B1 — unified admin feed of ALL activity at one station/provider across the
/// three transaction flows. Admin role required.
/// </summary>
public record GetProviderActivityRequest(
    string ProviderType,
    int ProviderId,
    string? ActivityType = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 20
) : IRequest<PagedResult<ProviderActivityDto>>;

public class GetProviderActivityRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetProviderActivityRequest, PagedResult<ProviderActivityDto>>
{
    public async Task<PagedResult<ProviderActivityDto>> Handle(GetProviderActivityRequest request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        if (request.ProviderType is not ("ChargingPoint" or "ServiceProvider"))
            throw new DataValidationException("ProviderType",
                "ProviderType must be one of: ChargingPoint, ServiceProvider");

        if (request.ActivityType is not (null or "" or "Offer" or "Partner" or "Redemption"))
            throw new DataValidationException("ActivityType",
                "ActivityType must be one of: Offer, Partner, Redemption");

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        // Each source contributes at most the rows visible up to the requested page;
        // merging those windows is enough to slice the correct combined page.
        var window = page * pageSize;

        var includeOffers = request.ActivityType is null or "" or "Offer";
        var includePartners = request.ActivityType is null or "" or "Partner";
        var includeRedemptions = request.ActivityType is null or "" or "Redemption";

        var totalCount = 0;
        var merged = new List<ProviderActivityDto>();

        if (includeOffers)
        {
            var q = applicationDbContext.OfferTransactions
                .AsNoTracking()
                .Where(t => t.ProviderType == request.ProviderType
                            && t.ProviderId == request.ProviderId
                            && !t.IsDeleted);
            q = ApplyDates(q, request.From, request.To);

            totalCount += await q.CountAsync(cancellationToken);
            merged.AddRange((await q
                    .OrderByDescending(t => t.CreatedAt)
                    .Take(window)
                    .Select(t => new
                    {
                        t.Id, t.UserId, UserName = t.User != null ? t.User.Name : null,
                        t.OfferCode, t.Status, t.PointsDeducted, t.MonetaryValue,
                        t.CurrencyCode, t.CreatedAt, t.CompletedAt
                    })
                    .ToListAsync(cancellationToken))
                .Select(t => new ProviderActivityDto(
                    "Offer", t.Id, t.UserId, t.UserName, t.OfferCode,
                    t.Status, ((OfferTransactionStatus)t.Status).ToString(),
                    -t.PointsDeducted, t.MonetaryValue, t.CurrencyCode,
                    t.CreatedAt, t.CompletedAt)));
        }

        if (includePartners)
        {
            var q = applicationDbContext.PartnerTransactions
                .AsNoTracking()
                .Where(t => t.ProviderType == request.ProviderType
                            && t.ProviderId == request.ProviderId
                            && !t.IsDeleted);
            q = ApplyDates(q, request.From, request.To);

            totalCount += await q.CountAsync(cancellationToken);
            merged.AddRange((await q
                    .OrderByDescending(t => t.CreatedAt)
                    .Take(window)
                    .Select(t => new
                    {
                        t.Id, t.UserId, UserName = t.User != null ? t.User.Name : null,
                        t.TransactionCode, t.Status, t.PointsAwarded, t.TransactionAmount,
                        t.CurrencyCode, t.CreatedAt, t.CompletedAt
                    })
                    .ToListAsync(cancellationToken))
                .Select(t => new ProviderActivityDto(
                    "Partner", t.Id, t.UserId, t.UserName, t.TransactionCode,
                    t.Status, ((PartnerTransactionStatus)t.Status).ToString(),
                    t.PointsAwarded ?? 0, t.TransactionAmount, t.CurrencyCode,
                    t.CreatedAt, t.CompletedAt)));
        }

        if (includeRedemptions)
        {
            var q = applicationDbContext.UserRewardRedemptions
                .AsNoTracking()
                .Where(r => r.ProviderType == request.ProviderType
                            && r.ProviderId == request.ProviderId
                            && !r.IsDeleted);
            if (request.From.HasValue) q = q.Where(r => r.CreatedAt >= request.From.Value);
            if (request.To.HasValue) q = q.Where(r => r.CreatedAt <= request.To.Value);

            totalCount += await q.CountAsync(cancellationToken);
            merged.AddRange((await q
                    .OrderByDescending(r => r.CreatedAt)
                    .Take(window)
                    .Select(r => new
                    {
                        r.Id, r.UserId, UserName = (string?)r.User.Name,
                        r.RedemptionCode, r.Status, r.PointsSpent,
                        r.CreatedAt, r.FulfilledAt
                    })
                    .ToListAsync(cancellationToken))
                .Select(r => new ProviderActivityDto(
                    "Redemption", r.Id, r.UserId, r.UserName, r.RedemptionCode,
                    r.Status, ((RedemptionStatus)r.Status).ToString(),
                    -r.PointsSpent, null, null,
                    r.CreatedAt, r.FulfilledAt)));
        }

        var items = merged
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new PagedResult<ProviderActivityDto>(items, totalCount, page, pageSize);
    }

    private static IQueryable<T> ApplyDates<T>(IQueryable<T> query, DateTime? from, DateTime? to)
        where T : Domain.Common.BaseAuditableEntity
    {
        if (from.HasValue) query = query.Where(t => t.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(t => t.CreatedAt <= to.Value);
        return query;
    }
}
