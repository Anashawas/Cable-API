using Application.ChargingPoints.Queries;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Providers.Queries.GetMyProviderAssets;
using Application.ServiceProviders.Queries.GetAllServiceProviders;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Providers.Queries.GetProviderAssetsByUserId;

public record GetProviderAssetsByUserIdRequest(int UserId) : IRequest<ProviderAssetsDto>;

public class GetProviderAssetsByUserIdRequestHandler(
    IChargingPointRepository chargingPointRepository,
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetProviderAssetsByUserIdRequest, ProviderAssetsDto>
{
    public async Task<ProviderAssetsDto> Handle(GetProviderAssetsByUserIdRequest request,
        CancellationToken cancellationToken)
    {
        var userExists = await applicationDbContext.UserAccounts
            .AnyAsync(x => x.Id == request.UserId && !x.IsDeleted, cancellationToken);

        if (!userExists)
            throw new NotFoundException($"User with id {request.UserId} not found");

        // Get charging points owned by specified user
        var chargingPoints = await chargingPointRepository.GetChargingPointsByOwner(
            request.UserId, null, null, cancellationToken);

        // Get service providers owned by specified user
        var serviceProviders = await applicationDbContext.ServiceProviders
            .AsNoTracking()
            .Include(x => x.Owner)
            .Include(x => x.ServiceCategory)
            .Include(x => x.Status)
            .Include(x => x.ServiceProviderRates.Where(r => !r.IsDeleted))
            .Include(x => x.ServiceProviderAttachments.Where(a => !a.IsDeleted))
            .Where(x => !x.IsDeleted && x.OwnerId == request.UserId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var spIds = serviceProviders.Select(p => p.Id).ToList();
        var partnerProviderIds = await applicationDbContext.PartnerAgreements
            .AsNoTracking()
            .Where(pa => pa.ProviderType == "ServiceProvider" && pa.IsActive && !pa.IsDeleted
                         && spIds.Contains(pa.ProviderId))
            .Select(pa => pa.ProviderId)
            .ToListAsync(cancellationToken);
        var partnerSet = partnerProviderIds.ToHashSet();

        var serviceProviderDtos = serviceProviders.Select(x => new ServiceProviderDto(
            x.Id,
            x.Name,
            x.OwnerId,
            x.Owner?.Name,
            x.ServiceCategoryId,
            x.ServiceCategory?.Name,
            x.ServiceCategory?.NameAr,
            x.StatusId,
            x.Status?.Name,
            x.Description,
            x.Phone,
            x.Address,
            x.CountryName,
            x.CityName,
            x.Latitude,
            x.Longitude,
            x.Price,
            x.PriceDescription,
            x.FromTime,
            x.ToTime,
            x.MethodPayment,
            x.VisitorsCount,
            x.IsVerified,
            x.HasOffer,
            x.OfferDescription,
            x.Service,
            x.Icon,
            x.WhatsAppNumber,
            x.WebsiteUrl,
            x.ServiceProviderRates.Any() ? x.ServiceProviderRates.Average(r => r.Rating) : 0,
            x.ServiceProviderRates.Count,
            x.ServiceProviderAttachments.Select(a => a.FileName).ToList(),
            x.CreatedAt,
            partnerSet.Contains(x.Id)
        )).ToList();

        return new ProviderAssetsDto(chargingPoints, serviceProviderDtos, []);   // admin view: no caller access list
    }
}
