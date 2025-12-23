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
        RuleFor(x => x.StationTypeId).NotEmpty().WithMessage(Resources.StationTypeMustExist);
        RuleFor(x => x.OfferDescription).MaximumLength(1000).WithMessage("Offer description must not exceed 1000 characters.");
        RuleFor(x => x)
            .MustAsync(CheckNameIsUnique)
            .WithMessage(Resources.NameMustBeUnique);
        RuleFor(x => x.ChargerPointTypeId)
            .MustAsync(CheckChargerPointTypeExists)
            .WithMessage(Resources.ChargerPointTypeMustExist);
        RuleFor(x => x.StatusId)
            .MustAsync(CheckStatusExists)
            .WithMessage(Resources.StatusMustExist);
        RuleFor(x => x.StationTypeId)
            .MustAsync(CheckStationTypeExists)
            .WithMessage(Resources.StationTypeMustExist);
    }
    
    private async Task<bool> CheckNameIsUnique(AddChargingPointCommand command, CancellationToken cancellationToken)
        => !await _applicationDbContext.ChargingPoints
            .AnyAsync(x => x.Name == command.Name && !x.IsDeleted, cancellationToken);
            
    private async Task<bool> CheckChargerPointTypeExists(int chargerPointTypeId, CancellationToken cancellationToken)
        => await _applicationDbContext.ChargingPointTypes
            .AnyAsync(x => x.Id == chargerPointTypeId , cancellationToken);
            
    private async Task<bool> CheckStatusExists(int statusId, CancellationToken cancellationToken)
        => await _applicationDbContext.Statuses
            .AnyAsync(x => x.Id == statusId , cancellationToken);
            
    private async Task<bool> CheckStationTypeExists(int stationTypeId, CancellationToken cancellationToken)
        => await _applicationDbContext.StationTypes
            .AnyAsync(x => x.Id == stationTypeId, cancellationToken);
}