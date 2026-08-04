using Application.Common.Models;
using Application.Common.Interfaces;
using Application.ServiceProviders.Queries.GetAllServiceProviders;
using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.ServiceProviders.Queries.GetMyServiceProviders;

public record GetMyServiceProvidersRequest(int? CategoryId = null, int? Page = null, int? PageSize = null)
    : IRequest<PagedResult<ServiceProviderDto>>;

public class GetMyServiceProvidersRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    IUploadFileService uploadFileService)
    : IRequestHandler<GetMyServiceProvidersRequest, PagedResult<ServiceProviderDto>>
{
    public async Task<PagedResult<ServiceProviderDto>> Handle(GetMyServiceProvidersRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId!.Value;

        // Providers this user is an active worker for (in addition to ones they own).
        var workerProviderIds = await applicationDbContext.ProviderManagers
            .AsNoTracking()
            .Where(pm => pm.ProviderType == "ServiceProvider"
                      && pm.UserId == userId
                      && pm.IsActive && !pm.IsDeleted)
            .Select(pm => pm.ProviderId)
            .ToListAsync(cancellationToken);

        var query = applicationDbContext.ServiceProviders
            .AsNoTracking()
            .Include(x => x.Owner)
            .Include(x => x.ServiceCategory)
            .Include(x => x.Status)
            .Include(x => x.ServiceProviderRates.Where(r => !r.IsDeleted))
            .Include(x => x.ServiceProviderAttachments.Where(a => !a.IsDeleted))
            .Where(x => !x.IsDeleted && (x.OwnerId == userId || workerProviderIds.Contains(x.Id)));

        if (request.CategoryId.HasValue)
            query = query.Where(x => x.ServiceCategoryId == request.CategoryId.Value);

        var providers = await query
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

        var favCounts = await applicationDbContext.UserFavoriteServiceProviders
            .AsNoTracking()
            .Where(f => !f.IsDeleted && providerIds.Contains(f.ServiceProviderId))
            .GroupBy(f => f.ServiceProviderId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

        return providers.Select(x => new ServiceProviderDto(
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
            !string.IsNullOrEmpty(x.Icon)
                ? uploadFileService.GetFilePath(UploadFileFolders.CableServiceProvider, x.Icon)
                : null,
            x.WhatsAppNumber,
            x.WebsiteUrl,
            x.ServiceProviderRates.Any() ? x.ServiceProviderRates.Average(r => r.Rating) : 0,
            x.ServiceProviderRates.Count,
            x.ServiceProviderAttachments.Select(a =>
                uploadFileService.GetFilePath(UploadFileFolders.CableServiceProvider, a.FileName)).ToList(),
            x.CreatedAt,
            partnerSet.Contains(x.Id),
            favCounts.GetValueOrDefault(x.Id, 0)
        )).ToList().ToOptionallyPaginated(request.Page, request.PageSize);
    }
}
