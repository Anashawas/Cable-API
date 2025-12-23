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
        RuleFor(x => x.StationTypeId).NotEmpty().WithMessage(Resources.StationTypeMustExist);
        RuleFor(x => x.OfferDescription).MaximumLength(1000).WithMessage("Offer description must not exceed 1000 characters.");
        RuleFor(x => x)
            .MustAsync(CheckNameIsUnique)
            .WithMessage(Resources.NameMustBeUnique);
        RuleFor(x => x.ChargerPointTypeId)
            .MustAsync(CheckChargerPointTypeExists)
            .WithMessage(Resources.ChargerPointTypeMustExist);
        RuleFor(x => x.StationTypeId)
            .MustAsync(CheckStationTypeExists)
            .WithMessage(Resources.StationTypeMustExist);
    }
    private async Task<bool> CheckNameIsUnique(UpdateChargingPointCommand command, CancellationToken cancellationToken)
    {
        return !await _applicationDbContext.ChargingPoints
            .AnyAsync(x => x.Name == command.Name && x.Id != command.Id && !x.IsDeleted, cancellationToken);
    }
    
    private async Task<bool> CheckChargerPointTypeExists(int chargerPointTypeId, CancellationToken cancellationToken)
        => await _applicationDbContext.ChargingPointTypes
            .AnyAsync(x => x.Id == chargerPointTypeId , cancellationToken);
            
    private async Task<bool> CheckStationTypeExists(int stationTypeId, CancellationToken cancellationToken)
        => await _applicationDbContext.StationTypes
            .AnyAsync(x => x.Id == stationTypeId, cancellationToken);
}