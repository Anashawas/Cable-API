using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Constants;
using Cable.Core.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.Commands.ManageAlertThresholds;

// ---------------------------------------------------------------------------
// Per-station alert thresholds. The alert job (CheckOcppAlerts) uses these minutes
// instead of the OcppLimits defaults when set. Null = default. Admin only.
// ---------------------------------------------------------------------------

/// <summary>One threshold: the station's own value (null = default), the default, and what the job uses.</summary>
public record OcppAlertThresholdDto(int? Minutes, int DefaultMinutes, int EffectiveMinutes, int MinMinutes, int MaxMinutes);

public record OcppAlertThresholdsDto(
    int ChargingPointId,
    OcppAlertThresholdDto Offline,
    OcppAlertThresholdDto Faulted,
    OcppAlertThresholdDto LongSession,
    OcppAlertThresholdDto Parked);

public static class OcppAlertThresholds
{
    public const int OfflineMin = 5, OfflineMax = 1440;
    public const int FaultedMin = 5, FaultedMax = 1440;
    public const int LongSessionMin = 30, LongSessionMax = 2880;
    public const int ParkedMin = 5, ParkedMax = 720;

    public static int DefaultOffline => (int)OcppLimits.OfflineAlertAfter.TotalMinutes;
    public static int DefaultFaulted => (int)OcppLimits.FaultedAlertAfter.TotalMinutes;
    public static int DefaultLongSession => (int)OcppLimits.LongSessionAlertAfter.TotalMinutes;
    public static int DefaultParked => (int)OcppLimits.ParkedAlertAfter.TotalMinutes;

    public static OcppAlertThresholdsDto Build(int chargingPointId, int? offline, int? faulted, int? longSession, int? parked) => new(
        chargingPointId,
        new(offline, DefaultOffline, offline ?? DefaultOffline, OfflineMin, OfflineMax),
        new(faulted, DefaultFaulted, faulted ?? DefaultFaulted, FaultedMin, FaultedMax),
        new(longSession, DefaultLongSession, longSession ?? DefaultLongSession, LongSessionMin, LongSessionMax),
        new(parked, DefaultParked, parked ?? DefaultParked, ParkedMin, ParkedMax));
}

/// <summary>Admin: the station's thresholds with defaults and allowed ranges.</summary>
public record GetOcppAlertThresholdsRequest(int ChargingPointId) : IRequest<OcppAlertThresholdsDto>;

public class GetOcppAlertThresholdsRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetOcppAlertThresholdsRequest, OcppAlertThresholdsDto>
{
    public async Task<OcppAlertThresholdsDto> Handle(GetOcppAlertThresholdsRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var cp = await db.ChargingPoints.AsNoTracking()
            .Where(c => c.Id == request.ChargingPointId && !c.IsDeleted)
            .Select(c => new { c.OcppOfflineAlertMin, c.OcppFaultedAlertMin, c.OcppLongSessionAlertMin, c.OcppParkedAlertMin })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("cannot find charging point with id: " + request.ChargingPointId);
        return OcppAlertThresholds.Build(request.ChargingPointId, cp.OcppOfflineAlertMin, cp.OcppFaultedAlertMin, cp.OcppLongSessionAlertMin, cp.OcppParkedAlertMin);
    }
}

/// <summary>Admin: set the station's thresholds in minutes; null restores the default for that alert.</summary>
public record SetOcppAlertThresholdsCommand(int ChargingPointId, int? OfflineMinutes, int? FaultedMinutes, int? LongSessionMinutes, int? ParkedMinutes)
    : IRequest<OcppAlertThresholdsDto>;

public class SetOcppAlertThresholdsCommandValidator : AbstractValidator<SetOcppAlertThresholdsCommand>
{
    public SetOcppAlertThresholdsCommandValidator()
    {
        RuleFor(x => x.OfflineMinutes).InclusiveBetween(OcppAlertThresholds.OfflineMin, OcppAlertThresholds.OfflineMax).When(x => x.OfflineMinutes != null);
        RuleFor(x => x.FaultedMinutes).InclusiveBetween(OcppAlertThresholds.FaultedMin, OcppAlertThresholds.FaultedMax).When(x => x.FaultedMinutes != null);
        RuleFor(x => x.LongSessionMinutes).InclusiveBetween(OcppAlertThresholds.LongSessionMin, OcppAlertThresholds.LongSessionMax).When(x => x.LongSessionMinutes != null);
        RuleFor(x => x.ParkedMinutes).InclusiveBetween(OcppAlertThresholds.ParkedMin, OcppAlertThresholds.ParkedMax).When(x => x.ParkedMinutes != null);
    }
}

public class SetOcppAlertThresholdsCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<SetOcppAlertThresholdsCommand, OcppAlertThresholdsDto>
{
    public async Task<OcppAlertThresholdsDto> Handle(SetOcppAlertThresholdsCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var cp = await db.ChargingPoints.FirstOrDefaultAsync(c => c.Id == request.ChargingPointId && !c.IsDeleted, cancellationToken)
                 ?? throw new NotFoundException("cannot find charging point with id: " + request.ChargingPointId);

        cp.OcppOfflineAlertMin = request.OfflineMinutes;
        cp.OcppFaultedAlertMin = request.FaultedMinutes;
        cp.OcppLongSessionAlertMin = request.LongSessionMinutes;
        cp.OcppParkedAlertMin = request.ParkedMinutes;
        await db.SaveChanges(cancellationToken);

        return OcppAlertThresholds.Build(cp.Id, cp.OcppOfflineAlertMin, cp.OcppFaultedAlertMin, cp.OcppLongSessionAlertMin, cp.OcppParkedAlertMin);
    }
}
