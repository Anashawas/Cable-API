using Application.Partners.Queries.GetProviderPartnerTransactions;
using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Partners.Queries.GetProviderPartnerTransactionById;

public record GetProviderPartnerTransactionByIdRequest(int Id) : IRequest<ProviderPartnerTransactionDto>;

public class GetProviderPartnerTransactionByIdRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetProviderPartnerTransactionByIdRequest, ProviderPartnerTransactionDto>
{
    public async Task<ProviderPartnerTransactionDto> Handle(
        GetProviderPartnerTransactionByIdRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var x = await applicationDbContext.PartnerTransactions
                    .Include(t => t.User)
                    .FirstOrDefaultAsync(t => t.Id == request.Id
                                              && t.ConfirmedByUserId == userId
                                              && !t.IsDeleted, cancellationToken)
                ?? throw new NotFoundException($"Partner transaction with Id '{request.Id}' not found");

        return new ProviderPartnerTransactionDto(
            x.Id, x.UserId, x.User?.Name, x.TransactionCode,
            x.Status, x.TransactionAmount, x.CurrencyCode,
            x.CommissionAmount, x.PointsAwarded,
            x.CodeExpiresAt, x.CompletedAt, x.CreatedAt);
    }
}
