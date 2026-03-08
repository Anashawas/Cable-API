using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.ServiceProviders.Commands.ChangeServiceProviderOwner;

public class ChangeServiceProviderOwnerCommandValidator : AbstractValidator<ChangeServiceProviderOwnerCommand>
{
    private readonly IApplicationDbContext _applicationDbContext;

    public ChangeServiceProviderOwnerCommandValidator(IApplicationDbContext applicationDbContext)
    {
        _applicationDbContext = applicationDbContext;

        RuleFor(x => x.ServiceProviderId)
            .GreaterThan(0)
            .WithMessage("Service provider ID must be greater than 0")
            .MustAsync(CheckServiceProviderExists)
            .WithMessage("Service provider does not exist");

        RuleFor(x => x.NewOwnerId)
            .GreaterThan(0)
            .WithMessage("New owner ID must be greater than 0")
            .MustAsync(CheckUserExists)
            .WithMessage("User does not exist");
    }

    private async Task<bool> CheckServiceProviderExists(int serviceProviderId, CancellationToken cancellationToken)
        => await _applicationDbContext.ServiceProviders
            .AnyAsync(x => x.Id == serviceProviderId && !x.IsDeleted, cancellationToken);

    private async Task<bool> CheckUserExists(int userId, CancellationToken cancellationToken)
        => await _applicationDbContext.UserAccounts
            .AnyAsync(x => x.Id == userId && !x.IsDeleted, cancellationToken);
}
