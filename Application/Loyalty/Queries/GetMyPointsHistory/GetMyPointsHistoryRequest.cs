using Cable.Core;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Queries.GetMyPointsHistory;

public record PointsHistoryDto(
    int Id,
    int TransactionType,
    int Points,
    int BalanceAfter,
    string? ReferenceType,
    int? ReferenceId,
    string? Note,
    string? ActionName,
    string? ProviderName,
    DateTime CreatedAt
);

public record GetMyPointsHistoryRequest(
    int? SeasonId,
    int? TransactionType,
    int Page = 1,
    int PageSize = 20
) : IRequest<List<PointsHistoryDto>>;

public class GetMyPointsHistoryRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetMyPointsHistoryRequest, List<PointsHistoryDto>>
{
    public async Task<List<PointsHistoryDto>> Handle(GetMyPointsHistoryRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var query = applicationDbContext.LoyaltyPointTransactions
            .Include(t => t.Action)
            .Where(t => t.Account.UserId == userId && !t.IsDeleted);

        if (request.SeasonId.HasValue)
            query = query.Where(t => t.LoyaltySeasonId == request.SeasonId.Value);

        if (request.TransactionType.HasValue)
            query = query.Where(t => t.TransactionType == request.TransactionType.Value);

        var transactions = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(t => new
            {
                t.Id,
                t.TransactionType,
                t.Points,
                t.BalanceAfter,
                t.ReferenceType,
                t.ReferenceId,
                t.Note,
                ActionName = t.Action != null ? t.Action.Name : null,
                t.CreatedAt
            })
            .ToListAsync(cancellationToken);

        // Batch resolve provider names to avoid N+1 queries
        var cpIds = transactions
            .Where(t => t.ReferenceType == "ChargingPoint" && t.ReferenceId.HasValue)
            .Select(t => t.ReferenceId!.Value)
            .Distinct()
            .ToList();

        var spIds = transactions
            .Where(t => t.ReferenceType == "ServiceProvider" && t.ReferenceId.HasValue)
            .Select(t => t.ReferenceId!.Value)
            .Distinct()
            .ToList();

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

        return transactions.Select(t =>
        {
            string? providerName = null;
            if (t.ReferenceId.HasValue)
            {
                if (t.ReferenceType == "ChargingPoint")
                    cpNames.TryGetValue(t.ReferenceId.Value, out providerName);
                else if (t.ReferenceType == "ServiceProvider")
                    spNames.TryGetValue(t.ReferenceId.Value, out providerName);
            }

            return new PointsHistoryDto(
                t.Id,
                t.TransactionType,
                t.Points,
                t.BalanceAfter,
                t.ReferenceType,
                t.ReferenceId,
                t.Note,
                t.ActionName,
                providerName,
                t.CreatedAt);
        }).ToList();
    }
}
