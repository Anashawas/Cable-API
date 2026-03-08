using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Commands.RejectOffer;

public record RejectOfferCommand(int Id, string? Note) : IRequest;

public class RejectOfferCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<RejectOfferCommand>
{
    public async Task Handle(RejectOfferCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var offer = await applicationDbContext.ProviderOffers
                        .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                    ?? throw new NotFoundException($"Offer with id {request.Id} not found");

        offer.ApprovalStatus = (int)OfferApprovalStatus.Rejected;
        offer.ApprovedByUserId = userId;
        offer.ApprovedAt = DateTime.UtcNow;
        offer.ApprovalNote = request.Note;
        offer.IsActive = false;

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
