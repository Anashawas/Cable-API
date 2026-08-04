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
    int CodeExpirySeconds,
    decimal? MinimumTransactionAmount,
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
        RuleFor(x => x.CodeExpirySeconds).GreaterThan(0);
        RuleFor(x => x.MinimumTransactionAmount).GreaterThan(0)
            .When(x => x.MinimumTransactionAmount.HasValue)
            .WithMessage("MinimumTransactionAmount must be greater than 0");
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

        // A partner agreement needs an owner as the commission/settlement counterparty —
        // block agreements on unassigned providers.
        var providerOwner = request.ProviderType == "ChargingPoint"
            ? await applicationDbContext.ChargingPoints
                .Where(x => x.Id == request.ProviderId && !x.IsDeleted)
                .Select(x => new { x.OwnerId })
                .FirstOrDefaultAsync(cancellationToken)
            : await applicationDbContext.ServiceProviders
                .Where(x => x.Id == request.ProviderId && !x.IsDeleted)
                .Select(x => new { x.OwnerId })
                .FirstOrDefaultAsync(cancellationToken);

        if (providerOwner == null)
            throw new Cable.Core.Exceptions.NotFoundException(
                $"{request.ProviderType} with id {request.ProviderId} not found");

        if (providerOwner.OwnerId == null)
            throw new DataValidationException("ProviderId",
                "This provider has no owner assigned. Assign an owner before creating a partner agreement.");

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
            CodeExpirySeconds = request.CodeExpirySeconds,
            MinimumTransactionAmount = request.MinimumTransactionAmount,
            IsActive = true,
            Note = request.Note
        };

        applicationDbContext.PartnerAgreements.Add(agreement);
        await applicationDbContext.SaveChanges(cancellationToken);

        return agreement.Id;
    }
}
