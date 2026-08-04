using Application.Common.Models;
using Cable.Core;
using Microsoft.EntityFrameworkCore;

namespace Application.Partners.Queries.GetMyPartnerTransactions;

public record PartnerTransactionDto(
    int Id,
    int PartnerAgreementId,
    string? ProviderName,
    string TransactionCode,
    int Status,
    string ProviderType,
    int ProviderId,
    decimal? TransactionAmount,
    string? CurrencyCode,
    double CommissionPercentage,
    decimal? CommissionAmount,
    int? PointsAwarded,
    DateTime CodeExpiresAt,
    DateTime? CompletedAt,
    DateTime? CreatedAt
);

public record GetMyPartnerTransactionsRequest(int? Status, int? Page = null, int? PageSize = null) : IRequest<PagedResult<PartnerTransactionDto>>;

public class GetMyPartnerTransactionsRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetMyPartnerTransactionsRequest, PagedResult<PartnerTransactionDto>>
{
    public async Task<PagedResult<PartnerTransactionDto>> Handle(GetMyPartnerTransactionsRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var query = applicationDbContext.PartnerTransactions
            .Where(x => x.UserId == userId && !x.IsDeleted);

        if (request.Status.HasValue)
            query = query.Where(x => x.Status == request.Status.Value);

        var paged = await query
            .OrderByDescending(x => x.CreatedAt)
            .ToOptionallyPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);
        var transactions = paged.Items;

        // Batch load provider names (avoids N+1 queries)
        var cpIds = transactions.Where(t => t.ProviderType == "ChargingPoint").Select(t => t.ProviderId).Distinct().ToList();
        var spIds = transactions.Where(t => t.ProviderType == "ServiceProvider").Select(t => t.ProviderId).Distinct().ToList();

        var cpNames = cpIds.Count > 0
            ? await applicationDbContext.ChargingPoints.Where(x => cpIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken)
            : new Dictionary<int, string>();
        var spNames = spIds.Count > 0
            ? await applicationDbContext.ServiceProviders.Where(x => spIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken)
            : new Dictionary<int, string>();

        return paged.As(transactions.Select(t => new PartnerTransactionDto(
            t.Id, t.PartnerAgreementId,
            t.ProviderType == "ChargingPoint" ? cpNames.GetValueOrDefault(t.ProviderId) : spNames.GetValueOrDefault(t.ProviderId),
            t.TransactionCode, t.Status, t.ProviderType, t.ProviderId,
            t.TransactionAmount, t.CurrencyCode, t.CommissionPercentage,
            t.CommissionAmount, t.PointsAwarded,
            t.CodeExpiresAt, t.CompletedAt, t.CreatedAt)).ToList());
    }
}
