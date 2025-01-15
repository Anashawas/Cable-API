using Application.Common.Localization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Commands.AddChargingPoint;

public class AddChargingPointCommandValidator : AbstractValidator<AddChargingPointCommand>
{
    private readonly IApplicationDbContext _applicationDbContext;

    public AddChargingPointCommandValidator(IApplicationDbContext applicationDbContext)
    {
        _applicationDbContext = applicationDbContext;

        RuleFor(x => x.Name).NotEmpty().WithMessage(Resources.Name);
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90).WithMessage("Latitude must be between -90 and 90.");
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180).WithMessage("Longitude must be between -180 and 180.");
        RuleFor(x => x.ChargerPointTypeId).NotEmpty().WithMessage(Resources.ChargerPointTypeMustExist);
        RuleFor(x => x.StatusId).NotEmpty().WithMessage(Resources.StatusMustExist);
        RuleFor(x => x)
            .MustAsync(CheckNameIsUnique)
            .WithMessage(Resources.NameMustBeUnique);
    }
    
    private async Task<bool> CheckNameIsUnique(AddChargingPointCommand command, CancellationToken cancellationToken)
        => !await _applicationDbContext.ChargingPoints
            .AnyAsync(x => x.Name == command.Name && !x.IsDeleted, cancellationToken);
}