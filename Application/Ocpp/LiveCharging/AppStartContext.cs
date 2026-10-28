using Application.Common.Interfaces;
using Cable.Core;
using Cable.Core.Constants;
using Cable.Core.Exceptions;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.LiveCharging;

/// <summary>
/// What the driver's app start knows before the session exists — the phone's position and distance to the
/// station (4.2 / 11.c) and the car (13.4). RemoteStartTransaction only opens the session later, in
/// Cable.Ocpp's StartTransaction, so the context is parked on the plug exactly like the pending charge limit
/// and taken by that driver's App session on that plug within OcppVirtualTag.RemoteStartAuthorizeWindow.
/// </summary>
public static class OcppPendingStart
{
    public sealed record Context(int UserId, double? Lat, double? Lng, int? AccuracyMeters, int? DistanceMeters, int? CarId);

    public static async Task SetAsync(IApplicationDbContext db, int ocppChargePointId, int connectorId, Context c, CancellationToken ct)
    {
        var plug = await db.OcppConnectors.FirstAsync(x => x.OcppChargePointId == ocppChargePointId && x.ConnectorId == connectorId, ct);
        plug.PendingStartUserId = c.UserId;
        plug.PendingStartSetAt = DateTime.UtcNow;
        plug.PendingStartLat = c.Lat;
        plug.PendingStartLng = c.Lng;
        plug.PendingStartAccuracyMeters = c.AccuracyMeters;
        plug.PendingStartDistanceMeters = c.DistanceMeters;
        plug.PendingCarId = c.CarId;
        await db.SaveChanges(ct);
    }

    /// <summary>
    /// Cable.Ocpp, StartTransaction, before the new session is saved: an accepted App session of the same driver
    /// takes the parked context. A stale context (older than the window) is cleared; a fresh one that belongs to
    /// another driver is left for its own session. The caller's SaveChanges persists both rows.
    /// </summary>
    public static async Task ApplyAsync(IApplicationDbContext db, int ocppChargePointId, OcppTransaction tx, DateTime now, CancellationToken ct)
    {
        if (tx.WasRejected || tx.StartSource != OcppStartSource.App || tx.StartedByUserId is null) return;

        var plug = await db.OcppConnectors.FirstOrDefaultAsync(c => c.OcppChargePointId == ocppChargePointId
            && c.ConnectorId == tx.ConnectorId && c.PendingStartUserId != null, ct);
        if (plug is null) return;

        var fresh = plug.PendingStartSetAt >= now - OcppVirtualTag.RemoteStartAuthorizeWindow;
        if (fresh && plug.PendingStartUserId != tx.StartedByUserId) return;

        if (fresh)
        {
            tx.StartLat = plug.PendingStartLat;
            tx.StartLng = plug.PendingStartLng;
            tx.StartAccuracyMeters = plug.PendingStartAccuracyMeters;
            tx.StartDistanceMeters = plug.PendingStartDistanceMeters;
            tx.CarId = plug.PendingCarId;
        }

        plug.PendingStartUserId = null;
        plug.PendingStartSetAt = null;
        plug.PendingStartLat = null;
        plug.PendingStartLng = null;
        plug.PendingStartAccuracyMeters = null;
        plug.PendingStartDistanceMeters = null;
        plug.PendingCarId = null;
    }
}

