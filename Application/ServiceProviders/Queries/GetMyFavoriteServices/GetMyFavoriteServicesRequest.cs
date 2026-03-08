using Application.ServiceProviders.Queries.GetAllServiceProviders;
using Cable.Core;
using Microsoft.EntityFrameworkCore;

namespace Application.ServiceProviders.Queries.GetMyFavoriteServices;

public record GetMyFavoriteServicesRequest() : IRequest<List<ServiceProviderDto>>;

public class GetMyFavoriteServicesRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetMyFavoriteServicesRequest, List<ServiceProviderDto>>
{
    public async Task<List<ServiceProviderDto>> Handle(GetMyFavoriteServicesRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var favoriteProviderIds = await applicationDbContext.UserFavoriteServiceProviders
            .AsNoTracking()
            .Where(x => x.UserId == userId && !x.IsDeleted)
            .Select(x => x.ServiceProviderId)
            .ToListAsync(cancellationToken);

        var providers = await applicationDbContext.ServiceProviders
            .AsNoTracking()
            .Include(x => x.Owner)
            .Include(x => x.ServiceCategory)
            .Include(x => x.Status)
            .Include(x => x.ServiceProviderRates.Where(r => !r.IsDeleted))
            .Include(x => x.ServiceProviderAttachments.Where(a => !a.IsDeleted))
            .Where(x => !x.IsDeleted && favoriteProviderIds.Contains(x.Id))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var providerIds = providers.Select(p => p.Id).ToList();
        var partnerProviderIds = await applicationDbContext.PartnerAgreements
            .AsNoTracking()
            .Where(pa => pa.ProviderType == "ServiceProvider" && pa.IsActive && !pa.IsDeleted
                         && providerIds.Contains(pa.ProviderId))
            .Select(pa => pa.ProviderId)
            .ToListAsync(cancellationToken);
        var partnerSet = partnerProviderIds.ToHashSet();

        return providers.Select(x => new ServiceProviderDto(
            x.Id, x.Name, x.OwnerId, x.Owner?.Name,
            x.ServiceCategoryId, x.ServiceCategory?.Name, x.ServiceCategory?.NameAr,
            x.StatusId, x.Status?.Name, x.Description, x.Phone,
            x.Address, x.CountryName, x.CityName, x.Latitude, x.Longitude,
            x.Price, x.PriceDescription, x.FromTime, x.ToTime, x.MethodPayment,
            x.VisitorsCount, x.IsVerified, x.HasOffer, x.OfferDescription,
            x.Service, x.Icon, x.WhatsAppNumber, x.WebsiteUrl,
            x.ServiceProviderRates.Any() ? x.ServiceProviderRates.Average(r => r.Rating) : 0,
            x.ServiceProviderRates.Count,
            x.ServiceProviderAttachments.Select(a => a.FileName).ToList(),
            x.CreatedAt,
            partnerSet.Contains(x.Id)
        )).ToList();
    }
}
