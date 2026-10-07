using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Utilities;

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
    int OfferCodeExpirySeconds,
    string? ImageUrl,
    DateTime ValidFrom,
    DateTime? ValidTo,
    decimal? PointsPriceValue = null
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
            PointsPriceValue = await OfferPointsValueResolver.ResolveAsync(
                applicationDbContext, request.PointsPriceValue, request.PointsCost, request.CurrencyCode, cancellationToken),
            MonetaryValue = request.MonetaryValue,
            CurrencyCode = request.CurrencyCode,
            MaxUsesPerUser = request.MaxUsesPerUser,
            MaxTotalUses = request.MaxTotalUses,
            CurrentTotalUses = 0,
            OfferCodeExpirySeconds = request.OfferCodeExpirySeconds > 0 ? request.OfferCodeExpirySeconds : 60,
            ImageUrl = request.ImageUrl,
            // Entered as Amman wall-clock; stored and compared as UTC.
            ValidFrom = JordanTime.ToUtc(request.ValidFrom),
            ValidTo = JordanTime.ToUtc(request.ValidTo),
            IsActive = false // Not active until approved
        };

        applicationDbContext.ProviderOffers.Add(offer);
        await applicationDbContext.SaveChanges(cancellationToken);

        return offer.Id;
    }
}
