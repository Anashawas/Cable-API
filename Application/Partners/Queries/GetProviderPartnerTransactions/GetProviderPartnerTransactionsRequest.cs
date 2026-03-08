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
    int? Year
) : IRequest<List<ProviderPartnerTransactionDto>>;

public class GetProviderPartnerTransactionsRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetProviderPartnerTransactionsRequest, List<ProviderPartnerTransactionDto>>
{
    public async Task<List<ProviderPartnerTransactionDto>> Handle(
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
                x.Id, x.UserId, x.User.Name, x.TransactionCode,
                x.Status, x.TransactionAmount, x.CurrencyCode,
                x.CommissionAmount, x.PointsAwarded,
                x.CodeExpiresAt, x.CompletedAt, x.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
