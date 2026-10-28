using System.Globalization;
using Application.Common.Interfaces;
using Application.Common.Security;
using Application.Settings;
using Domain.Enitites;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.LiveCharging;

/// <summary>Admin: the Sprint 5 live-charging switches (AppSetting rows) in one place.</summary>
public record LiveChargingSettingsDto(
    /// <summary>Ocpp.AppStart.MaxDistanceMeters — how far from the station an app start may be (default 300).</summary>
    int MaxDistanceMeters,
    /// <summary>Ocpp.AppStart.RequireLocation — refuse an app start without a position (default true).</summary>
    bool RequireLocation,
    /// <summary>Ocpp.AutoPoints.Enabled — automatic points when a driver's session ends (default false).</summary>
    bool AutoPointsEnabled,
    /// <summary>When automatic points were last switched (UTC): only sessions that ended after it earn them.</summary>
    DateTime? AutoPointsSince);

public record GetLiveChargingSettingsRequest : IRequest<LiveChargingSettingsDto>;

public class GetLiveChargingSettingsRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetLiveChargingSettingsRequest, LiveChargingSettingsDto>
{
    public async Task<LiveChargingSettingsDto> Handle(GetLiveChargingSettingsRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        return await LiveChargingSettingsReader.ReadAsync(db, cancellationToken);
    }
}

/// <summary>Admin: change any of the switches; null leaves that one as it is.</summary>
public record SetLiveChargingSettingsCommand(int? MaxDistanceMeters, bool? RequireLocation, bool? AutoPointsEnabled) : IRequest<LiveChargingSettingsDto>;

public class SetLiveChargingSettingsCommandValidator : AbstractValidator<SetLiveChargingSettingsCommand>
{
    public SetLiveChargingSettingsCommandValidator()
    {
        RuleFor(x => x.MaxDistanceMeters).InclusiveBetween(10, 100_000).When(x => x.MaxDistanceMeters is not null);
    }
}

public class SetLiveChargingSettingsCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<SetLiveChargingSettingsCommand, LiveChargingSettingsDto>
{
    public async Task<LiveChargingSettingsDto> Handle(SetLiveChargingSettingsCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);

        if (request.MaxDistanceMeters is int m)
            await UpsertAsync(AppSettingsProvider.OcppAppStartMaxDistanceKey, m.ToString(CultureInfo.InvariantCulture), cancellationToken);
        if (request.RequireLocation is bool r)
            await UpsertAsync(AppSettingsProvider.OcppAppStartRequireLocationKey, r ? "true" : "false", cancellationToken);
        // Automatic points are not used (decision Oct 28): the admin API no longer switches them on; the
        // command keeps the parameter only so a deliberate SQL / future change can still use it.
        if (request.AutoPointsEnabled is bool a)
            await UpsertAsync(AppSettingsProvider.OcppAutoPointsEnabledKey, a ? "true" : "false", cancellationToken);

        await db.SaveChanges(cancellationToken);
        return await LiveChargingSettingsReader.ReadAsync(db, cancellationToken);
    }

    /// <summary>Only a real change touches the row — so ModifiedAt (the automatic-points "since") moves only when the switch does.</summary>
    private async Task UpsertAsync(string key, string value, CancellationToken ct)
    {
        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key && !s.IsDeleted, ct);
        if (row is null) db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        else if (!string.Equals(row.Value?.Trim(), value, StringComparison.OrdinalIgnoreCase)) row.Value = value;
    }
}

internal static class LiveChargingSettingsReader
{
    public static async Task<LiveChargingSettingsDto> ReadAsync(IApplicationDbContext db, CancellationToken ct)
    {
        var start = await AppSettingsProvider.GetOcppAppStartAsync(db, ct);
        var points = await AppSettingsProvider.GetOcppAutoPointsAsync(db, ct);
        return new LiveChargingSettingsDto(start.MaxDistanceMeters, start.RequireLocation, points.Enabled, points.Since);
    }
}
