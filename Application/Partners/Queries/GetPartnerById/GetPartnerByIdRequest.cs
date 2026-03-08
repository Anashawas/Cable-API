using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Partners.Queries.GetPartnerById;

public record GetPartnerByIdRequest(int Id) : IRequest<PartnerDetailDto>;

public record PartnerDetailDto(
    int Id,
    string ProviderType,
    int ProviderId,
    string? ProviderName,
    double CommissionPercentage,
    double PointsRewardPercentage,
    int? PointsConversionRateId,
    string? ConversionRateName,
    double? PointsPerUnit,
    int CodeExpiryMinutes,
    bool IsActive,
    string? Note,
    DateTime? CreatedAt
);

public class GetPartnerByIdRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetPartnerByIdRequest, PartnerDetailDto>
{
    public async Task<PartnerDetailDto> Handle(GetPartnerByIdRequest request, CancellationToken cancellationToken)
    {
        var agreement = await applicationDbContext.PartnerAgreements
                            .Include(x => x.ConversionRate)
                            .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                        ?? throw new NotFoundException($"Partner agreement with Id '{request.Id}' not found");

        string? providerName = null;
        if (agreement.ProviderType == "ChargingPoint")
        {
            providerName = await applicationDbContext.ChargingPoints
                .Where(x => x.Id == agreement.ProviderId && !x.IsDeleted)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }
        else if (agreement.ProviderType == "ServiceProvider")
        {
            providerName = await applicationDbContext.ServiceProviders
                .Where(x => x.Id == agreement.ProviderId && !x.IsDeleted)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new PartnerDetailDto(
            agreement.Id,
            agreement.ProviderType,
            agreement.ProviderId,
            providerName,
            agreement.CommissionPercentage,
            agreement.PointsRewardPercentage,
            agreement.PointsConversionRateId,
            agreement.ConversionRate?.Name,
            agreement.ConversionRate?.PointsPerUnit,
            agreement.CodeExpiryMinutes,
            agreement.IsActive,
            agreement.Note,
            agreement.CreatedAt);
    }
}
