using Application.Common.Models;
using Cable.Core;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Queries.GetMyOfferTransactions;

public record GetMyOfferTransactionsRequest(int? Status = null, int? Page = null, int? PageSize = null) : IRequest<PagedResult<OfferTransactionDto>>;

public class GetMyOfferTransactionsRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetMyOfferTransactionsRequest, PagedResult<OfferTransactionDto>>
{
    public async Task<PagedResult<OfferTransactionDto>> Handle(GetMyOfferTransactionsRequest request,
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

        var paged = await query
            .OrderByDescending(x => x.CreatedAt)
            .ToOptionallyPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);

        return paged.As(paged.Items.Select(x => new OfferTransactionDto(
            x.Id, x.ProviderOfferId, x.Offer?.Title,
            x.UserId, x.User?.Name, x.OfferCode, x.Status,
            x.PointsDeducted, x.MonetaryValue, x.CurrencyCode,
            x.ProviderType, x.ProviderId,
            x.ConfirmedByUserId, x.CodeExpiresAt, x.CompletedAt,
            x.CreatedAt
        )).ToList());
    }
}
