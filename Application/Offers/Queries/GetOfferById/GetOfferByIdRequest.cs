using Application.Offers.Queries.GetActiveOffers;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Queries.GetOfferById;

public record GetOfferByIdRequest(int Id) : IRequest<OfferDto>;

public class GetOfferByIdRequestHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<GetOfferByIdRequest, OfferDto>
{
    public async Task<OfferDto> Handle(GetOfferByIdRequest request, CancellationToken cancellationToken)
    {
        var x = await applicationDbContext.ProviderOffers
                    .AsNoTracking()
                    .Include(o => o.ProposedByUser)
                    .FirstOrDefaultAsync(o => o.Id == request.Id && !o.IsDeleted, cancellationToken)
                ?? throw new NotFoundException($"Offer with id {request.Id} not found");

        return new OfferDto(
            x.Id, x.Title, x.TitleAr, x.Description, x.DescriptionAr,
            x.ProviderType, x.ProviderId, null,
            x.ProposedByUserId, x.ProposedByUser?.Name,
            x.ApprovalStatus, x.PointsCost, x.MonetaryValue, x.CurrencyCode,
            x.MaxUsesPerUser, x.MaxTotalUses, x.CurrentTotalUses,
            x.OfferCodeExpirySeconds,
            !string.IsNullOrEmpty(x.ImageUrl)
                ? uploadFileService.GetFilePath(UploadFileFolders.CableOfferAttachments, x.ImageUrl)
                : null,
            x.ValidFrom, x.ValidTo,
            x.IsActive, x.CreatedAt, x.PointsPriceValue
        );
    }
}
