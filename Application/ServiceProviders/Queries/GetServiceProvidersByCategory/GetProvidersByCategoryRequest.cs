using Application.Common.Models;
using Application.ServiceProviders.Queries.GetAllServiceProviders;
using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.ServiceProviders.Queries.GetServiceProvidersByCategory;

public record GetProvidersByCategoryRequest(int CategoryId, int? Page = null, int? PageSize = null) : IRequest<PagedResult<ServiceProviderDto>>;

public class GetProvidersByCategoryRequestHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<GetProvidersByCategoryRequest, PagedResult<ServiceProviderDto>>
{
    public async Task<PagedResult<ServiceProviderDto>> Handle(GetProvidersByCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var providers = await applicationDbContext.ServiceProviders
            .AsNoTracking()
            .Include(x => x.Owner)
            .Include(x => x.ServiceCategory)
            .Include(x => x.Status)
            .Include(x => x.ServiceProviderRates.Where(r => !r.IsDeleted))
            .Include(x => x.ServiceProviderAttachments.Where(a => !a.IsDeleted))
            .Where(x => !x.IsDeleted && x.ServiceCategoryId == request.CategoryId)
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
            x.Service,
            !string.IsNullOrEmpty(x.Icon)
                ? uploadFileService.GetFilePath(UploadFileFolders.CableServiceProvider, x.Icon)
                : null,
            x.WhatsAppNumber, x.WebsiteUrl,
            x.ServiceProviderRates.Any() ? x.ServiceProviderRates.Average(r => r.Rating) : 0,
            x.ServiceProviderRates.Count,
            x.ServiceProviderAttachments.Select(a =>
                uploadFileService.GetFilePath(UploadFileFolders.CableServiceProvider, a.FileName)).ToList(),
            x.CreatedAt,
            partnerSet.Contains(x.Id)
        )).ToList().ToOptionallyPaginated(request.Page, request.PageSize);
    }
}
