using Application.Common.Localization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Rates.Commands.AddRateCommand;

public class AddRateCommandValidator : AbstractValidator<AddRateCommand>
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IApplicationDbContext _applicationDbContext;
    public AddRateCommandValidator(ICurrentUserService currentUserService, IApplicationDbContext applicationDbContext)
    {
        _currentUserService = currentUserService;
        _applicationDbContext = applicationDbContext;

        RuleFor(x => x.ChargingPointId).NotEmpty();
        RuleFor(x => x.ChargingPointRate).NotEmpty();
        RuleFor(x => x)
            .MustAsync(CheckUserHasNotRatedChargingPoint)
            .WithMessage(Resources.UserAlreadyRated);

    }
    
    private async Task<bool> CheckUserHasNotRatedChargingPoint(AddRateCommand command, CancellationToken cancellationToken)
    {
        return !await _applicationDbContext.Rates.AnyAsync(x => x.UserId == _currentUserService.UserId && x.ChargingPointId == command.ChargingPointId && !x.IsDeleted, cancellationToken);
    }
}