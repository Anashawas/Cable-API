using Application.Offers.Queries.GetMyOfferTransactions;
using Cable.Core;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Queries.GetProviderTransactions;

public record GetProviderTransactionsRequest(
    string ProviderType,
    int ProviderId,
    int? Month = null,
    int? Year = null
) : IRequest<List<OfferTransactionDto>>;

public class GetProviderTransactionsRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetProviderTransactionsRequest, List<OfferTransactionDto>>
{
    public async Task<List<OfferTransactionDto>> Handle(GetProviderTransactionsRequest request,
        CancellationToken cancellationToken)
    {
        _ = currentUserService.UserId
            ?? throw new NotAuthorizedAccessException("User not authenticated");

        var query = applicationDbContext.OfferTransactions
            .AsNoTracking()
            .Include(x => x.Offer)
            .Include(x => x.User)
            .Where(x => x.ProviderType == request.ProviderType
                         && x.ProviderId == request.ProviderId
                         && !x.IsDeleted);

        if (request.Year.HasValue && request.Month.HasValue)
        {
            var startDate = new DateTime(request.Year.Value, request.Month.Value, 1);
            var endDate = startDate.AddMonths(1);
            query = query.Where(x => x.CreatedAt >= startDate && x.CreatedAt < endDate);
        }

        var transactions = await query
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return transactions.Select(x => new OfferTransactionDto(
            x.Id, x.ProviderOfferId, x.Offer?.Title,
            x.UserId, x.User?.Name, x.OfferCode, x.Status,
            x.PointsDeducted, x.MonetaryValue, x.CurrencyCode,
            x.ProviderType, x.ProviderId,
            x.ConfirmedByUserId, x.CodeExpiresAt, x.CompletedAt,
            x.CreatedAt
        )).ToList();
    }
}
