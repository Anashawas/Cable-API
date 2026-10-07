using Application.Common.Interfaces;
using Application.Common.Security;

namespace Application.Settings.Queries.GetWelcomeBonus;

/// <param name="Multiplier">What a customer's first ever charge is multiplied by.</param>
/// <param name="IsEnabled">
/// False when the multiplier is 1 — the bonus is off. Derived rather than stored,
/// so there is no second switch that can disagree with the number.
/// </param>
/// <param name="IsDefault">
/// True when no AppSetting row exists and the shipped default is in force. Lets
/// the admin screen show "default (2x)" instead of implying someone chose it.
/// </param>
public record WelcomeBonusDto(double Multiplier, bool IsEnabled, bool IsDefault);

/// <summary>Admin: the current welcome-bonus setting and whether it is overridden.</summary>
public record GetWelcomeBonusRequest : IRequest<WelcomeBonusDto>;

public class GetWelcomeBonusRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetWelcomeBonusRequest, WelcomeBonusDto>
{
    public async Task<WelcomeBonusDto> Handle(
        GetWelcomeBonusRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var multiplier = await AppSettingsProvider
            .GetWelcomeBonusMultiplierAsync(applicationDbContext, cancellationToken);

        var isOverridden = await AppSettingsProvider
            .HasWelcomeBonusOverrideAsync(applicationDbContext, cancellationToken);

        return new WelcomeBonusDto(multiplier, multiplier > 1.0, !isOverridden);
    }
}
