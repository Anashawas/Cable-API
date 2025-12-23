using Application.Common.Localization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.CarsManagement.CarsModels.Commands.DeleteCarModel;

public class DeleteCarModelCommandValidator : AbstractValidator<DeleteCarModelCommand>
{
    private readonly IApplicationDbContext _applicationDbContext;

    public DeleteCarModelCommandValidator(IApplicationDbContext applicationDbContext)
    {
        _applicationDbContext = applicationDbContext;
        RuleFor(x => x.Id).NotEmpty().MustAsync(CheckCarModelInUse)
            .WithMessage(Resources.CheckCarModelInUse);
    }

    private async Task<bool> CheckCarModelInUse(int id, CancellationToken cancellationToken)
        => !await _applicationDbContext.UserCars.AnyAsync(x => x.CarModelId == id && !x.IsDeleted, cancellationToken);
}