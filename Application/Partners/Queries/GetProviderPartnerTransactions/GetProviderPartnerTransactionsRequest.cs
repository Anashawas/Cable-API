using Application.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace Application.Partners.Queries.GetProviderPartnerTransactions;

public record ProviderPartnerTransactionDto(
    int Id,
    int? UserId,
    string? UserName,
    string TransactionCode,
    int Status,
    decimal? TransactionAmount,
    string? CurrencyCode,
    decimal? CommissionAmount,
    int? PointsAwarded,
    DateTime CodeExpiresAt,
    DateTime? CompletedAt,
    DateTime? CreatedAt,
    /// <summary>
    /// Whether the provider has already rated this visit's driver. Lets the
    /// provider app show "Rate customer" only where it can still be used —
    /// the one-rating-per-transaction rule would otherwise surface as an error
    /// after the fact.
    /// </summary>
    bool IsRated,
    /// <summary>Sprint 5: the promo applied by the customer — discount, what to collect, what Cable pays. Null without one.</summary>
    Application.Promos.PartnerTransactionPromoDto? Promo = null
);

public record GetProviderPartnerTransactionsRequest(
    string ProviderType,
    int ProviderId,
    int? Month,
    int? Year,
    int? Page = null,
    int? PageSize = null
) : IRequest<PagedResult<ProviderPartnerTransactionDto>>;

public class GetProviderPartnerTransactionsRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetProviderPartnerTransactionsRequest, PagedResult<ProviderPartnerTransactionDto>>
{
    public async Task<PagedResult<ProviderPartnerTransactionDto>> Handle(
        GetProviderPartnerTransactionsRequest request, CancellationToken cancellationToken)
    {
        var query = applicationDbContext.PartnerTransactions
            .Include(x => x.User)
            .Where(x => x.ProviderType == request.ProviderType
                         && x.ProviderId == request.ProviderId
                         && !x.IsDeleted);

        if (request.Month.HasValue && request.Year.HasValue)
        {
            var startDate = new DateTime(request.Year.Value, request.Month.Value, 1);
            var endDate = startDate.AddMonths(1);
            query = query.Where(x => x.CreatedAt >= startDate && x.CreatedAt < endDate);
        }

        var paged = await query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new ProviderPartnerTransactionDto(
                x.Id, x.UserId, x.User != null ? x.User.Name : null, x.TransactionCode,
                x.Status, x.TransactionAmount, x.CurrencyCode,
                x.CommissionAmount, x.PointsAwarded,
                x.CodeExpiresAt, x.CompletedAt, x.CreatedAt,
                applicationDbContext.UserRates.Any(r => r.PartnerTransactionId == x.Id && !r.IsDeleted),
                null))
            .ToOptionallyPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);

        var promos = await Application.Promos.PartnerPromoView.ForTransactionsAsync(
            applicationDbContext, paged.Items.Select(x => x.Id).ToList(), cancellationToken);
        return promos.Count == 0
            ? paged
            : paged.As(paged.Items.Select(x => x with { Promo = promos.GetValueOrDefault(x.Id) }).ToList());
    }
}
