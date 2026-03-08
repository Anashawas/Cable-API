using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Commands.ApproveOffer;

public record ApproveOfferCommand(int Id) : IRequest;

public class ApproveOfferCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<ApproveOfferCommand>
{
    public async Task Handle(ApproveOfferCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var offer = await applicationDbContext.ProviderOffers
                        .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                    ?? throw new NotFoundException($"Offer with id {request.Id} not found");

        offer.ApprovalStatus = (int)OfferApprovalStatus.Approved;
        offer.ApprovedByUserId = userId;
        offer.ApprovedAt = DateTime.UtcNow;
        offer.IsActive = true;

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
