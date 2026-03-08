using Microsoft.EntityFrameworkCore;

namespace Application.Partners.Queries.GetAllPartnerAgreements;

public record AdminPartnerAgreementDto(
    int Id,
    string ProviderType,
    int ProviderId,
    string? ProviderName,
    double CommissionPercentage,
    double PointsRewardPercentage,
    int? PointsConversionRateId,
    string? ConversionRateName,
    int CodeExpiryMinutes,
    bool IsActive,
    string? Note,
    DateTime? CreatedAt
);

public record GetAllPartnerAgreementsRequest(bool? IsActive) : IRequest<List<AdminPartnerAgreementDto>>;

public class GetAllPartnerAgreementsRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetAllPartnerAgreementsRequest, List<AdminPartnerAgreementDto>>
{
    public async Task<List<AdminPartnerAgreementDto>> Handle(
        GetAllPartnerAgreementsRequest request, CancellationToken cancellationToken)
    {
        var query = applicationDbContext.PartnerAgreements
            .Include(x => x.ConversionRate)
            .Where(x => !x.IsDeleted);

        if (request.IsActive.HasValue)
            query = query.Where(x => x.IsActive == request.IsActive.Value);

        var agreements = await query
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var result = new List<AdminPartnerAgreementDto>();
        foreach (var a in agreements)
        {
            string? providerName = null;
            if (a.ProviderType == "ChargingPoint")
            {
                providerName = await applicationDbContext.ChargingPoints
                    .Where(x => x.Id == a.ProviderId)
                    .Select(x => x.Name)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            else if (a.ProviderType == "ServiceProvider")
            {
                providerName = await applicationDbContext.ServiceProviders
                    .Where(x => x.Id == a.ProviderId)
                    .Select(x => x.Name)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            result.Add(new AdminPartnerAgreementDto(
                a.Id, a.ProviderType, a.ProviderId, providerName,
                a.CommissionPercentage, a.PointsRewardPercentage,
                a.PointsConversionRateId, a.ConversionRate?.Name,
                a.CodeExpiryMinutes, a.IsActive, a.Note, a.CreatedAt));
        }

        return result;
    }
}
