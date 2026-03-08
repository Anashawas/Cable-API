using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Commands.DeactivateOffer;

public record DeactivateOfferCommand(int Id) : IRequest;

public class DeactivateOfferCommandHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<DeactivateOfferCommand>
{
    public async Task Handle(DeactivateOfferCommand request, CancellationToken cancellationToken)
    {
        var offer = await applicationDbContext.ProviderOffers
                        .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                    ?? throw new NotFoundException($"Offer with id {request.Id} not found");

        offer.IsActive = false;

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
