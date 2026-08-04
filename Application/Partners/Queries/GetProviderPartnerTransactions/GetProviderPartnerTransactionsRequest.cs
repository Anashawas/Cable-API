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
    DateTime? CreatedAt
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

        return await query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new ProviderPartnerTransactionDto(
                x.Id, x.UserId, x.User != null ? x.User.Name : null, x.TransactionCode,
                x.Status, x.TransactionAmount, x.CurrencyCode,
                x.CommissionAmount, x.PointsAwarded,
                x.CodeExpiresAt, x.CompletedAt, x.CreatedAt))
            .ToOptionallyPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);
    }
}
