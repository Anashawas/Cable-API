using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Commands.CancelOfferTransaction;

public record CancelOfferTransactionCommand(int Id) : IRequest;

public class CancelOfferTransactionCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<CancelOfferTransactionCommand>
{
    public async Task Handle(CancelOfferTransactionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var transaction = await applicationDbContext.OfferTransactions
                              .FirstOrDefaultAsync(x => x.Id == request.Id
                                                        && x.ConfirmedByUserId == userId
                                                        && !x.IsDeleted
                                                        && x.Status == (int)OfferTransactionStatus.Initiated,
                                  cancellationToken)
                          ?? throw new NotFoundException($"Initiated transaction with id {request.Id} not found");

        transaction.Status = (int)OfferTransactionStatus.Cancelled;

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
