using Application.ServiceProviders.Queries.GetAllServiceProviders;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ServiceProviders.Queries.GetServiceProviderById;

public record GetServiceProviderByIdRequest(int Id) : IRequest<ServiceProviderDto>;

public class GetServiceProviderByIdRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetServiceProviderByIdRequest, ServiceProviderDto>
{
    public async Task<ServiceProviderDto> Handle(GetServiceProviderByIdRequest request,
        CancellationToken cancellationToken)
    {
        var x = await applicationDbContext.ServiceProviders
                    .AsNoTracking()
                    .Include(sp => sp.Owner)
                    .Include(sp => sp.ServiceCategory)
                    .Include(sp => sp.Status)
                    .Include(sp => sp.ServiceProviderRates.Where(r => !r.IsDeleted))
                    .Include(sp => sp.ServiceProviderAttachments.Where(a => !a.IsDeleted))
                    .FirstOrDefaultAsync(sp => sp.Id == request.Id && !sp.IsDeleted, cancellationToken)
                ?? throw new NotFoundException($"Service provider with id {request.Id} not found");

        // Increment visitors count
        var provider = await applicationDbContext.ServiceProviders
            .FirstAsync(sp => sp.Id == request.Id, cancellationToken);
        provider.VisitorsCount++;
        await applicationDbContext.SaveChanges(cancellationToken);

        var isPartner = await applicationDbContext.PartnerAgreements
            .AsNoTracking()
            .AnyAsync(pa => pa.ProviderType == "ServiceProvider" && pa.ProviderId == x.Id
                            && pa.IsActive && !pa.IsDeleted, cancellationToken);

        return new ServiceProviderDto(
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
            x.VisitorsCount + 1,
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
            isPartner
        );
    }
}
