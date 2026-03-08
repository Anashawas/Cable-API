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

public record GetMyPartnerTransactionsRequest(int? Status) : IRequest<List<PartnerTransactionDto>>;

public class GetMyPartnerTransactionsRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetMyPartnerTransactionsRequest, List<PartnerTransactionDto>>
{
    public async Task<List<PartnerTransactionDto>> Handle(GetMyPartnerTransactionsRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var query = applicationDbContext.PartnerTransactions
            .Where(x => x.UserId == userId && !x.IsDeleted);

        if (request.Status.HasValue)
            query = query.Where(x => x.Status == request.Status.Value);

        var transactions = await query
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var result = new List<PartnerTransactionDto>();
        foreach (var t in transactions)
        {
            string? providerName = null;
            if (t.ProviderType == "ChargingPoint")
            {
                providerName = await applicationDbContext.ChargingPoints
                    .Where(x => x.Id == t.ProviderId)
                    .Select(x => x.Name)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            else if (t.ProviderType == "ServiceProvider")
            {
                providerName = await applicationDbContext.ServiceProviders
                    .Where(x => x.Id == t.ProviderId)
                    .Select(x => x.Name)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            result.Add(new PartnerTransactionDto(
                t.Id, t.PartnerAgreementId, providerName, t.TransactionCode,
                t.Status, t.ProviderType, t.ProviderId,
                t.TransactionAmount, t.CurrencyCode, t.CommissionPercentage,
                t.CommissionAmount, t.PointsAwarded,
                t.CodeExpiresAt, t.CompletedAt, t.CreatedAt));
        }

        return result;
    }
}
