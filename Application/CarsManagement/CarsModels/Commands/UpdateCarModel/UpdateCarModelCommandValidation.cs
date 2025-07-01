using Application.Common.Localization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.CarsManagement.CarsModels.Commands.UpdateCarModel;

public class UpdateCarModelCommandValidation :  AbstractValidator<UpdateCarModelCommand>
{
    private readonly IApplicationDbContext _applicationDbContext;
    public UpdateCarModelCommandValidation(IApplicationDbContext applicationDbContext)
    {
        _applicationDbContext = applicationDbContext;
        RuleFor(x=>x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x=>x.CarTypeId).NotEmpty().MustAsync(CarTypeMustExist).WithMessage(Resources.CarTypeMustExist);

    }
    private async Task<bool> CarTypeMustExist(int carTypeId, CancellationToken cancellationToken)
        => await _applicationDbContext.CarTypes.AnyAsync(x => x.Id == carTypeId, cancellationToken);
}