/// <summary>4.2: the server-side proximity check of an app start, with the settings in force.</summary>
public static class AppStartProximity
{
    /// <returns>The evaluation (distance for the session record) when the start may go ahead.</returns>
    /// <exception cref="UnprocessableException">422 LOCATION_REQUIRED (no position, required) or TOO_FAR_FROM_STATION (details distanceMeters, maxMeters).</exception>
    public static async Task<ProximityRule.Result> EnsureNearAsync(IApplicationDbContext db, int chargingPointId,
        double? lat, double? lng, double? accuracyMeters, CancellationToken ct)
    {
        var settings = await Application.Settings.AppSettingsProvider.GetOcppAppStartAsync(db, ct);
        var station = await db.ChargingPoints.AsNoTracking()
            .Where(c => c.Id == chargingPointId && !c.IsDeleted)
            .Select(c => new { c.Latitude, c.Longitude })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("cannot find charging point " + chargingPointId);

        var result = ProximityRule.Evaluate(station.Latitude, station.Longitude, lat, lng, accuracyMeters,
            settings.MaxDistanceMeters, settings.RequireLocation);

        return result.Outcome switch
        {
            ProximityRule.Outcome.LocationMissing => throw new UnprocessableException(ProximityRule.LocationRequiredCode,
                LiveChargingTexts.LocationRequired(),
                new Dictionary<string, object?> { ["maxMeters"] = result.MaxMeters }),
            ProximityRule.Outcome.TooFar => throw new UnprocessableException(ProximityRule.TooFarCode,
                LiveChargingTexts.TooFar(result.DistanceMeters ?? 0, result.MaxMeters),
                new Dictionary<string, object?> { ["distanceMeters"] = result.DistanceMeters, ["maxMeters"] = result.MaxMeters }),
            _ => result,
        };
    }
}

/// <summary>13.4: the driver's car on a session.</summary>
public record SessionCarDto(int Id, string Name, bool? ReportsSoc = null);

public static class SessionCars
{
    /// <summary>"Hyundai Ioniq 5" — the car type (brand) and the model.</summary>
    public static string NameOf(string? carType, string? model) =>
        string.Join(" ", new[] { carType?.Trim(), model?.Trim() }.Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>
    /// The car for an app start: the given one must be the driver's own; none given → their only car (a driver
    /// with several cars and no choice gets none rather than a guess).
    /// </summary>
    public static async Task<int?> ResolveForStartAsync(IApplicationDbContext db, int userId, int? carId, CancellationToken ct)
    {
        if (carId is int id)
        {
            var mine = await db.UserCars.AsNoTracking().AnyAsync(c => c.Id == id && c.UserId == userId && !c.IsDeleted, ct);
            if (!mine) throw new DataValidationException("CarId", LiveChargingTexts.CarNotFound());
            return id;
        }

        var cars = await db.UserCars.AsNoTracking()
            .Where(c => c.UserId == userId && !c.IsDeleted)
            .Select(c => c.Id).Take(2).ToListAsync(ct);
        return cars.Count == 1 ? cars[0] : null;
    }

    public static async Task<Dictionary<int, SessionCarDto>> LoadAsync(IApplicationDbContext db, IEnumerable<int?> carIds, CancellationToken ct)
    {
        var ids = carIds.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, SessionCarDto>();
        var rows = await db.UserCars.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, Type = c.CarModel.CarType.Name, Model = c.CarModel.Name, c.CarModel.ReportsSoc })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => new SessionCarDto(r.Id, NameOf(r.Type, r.Model), r.ReportsSoc));
    }
}

/// <summary>
/// 13.4 learning: once a session of a car reported a battery %, its model is marked ReportsSoc = true, so the
/// app learns over time which cars support the battery-% limit instead of a hand-kept list. Called by Cable.Ocpp
/// when the session closes (SocStartPercent is set from the readings by then).
/// </summary>
public static class CarSocLearning
{
    public static async Task<int> MarkAsync(IApplicationDbContext db, int transactionId, CancellationToken ct)
    {
        var modelId = await db.OcppTransactions.AsNoTracking()
            .Where(t => t.Id == transactionId && t.CarId != null && (t.SocStartPercent != null || t.SocEndPercent != null))
            .Join(db.UserCars, t => t.CarId, c => (int?)c.Id, (t, c) => (int?)c.CarModelId)
            .FirstOrDefaultAsync(ct);
        if (modelId is null) return 0;

        return await db.CarModels
            .Where(m => m.Id == modelId && m.ReportsSoc != true)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReportsSoc, (bool?)true), ct);
    }
}
