using Application.Common.Localization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Commands.ChangeChargingPointOwner;

public class ChangeChargingPointOwnerCommandValidator : AbstractValidator<ChangeChargingPointOwnerCommand>
{
    private readonly IApplicationDbContext _applicationDbContext;

    public ChangeChargingPointOwnerCommandValidator(IApplicationDbContext applicationDbContext)
    {
        _applicationDbContext = applicationDbContext;

        RuleFor(x => x.ChargingPointId)
            .GreaterThan(0)
            .WithMessage("Charging point ID must be greater than 0")
            .MustAsync(CheckChargingPointExists)
            .WithMessage("Charging point does not exist");

        RuleFor(x => x.NewOwnerId)
            .GreaterThan(0)
            .WithMessage("New owner ID must be greater than 0")
            .MustAsync(CheckUserExists)
            .WithMessage("User does not exist");
    }

    private async Task<bool> CheckChargingPointExists(int chargingPointId, CancellationToken cancellationToken)
        => await _applicationDbContext.ChargingPoints
            .AnyAsync(x => x.Id == chargingPointId && !x.IsDeleted, cancellationToken);

    private async Task<bool> CheckUserExists(int userId, CancellationToken cancellationToken)
        => await _applicationDbContext.UserAccounts
            .AnyAsync(x => x.Id == userId && !x.IsDeleted, cancellationToken);
}
