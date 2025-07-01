using Application.CarsManagement.CarsModels.Commands.AddCarModal;
using Application.Common.Localization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.CarsManagement.CarsModels.Commands.AddCarModel;

public class AddCarModelCommandValidator:AbstractValidator<AddCarModelCommand>
{
    private readonly IApplicationDbContext _applicationDbContext;
    public AddCarModelCommandValidator(IApplicationDbContext applicationDbContext)
    {
        this._applicationDbContext = applicationDbContext;
        RuleFor(x=>x.CarTypeId).NotEmpty().MustAsync(CarTypeMustExist).WithMessage(Resources.CarTypeMustExist);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }

    private async Task<bool> CarTypeMustExist(int carTypeId, CancellationToken cancellationToken)
        => await _applicationDbContext.CarTypes.AnyAsync(x => x.Id == carTypeId, cancellationToken);
}