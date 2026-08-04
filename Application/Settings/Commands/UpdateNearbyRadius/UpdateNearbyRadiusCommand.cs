using Application.Common.Interfaces;
using Application.Common.Security;
using Domain.Enitites;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Settings.Commands.UpdateNearbyRadius;

/// <summary>Admin: change the global nearby-radius cap (km) for home-screen serving.</summary>
public record UpdateNearbyRadiusCommand(double RadiusKm) : IRequest;

public class UpdateNearbyRadiusCommandValidator : AbstractValidator<UpdateNearbyRadiusCommand>
{
    public UpdateNearbyRadiusCommandValidator()
    {
        RuleFor(x => x.RadiusKm).GreaterThan(0).LessThanOrEqualTo(500);
    }
}

public class UpdateNearbyRadiusCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateNearbyRadiusCommand>
{
    public async Task Handle(UpdateNearbyRadiusCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var setting = await applicationDbContext.AppSettings
            .FirstOrDefaultAsync(s => s.Key == AppSettingsProvider.NearbyRadiusKey && !s.IsDeleted, cancellationToken);

        if (setting is null)
        {
            applicationDbContext.AppSettings.Add(new AppSetting
            {
                Key = AppSettingsProvider.NearbyRadiusKey,
                Value = request.RadiusKm.ToString("0.##")
            });
        }
        else
        {
            setting.Value = request.RadiusKm.ToString("0.##");
        }

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
