using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Partners.Commands.UpdatePartnerAgreement;

public record UpdatePartnerAgreementCommand(
    int Id,
    double CommissionPercentage,
    double PointsRewardPercentage,
    int? PointsConversionRateId,
    int CodeExpiryMinutes,
    string? Note,
    bool IsActive
) : IRequest;

public class UpdatePartnerAgreementCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdatePartnerAgreementCommand>
{
    public async Task Handle(UpdatePartnerAgreementCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var agreement = await applicationDbContext.PartnerAgreements
                            .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                        ?? throw new NotFoundException($"Partner agreement with Id '{request.Id}' not found");

        agreement.CommissionPercentage = request.CommissionPercentage;
        agreement.PointsRewardPercentage = request.PointsRewardPercentage;
        agreement.PointsConversionRateId = request.PointsConversionRateId;
        agreement.CodeExpiryMinutes = request.CodeExpiryMinutes;
        agreement.Note = request.Note;
        agreement.IsActive = request.IsActive;
        agreement.ModifiedAt = DateTime.UtcNow;
        agreement.ModifiedBy = userId;

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
