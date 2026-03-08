using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Commands.UpdateOffer;

public record UpdateOfferCommand(
    int Id,
    string Title,
    string? TitleAr,
    string? Description,
    string? DescriptionAr,
    string ProviderType,
    int ProviderId,
    int PointsCost,
    decimal MonetaryValue,
    string CurrencyCode,
    int? MaxUsesPerUser,
    int? MaxTotalUses,
    int OfferCodeExpiryMinutes,
    string? ImageUrl,
    DateTime ValidFrom,
    DateTime? ValidTo,
    bool IsActive
) : IRequest;

public class UpdateOfferCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateOfferCommand>
{
    public async Task Handle(UpdateOfferCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var offer = await applicationDbContext.ProviderOffers
                        .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                    ?? throw new NotFoundException($"Offer with id {request.Id} not found");

        offer.Title = request.Title;
        offer.TitleAr = request.TitleAr;
        offer.Description = request.Description;
        offer.DescriptionAr = request.DescriptionAr;
        offer.ProviderType = request.ProviderType;
        offer.ProviderId = request.ProviderId;
        offer.PointsCost = request.PointsCost;
        offer.MonetaryValue = request.MonetaryValue;
        offer.CurrencyCode = request.CurrencyCode;
        offer.MaxUsesPerUser = request.MaxUsesPerUser;
        offer.MaxTotalUses = request.MaxTotalUses;
        offer.OfferCodeExpiryMinutes = request.OfferCodeExpiryMinutes;
        offer.ImageUrl = request.ImageUrl;
        offer.ValidFrom = request.ValidFrom;
        offer.ValidTo = request.ValidTo;
        offer.IsActive = request.IsActive;

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
