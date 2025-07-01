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
        RuleFor(x => x.UserId).NotEmpty().MustAsync(UserMustExist).WithMessage(Resources.UserMustExist);
        RuleFor(x => x.CarId).NotEmpty().MustAsync(CarMustExist).WithMessage(Resources.CarMustExist);
        RuleFor(x => x.PlugTypeId)
            .NotEmpty()
            .MustAsync(PlugTypeMustExist).WithMessage(Resources.PlugTypeMustExist);
    }

    private async Task<bool> UserMustExist(int userId, CancellationToken cancellationToken)
        => await _applicationDbContext.UserAccounts.AnyAsync(x => x.Id == userId && !x.IsDeleted && x.IsActive,
            cancellationToken);

    private async Task<bool> CarMustExist(int carId, CancellationToken cancellationToken)
        => await _applicationDbContext.Cars.AnyAsync(x => x.Id == carId, cancellationToken);

    private async Task<bool> PlugTypeMustExist(int plugTypeId, CancellationToken cancellationToken)
        => await _applicationDbContext.PlugTypes.AnyAsync(x => x.Id == plugTypeId, cancellationToken);
}