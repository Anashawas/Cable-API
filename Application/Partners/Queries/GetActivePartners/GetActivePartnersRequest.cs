using Microsoft.EntityFrameworkCore;

namespace Application.Partners.Queries.GetActivePartners;

public record PartnerDto(
    int Id,
    string ProviderType,
    int ProviderId,
    string? ProviderName,
    double CommissionPercentage,
    double PointsRewardPercentage,
    int CodeExpiryMinutes,
    string? Note
);

public record GetActivePartnersRequest(string? ProviderType) : IRequest<List<PartnerDto>>;

public class GetActivePartnersRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetActivePartnersRequest, List<PartnerDto>>
{
    public async Task<List<PartnerDto>> Handle(GetActivePartnersRequest request, CancellationToken cancellationToken)
    {
        var query = applicationDbContext.PartnerAgreements
            .Where(x => x.IsActive && !x.IsDeleted);

        if (!string.IsNullOrEmpty(request.ProviderType))
            query = query.Where(x => x.ProviderType == request.ProviderType);

        var agreements = await query.OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);

        var result = new List<PartnerDto>();
        foreach (var a in agreements)
        {
            string? providerName = null;

            if (a.ProviderType == "ChargingPoint")
            {
                providerName = await applicationDbContext.ChargingPoints
                    .Where(x => x.Id == a.ProviderId && !x.IsDeleted)
                    .Select(x => x.Name)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            else if (a.ProviderType == "ServiceProvider")
            {
                providerName = await applicationDbContext.ServiceProviders
                    .Where(x => x.Id == a.ProviderId && !x.IsDeleted)
                    .Select(x => x.Name)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            result.Add(new PartnerDto(
                a.Id, a.ProviderType, a.ProviderId, providerName,
                a.CommissionPercentage, a.PointsRewardPercentage,
                a.CodeExpiryMinutes, a.Note));
        }

        return result;
    }
}
