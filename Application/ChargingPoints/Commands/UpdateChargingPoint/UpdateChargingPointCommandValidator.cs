using Application.Common.Localization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Commands.UpdateChargingPoint;

public class UpdateChargingPointCommandValidator : AbstractValidator<UpdateChargingPointCommand>
{    private readonly IApplicationDbContext _applicationDbContext;

    public UpdateChargingPointCommandValidator(IApplicationDbContext applicationDbContext)
    {
        
        _applicationDbContext = applicationDbContext;
        RuleFor(x => x.Name).NotEmpty().WithMessage(Resources.Name);

        RuleFor(x => x.ChargerPointTypeId).NotEmpty().WithMessage(Resources.ChargerPointTypeMustExist);
        RuleFor(x => x)
            .MustAsync(CheckNameIsUnique)
            .WithMessage(Resources.NameMustBeUnique);
    }
    private async Task<bool> CheckNameIsUnique(UpdateChargingPointCommand command, CancellationToken cancellationToken)
    {
        return !await _applicationDbContext.ChargingPoints
            .AnyAsync(x => x.Name == command.Name && !x.IsDeleted, cancellationToken);
    }
}