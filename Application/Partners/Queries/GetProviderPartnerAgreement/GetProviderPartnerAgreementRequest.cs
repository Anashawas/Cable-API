using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Partners.Queries.GetProviderPartnerAgreement;

public record ProviderPartnerAgreementDto(
    int Id,
    double CommissionPercentage,
    double PointsRewardPercentage,
    string? ConversionRateName,
    double? PointsPerUnit,
    int CodeExpiryMinutes,
    bool IsActive,
    string? Note,
    DateTime? CreatedAt
);

public record GetProviderPartnerAgreementRequest(
    string ProviderType,
    int ProviderId
) : IRequest<ProviderPartnerAgreementDto>;

public class GetProviderPartnerAgreementRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetProviderPartnerAgreementRequest, ProviderPartnerAgreementDto>
{
    public async Task<ProviderPartnerAgreementDto> Handle(
        GetProviderPartnerAgreementRequest request, CancellationToken cancellationToken)
    {
        var agreement = await applicationDbContext.PartnerAgreements
                            .Include(x => x.ConversionRate)
                            .FirstOrDefaultAsync(x => x.ProviderType == request.ProviderType
                                                       && x.ProviderId == request.ProviderId
                                                       && x.IsActive
                                                       && !x.IsDeleted, cancellationToken)
                        ?? throw new NotFoundException(
                            $"Active partner agreement for {request.ProviderType} with Id '{request.ProviderId}' not found");

        return new ProviderPartnerAgreementDto(
            agreement.Id,
            agreement.CommissionPercentage,
            agreement.PointsRewardPercentage,
            agreement.ConversionRate?.Name,
            agreement.ConversionRate?.PointsPerUnit,
            agreement.CodeExpiryMinutes,
            agreement.IsActive,
            agreement.Note,
            agreement.CreatedAt);
    }
}
