using Cable.Core;
using Cable.Core.Emuns;

namespace Application.Offers.Commands.ProposeOffer;

public record ProposeOfferCommand(
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
    DateTime? ValidTo
) : IRequest<int>;

public class ProposeOfferCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<ProposeOfferCommand, int>
{
    public async Task<int> Handle(ProposeOfferCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var offer = new ProviderOffer
        {
            Title = request.Title,
            TitleAr = request.TitleAr,
            Description = request.Description,
            DescriptionAr = request.DescriptionAr,
            ProviderType = request.ProviderType,
            ProviderId = request.ProviderId,
            ProposedByUserId = userId,
            ApprovalStatus = (int)OfferApprovalStatus.Pending,
            PointsCost = request.PointsCost,
            MonetaryValue = request.MonetaryValue,
            CurrencyCode = request.CurrencyCode,
            MaxUsesPerUser = request.MaxUsesPerUser,
            MaxTotalUses = request.MaxTotalUses,
            CurrentTotalUses = 0,
            OfferCodeExpiryMinutes = request.OfferCodeExpiryMinutes > 0 ? request.OfferCodeExpiryMinutes : 30,
            ImageUrl = request.ImageUrl,
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo,
            IsActive = false // Not active until approved
        };

        applicationDbContext.ProviderOffers.Add(offer);
        await applicationDbContext.SaveChanges(cancellationToken);

        return offer.Id;
    }
}
