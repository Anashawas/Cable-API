using Application.Common.Models;
using Application.Common.Security;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Queries.GetAllPointsTransactions;

/// <summary>
/// Admin points-ledger row: PointsHistoryDto enriched with the account owner
/// (userId + userName) and, for admin-initiated rows, the admin actor.
/// </summary>
public record AdminPointsHistoryDto(
    int Id,
    int UserId,
    string? UserName,
    int TransactionType,
    int Points,
    int BalanceAfter,
    string? ReferenceType,
    int? ReferenceId,
    string? Note,
    string? ActionName,
    string? ProviderName,
    int? PerformedByUserId,
    string? PerformedByUserName,
    DateTime CreatedAt
);

/// <summary>
/// Admin global points ledger across ALL users (C1), also powering the per-user
/// admin history (A2) when <paramref name="UserId"/> is set. Not owner-gated.
/// </summary>
public record GetAllPointsTransactionsRequest(
    int? TransactionType = null,
    int? UserId = null,
    int? SeasonId = null,
    string? ProviderType = null,
    int? ProviderId = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 20
) : IRequest<PagedResult<AdminPointsHistoryDto>>;

public class GetAllPointsTransactionsRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetAllPointsTransactionsRequest, PagedResult<AdminPointsHistoryDto>>
{
    public async Task<PagedResult<AdminPointsHistoryDto>> Handle(GetAllPointsTransactionsRequest request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var query = applicationDbContext.LoyaltyPointTransactions
            .AsNoTracking()
            .Where(t => !t.IsDeleted);

        if (request.UserId.HasValue)
            query = query.Where(t => t.Account.UserId == request.UserId.Value);

        if (request.TransactionType.HasValue)
            query = query.Where(t => t.TransactionType == request.TransactionType.Value);

        if (request.SeasonId.HasValue)
            query = query.Where(t => t.LoyaltySeasonId == request.SeasonId.Value);

        if (!string.IsNullOrEmpty(request.ProviderType))
            query = query.Where(t => t.ReferenceType == request.ProviderType);

        if (request.ProviderId.HasValue)
            query = query.Where(t => t.ReferenceId == request.ProviderId.Value);

        if (request.From.HasValue)
            query = query.Where(t => t.CreatedAt >= request.From.Value);

        if (request.To.HasValue)
            query = query.Where(t => t.CreatedAt <= request.To.Value);

        var paged = await query
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                t.Id,
                OwnerUserId = t.Account.UserId,
                OwnerUserName = t.Account.User.Name,
                t.TransactionType,
                t.Points,
                t.BalanceAfter,
                t.ReferenceType,
                t.ReferenceId,
                t.Note,
                ActionName = t.Action != null ? t.Action.Name : null,
                t.CreatedBy,
                t.CreatedAt
            })
            .ToPaginatedAsync(request.Page, request.PageSize, maxPageSize: 200, cancellationToken: cancellationToken);

        var transactions = paged.Items;

        // Batch resolve provider names (same pattern as GetMyPointsHistory).
        var cpIds = transactions
            .Where(t => t.ReferenceType == "ChargingPoint" && t.ReferenceId.HasValue)
            .Select(t => t.ReferenceId!.Value).Distinct().ToList();
        var spIds = transactions
            .Where(t => t.ReferenceType == "ServiceProvider" && t.ReferenceId.HasValue)
            .Select(t => t.ReferenceId!.Value).Distinct().ToList();

        var cpNames = cpIds.Count > 0
            ? await applicationDbContext.ChargingPoints
                .Where(x => cpIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Name })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken)
            : new Dictionary<int, string>();
        var spNames = spIds.Count > 0
            ? await applicationDbContext.ServiceProviders
                .Where(x => spIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Name })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken)
            : new Dictionary<int, string>();

        // Batch resolve the acting admin for rows created by someone other than
        // the account owner (adjustments, season bonuses, cancellations, ...).
        var actorIds = transactions
            .Where(t => t.CreatedBy.HasValue && t.CreatedBy.Value != t.OwnerUserId)
            .Select(t => t.CreatedBy!.Value).Distinct().ToList();

        var actorNames = actorIds.Count > 0
            ? await applicationDbContext.UserAccounts
                .Where(x => actorIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Name })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken)
            : new Dictionary<int, string>();

        var items = transactions.Select(t =>
        {
            string? providerName = null;
            if (t.ReferenceId.HasValue)
            {
                if (t.ReferenceType == "ChargingPoint")
                    cpNames.TryGetValue(t.ReferenceId.Value, out providerName);
                else if (t.ReferenceType == "ServiceProvider")
                    spNames.TryGetValue(t.ReferenceId.Value, out providerName);
            }

            int? performedById = null;
            string? performedByName = null;
            if (t.CreatedBy.HasValue && t.CreatedBy.Value != t.OwnerUserId)
            {
                performedById = t.CreatedBy.Value;
                actorNames.TryGetValue(t.CreatedBy.Value, out performedByName);
            }

            return new AdminPointsHistoryDto(
                t.Id,
                t.OwnerUserId,
                t.OwnerUserName,
                t.TransactionType,
                t.Points,
                t.BalanceAfter,
                t.ReferenceType,
                t.ReferenceId,
                t.Note,
                t.ActionName,
                providerName,
                performedById,
                performedByName,
                t.CreatedAt);
        }).ToList();

        return paged.As(items);
    }
}
