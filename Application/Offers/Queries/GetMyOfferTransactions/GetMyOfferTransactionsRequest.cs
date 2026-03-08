using Cable.Core;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Queries.GetMyOfferTransactions;

public record GetMyOfferTransactionsRequest(int? Status = null) : IRequest<List<OfferTransactionDto>>;

public class GetMyOfferTransactionsRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetMyOfferTransactionsRequest, List<OfferTransactionDto>>
{
    public async Task<List<OfferTransactionDto>> Handle(GetMyOfferTransactionsRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var query = applicationDbContext.OfferTransactions
            .AsNoTracking()
            .Include(x => x.Offer)
            .Include(x => x.User)
            .Where(x => x.UserId == userId && !x.IsDeleted);

        if (request.Status.HasValue)
            query = query.Where(x => x.Status == request.Status.Value);

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
