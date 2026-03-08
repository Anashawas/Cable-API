using Cable.Core;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Partners.Commands.CreatePartnerAgreement;

public record CreatePartnerAgreementCommand(
    string ProviderType,
    int ProviderId,
    double CommissionPercentage,
    double PointsRewardPercentage,
    int? PointsConversionRateId,
    int CodeExpiryMinutes,
    string? Note
) : IRequest<int>;

public class CreatePartnerAgreementCommandValidator : AbstractValidator<CreatePartnerAgreementCommand>
{
    public CreatePartnerAgreementCommandValidator()
    {
        RuleFor(x => x.ProviderType).NotEmpty().MaximumLength(50)
            .Must(x => x is "ChargingPoint" or "ServiceProvider")
            .WithMessage("ProviderType must be 'ChargingPoint' or 'ServiceProvider'");
        RuleFor(x => x.ProviderId).GreaterThan(0);
        RuleFor(x => x.CommissionPercentage).GreaterThan(0).LessThanOrEqualTo(100);
        RuleFor(x => x.PointsRewardPercentage).GreaterThanOrEqualTo(0).LessThanOrEqualTo(100);
        RuleFor(x => x.CodeExpiryMinutes).GreaterThan(0);
    }
}

public class CreatePartnerAgreementCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<CreatePartnerAgreementCommand, int>
{
    public async Task<int> Handle(CreatePartnerAgreementCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        // Check if an active agreement already exists for this provider
        var existingAgreement = await applicationDbContext.PartnerAgreements
            .AnyAsync(x => x.ProviderType == request.ProviderType
                           && x.ProviderId == request.ProviderId
                           && x.IsActive
                           && !x.IsDeleted, cancellationToken);

        if (existingAgreement)
            throw new DataValidationException("Partner", "An active partnership already exists for this provider");

        var now = DateTime.UtcNow;
        var agreement = new PartnerAgreement
        {
            ProviderType = request.ProviderType,
            ProviderId = request.ProviderId,
            CommissionPercentage = request.CommissionPercentage,
            PointsRewardPercentage = request.PointsRewardPercentage,
            PointsConversionRateId = request.PointsConversionRateId,
            CodeExpiryMinutes = request.CodeExpiryMinutes,
            IsActive = true,
            Note = request.Note
        };

        applicationDbContext.PartnerAgreements.Add(agreement);
        await applicationDbContext.SaveChanges(cancellationToken);

        return agreement.Id;
    }
}
