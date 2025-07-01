using Application.Common.Localization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.CarsManagement.Cars.Commands.UpdateCar;




public class UpdateCarCommandValidator : AbstractValidator<UpdateCarCommand>
{
    private readonly IApplicationDbContext _applicationDbContext; 
    public UpdateCarCommandValidator(IApplicationDbContext applicationDbContext)
    {
        _applicationDbContext = applicationDbContext;
        RuleFor(x => x.CarModelId).NotEmpty().MustAsync(CarModelMustExist).WithMessage(Resources.CarModelMustExist);
    }
    private async Task<bool> CarModelMustExist(int carModelId, CancellationToken cancellationToken) =>
        await _applicationDbContext.CarModels.AnyAsync(x 
            => x.Id == carModelId, cancellationToken);
}