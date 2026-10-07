using Application.Common.Interfaces;
using Application.Common.Security;
using Domain.Enitites;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Settings.Commands.UpdateWelcomeBonus;

/// <summary>
/// Admin: change what a customer's first ever charge is multiplied by.
///
/// <b>1 switches the welcome bonus off.</b> There is deliberately no separate
/// enabled flag — one number cannot contradict itself, whereas a flag and a
/// multiplier can, and then nobody can tell from the row whether the bonus is
/// running.
/// </summary>
public record UpdateWelcomeBonusCommand(double Multiplier) : IRequest;

public class UpdateWelcomeBonusCommandValidator : AbstractValidator<UpdateWelcomeBonusCommand>
{
    /// <summary>
    /// Matches the campaign cap in LoyaltyBoostRules. A typo guard rather than a
    /// business limit: this multiplies points that cost real money, and a slipped
    /// decimal turning 2 into 20 should not reach production.
    /// </summary>
    private const double MaxMultiplier = 10.0;

    public UpdateWelcomeBonusCommandValidator()
    {
        RuleFor(x => x.Multiplier)
            .GreaterThanOrEqualTo(1.0)
            .WithMessage("Multiplier must be at least 1. Use exactly 1 to switch the welcome bonus off; below 1 would take points away.")
            .LessThanOrEqualTo(MaxMultiplier)
            .WithMessage($"Multiplier must not exceed {MaxMultiplier}.");
    }
}

public class UpdateWelcomeBonusCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateWelcomeBonusCommand>
{
    public async Task Handle(UpdateWelcomeBonusCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var setting = await applicationDbContext.AppSettings
            .FirstOrDefaultAsync(s => s.Key == AppSettingsProvider.WelcomeBonusMultiplierKey && !s.IsDeleted,
                cancellationToken);

        if (setting is null)
        {
            applicationDbContext.AppSettings.Add(new AppSetting
            {
                Key = AppSettingsProvider.WelcomeBonusMultiplierKey,
                Value = request.Multiplier.ToString("0.##")
            });
        }
        else
        {
            setting.Value = request.Multiplier.ToString("0.##");
        }

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
