using Application.CarsManagement.UserCars.Commands.UpdateUserCar;
using Application.Common.Localization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.CarsManagement.UserCars.Commands.UpdateUserCar;

public class UpdateUserCarCommandValidator : AbstractValidator<UpdateUserCarCommand>
{
    private readonly IApplicationDbContext _applicationDbContext;

    public UpdateUserCarCommandValidator(IApplicationDbContext applicationDbContext)
    {
        _applicationDbContext = applicationDbContext;
        RuleFor(x => x.Id).NotEmpty().MustAsync(UserMustExist).WithMessage(Resources.UserCarMustExist);
        RuleFor(x => x.CarModelId).NotEmpty().MustAsync(CarModelMustExist).WithMessage(Resources.CarMustExist);
        RuleFor(x => x.PlugTypeId)
            .NotEmpty()
            .MustAsync(PlugTypeMustExist).WithMessage(Resources.PlugTypeMustExist);
    }

    private async Task<bool> UserMustExist(int userId, CancellationToken cancellationToken)
        => await _applicationDbContext.UserCars.AnyAsync(x => x.Id == userId && !x.IsDeleted,
            cancellationToken);

    private async Task<bool> CarModelMustExist(int carId, CancellationToken cancellationToken)
        => await _applicationDbContext.CarModels.AnyAsync(x => x.Id == carId, cancellationToken);

    private async Task<bool> PlugTypeMustExist(int plugTypeId, CancellationToken cancellationToken)
        => await _applicationDbContext.PlugTypes.AnyAsync(x => x.Id == plugTypeId, cancellationToken);
}