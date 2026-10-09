using Application.ChargingPoints.Queries;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.ServiceProviders.Queries.GetAllServiceProviders;
using Microsoft.EntityFrameworkCore;

namespace Application.Providers.Queries.GetMyProviderAssets;

public record GetMyProviderAssetsRequest() : IRequest<ProviderAssetsDto>;

/// <summary>How the caller may act on one asset: owner (everything) or worker with the owner's privilege list.</summary>
public record ProviderAccessDto(string ProviderType, int ProviderId, bool IsOwner, IReadOnlyList<string> Privileges);

public record ProviderAssetsDto(
    List<GetAllChargingPointsDto> ChargingPoints,
    List<ServiceProviderDto> ServiceProviders,
    List<ProviderAccessDto> Access
);

public class GetMyProviderAssetsRequestHandler(
    IChargingPointRepository chargingPointRepository,
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetMyProviderAssetsRequest, ProviderAssetsDto>
{
    public async Task<ProviderAssetsDto> Handle(GetMyProviderAssetsRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId!.Value;

        // Charging points owned by the user OR assigned to them as a worker
        // (the repository query already covers both cases).
        var chargingPoints = await chargingPointRepository.GetChargingPointsByOwner(
            userId, null, null, cancellationToken);

        // Service providers this user is an active worker for, in addition to
        // the ones they own — mirrors GetChargingPointsByOwner and
        // GetMyServiceProviders, which both include worker assignments.
        var workerServiceProviderIds = await applicationDbContext.ProviderManagers
            .AsNoTracking()
            .Where(pm => pm.ProviderType == "ServiceProvider"
                      && pm.UserId == userId
                      && pm.IsActive && !pm.IsDeleted)
            .Select(pm => pm.ProviderId)
            .ToListAsync(cancellationToken);

        // Get service providers owned by, or worked on by, the current user
        var serviceProviders = await applicationDbContext.ServiceProviders
            .AsNoTracking()
            .Include(x => x.Owner)
            .Include(x => x.ServiceCategory)
            .Include(x => x.Status)
            .Include(x => x.ServiceProviderRates.Where(r => !r.IsDeleted))
            .Include(x => x.ServiceProviderAttachments.Where(a => !a.IsDeleted))
            .Where(x => !x.IsDeleted
                        && (x.OwnerId == userId || workerServiceProviderIds.Contains(x.Id)))
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

        var spFavCounts = await applicationDbContext.UserFavoriteServiceProviders
            .AsNoTracking()
            .Where(f => !f.IsDeleted && spIds.Contains(f.ServiceProviderId))
            .GroupBy(f => f.ServiceProviderId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

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
            partnerSet.Contains(x.Id),
            spFavCounts.GetValueOrDefault(x.Id, 0)
        )).ToList();

        // What the caller may do on each asset: owners everything, workers what the owner granted.
        var workerRows = await applicationDbContext.ProviderManagers.AsNoTracking()
            .Where(pm => pm.UserId == userId && pm.IsActive && !pm.IsDeleted)
            .Select(pm => new { pm.ProviderType, pm.ProviderId, pm.Privileges })
            .ToListAsync(cancellationToken);
        var ownedCps = await applicationDbContext.ChargingPoints.AsNoTracking()
            .Where(c => c.OwnerId == userId && !c.IsDeleted).Select(c => c.Id).ToListAsync(cancellationToken);
        var access = new List<ProviderAccessDto>();
        foreach (var cp in chargingPoints)
        {
            var w = workerRows.FirstOrDefault(r => r.ProviderType == "ChargingPoint" && r.ProviderId == cp.Id);
            var owner = ownedCps.Contains(cp.Id);
            access.Add(new ProviderAccessDto("ChargingPoint", cp.Id, owner, owner || w is null ? Cable.Core.Constants.WorkerPrivileges.All : Cable.Core.Constants.WorkerPrivileges.Parse(w.Privileges)));
        }
        foreach (var sp in serviceProviders)
        {
            var w = workerRows.FirstOrDefault(r => r.ProviderType == "ServiceProvider" && r.ProviderId == sp.Id);
            var owner = sp.OwnerId == userId;
            access.Add(new ProviderAccessDto("ServiceProvider", sp.Id, owner, owner || w is null ? Cable.Core.Constants.WorkerPrivileges.All : Cable.Core.Constants.WorkerPrivileges.Parse(w.Privileges)));
        }

        return new ProviderAssetsDto(chargingPoints, serviceProviderDtos, access);
    }
}
