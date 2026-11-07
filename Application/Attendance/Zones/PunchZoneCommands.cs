using Application.Attendance.Punching;
using Application.Common.Interfaces;
using Cable.Core;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Attendance.Zones;

public record ZoneWorkerDto(int ProviderManagerId, string? Name);

public record PunchZoneDto(int Id, string ProviderType, int ProviderId, string Name, double Lat, double Lng, int RadiusMeters, bool IsActive,
    List<ZoneWorkerDto> Workers,
    /// <summary>A zone with no workers applies to nobody (decision 8) — shown as "disabled".</summary>
    bool AppliesToNobody,
    /// <summary>The radius warnings of §3.6 (under 120 m / over 400 m), ready to show.</summary>
    string? Warning);

internal static class ZoneDtos
{
    public static string? Warning(int radius) =>
        radius < 120 ? Lang.Pick("دقة GPS داخل المباني ±60 م — ممكن ترفض موظف واقف عند الشاحن", "GPS accuracy indoors is ±60 m — a worker standing at the charger may be refused")
        : radius > 400 ? Lang.Pick("بتغطي الحارة — البيت ممكن ينحسب دوام", "This covers the neighbourhood — home may count as work")
        : null;

    public static async Task<PunchZoneDto> LoadAsync(IApplicationDbContext db, int zoneId, CancellationToken ct)
    {
        var z = await db.PunchZones.AsNoTracking().Include(x => x.Workers).ThenInclude(w => w.ProviderManager).ThenInclude(p => p.User)
                    .FirstOrDefaultAsync(x => x.Id == zoneId && !x.IsDeleted, ct)
                ?? throw new NotFoundException($"Punch zone {zoneId} not found");
        return ToDto(z);
    }

    public static PunchZoneDto ToDto(PunchZone z)
    {
        var workers = z.Workers.Where(w => !w.IsDeleted && !w.ProviderManager.IsDeleted)
            .Select(w => new ZoneWorkerDto(w.ProviderManagerId, w.ProviderManager.User?.Name)).ToList();
        return new PunchZoneDto(z.Id, z.ProviderType, z.ProviderId, z.Name, z.Lat, z.Lng, z.RadiusMeters, z.IsActive, workers, workers.Count == 0, Warning(z.RadiusMeters));
    }
}

/// <summary>Owner or admin: the station's zones with their workers.</summary>
public record GetPunchZonesRequest(string ProviderType, int ProviderId) : IRequest<List<PunchZoneDto>>;

public class GetPunchZonesRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<GetPunchZonesRequest, List<PunchZoneDto>>
{
    public async Task<List<PunchZoneDto>> Handle(GetPunchZonesRequest request, CancellationToken cancellationToken)
    {
        await AttendanceAccess.EnsureOwnerOrAdminAsync(db, currentUser, request.ProviderType, request.ProviderId, cancellationToken);
        var zones = await db.PunchZones.AsNoTracking().Include(x => x.Workers).ThenInclude(w => w.ProviderManager).ThenInclude(p => p.User)
            .Where(z => z.ProviderType == request.ProviderType && z.ProviderId == request.ProviderId && !z.IsDeleted)
            .OrderBy(z => z.Id).ToListAsync(cancellationToken);
        return zones.Select(ZoneDtos.ToDto).ToList();
    }
}

/// <summary>Owner: create (Id null) or edit a zone. Radius 50–800 m; workerIds must be workers of this station.</summary>
public record SavePunchZoneCommand(int? Id, string ProviderType, int ProviderId, string Name, double Lat, double Lng, int RadiusMeters,
    List<int>? WorkerIds = null, bool? IsActive = null) : IRequest<PunchZoneDto>;

public class SavePunchZoneCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<SavePunchZoneCommand, PunchZoneDto>
{
    public async Task<PunchZoneDto> Handle(SavePunchZoneCommand request, CancellationToken cancellationToken)
    {
        await AttendanceAccess.EnsureOwnerOrAdminAsync(db, currentUser, request.ProviderType, request.ProviderId, cancellationToken);
        await AttendanceAccess.EnsurePlanAsync(db, request.ProviderType, request.ProviderId, cancellationToken);
        var settings = await AttendanceSettings.ReadAsync(db, cancellationToken);

        if (string.IsNullOrWhiteSpace(request.Name)) throw new DataValidationException("Name", Lang.Pick("اسم المنطقة مطلوب", "The zone name is required"));
        if (request.Lat is < -90 or > 90 || request.Lng is < -180 or > 180 || (request.Lat == 0 && request.Lng == 0))
            throw new DataValidationException("Lat", Lang.Pick("الموقع غير صحيح", "Invalid position"));
        if (request.RadiusMeters < settings.MinRadiusMeters || request.RadiusMeters > settings.MaxRadiusMeters)
            throw new DataValidationException("RadiusMeters", Lang.Pick($"نصف القطر بين {settings.MinRadiusMeters} و{settings.MaxRadiusMeters} م", $"Radius must be between {settings.MinRadiusMeters} and {settings.MaxRadiusMeters} m"));

        PunchZone zone;
        if (request.Id is int id)
        {
            zone = await db.PunchZones.Include(z => z.Workers).FirstOrDefaultAsync(z => z.Id == id && !z.IsDeleted, cancellationToken)
                   ?? throw new NotFoundException($"Punch zone {id} not found");
            if (zone.ProviderType != request.ProviderType || zone.ProviderId != request.ProviderId)
                throw new ForbiddenAccessException("This zone belongs to another provider.");
        }
        else
        {
            zone = new PunchZone { ProviderType = request.ProviderType, ProviderId = request.ProviderId };
            db.PunchZones.Add(zone);
        }
        zone.Name = request.Name.Trim();
        zone.Lat = request.Lat; zone.Lng = request.Lng; zone.RadiusMeters = request.RadiusMeters;
        if (request.IsActive is bool active) zone.IsActive = active;
        if (request.WorkerIds is not null) await ZoneWorkers.ApplyAsync(db, zone, request.WorkerIds, cancellationToken);
        await db.SaveChanges(cancellationToken);
        return await ZoneDtos.LoadAsync(db, zone.Id, cancellationToken);
    }
}

internal static class ZoneWorkers
{
    public static async Task ApplyAsync(IApplicationDbContext db, PunchZone zone, List<int> workerIds, CancellationToken ct)
    {
        var ids = workerIds.Distinct().ToList();
        var valid = await db.ProviderManagers.AsNoTracking()
            .Where(pm => ids.Contains(pm.Id) && pm.ProviderType == zone.ProviderType && pm.ProviderId == zone.ProviderId && !pm.IsDeleted)
            .Select(pm => pm.Id).ToListAsync(ct);
        if (valid.Count != ids.Count)
            throw new DataValidationException("WorkerIds", Lang.Pick("بعض الموظفين مش من هاي المحطة", "Some workers are not workers of this provider"));
        foreach (var w in zone.Workers.Where(w => !w.IsDeleted && !ids.Contains(w.ProviderManagerId))) w.IsDeleted = true;
        foreach (var id in ids)
        {
            var row = zone.Workers.FirstOrDefault(w => w.ProviderManagerId == id);
            if (row is null) zone.Workers.Add(new PunchZoneWorker { ProviderManagerId = id });
            else row.IsDeleted = false;
        }
    }
}

public record SetPunchZoneWorkersCommand(int Id, List<int> WorkerIds) : IRequest<PunchZoneDto>;

public class SetPunchZoneWorkersCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<SetPunchZoneWorkersCommand, PunchZoneDto>
{
    public async Task<PunchZoneDto> Handle(SetPunchZoneWorkersCommand request, CancellationToken cancellationToken)
    {
        var zone = await db.PunchZones.Include(z => z.Workers).FirstOrDefaultAsync(z => z.Id == request.Id && !z.IsDeleted, cancellationToken)
                   ?? throw new NotFoundException($"Punch zone {request.Id} not found");
        await AttendanceAccess.EnsureOwnerOrAdminAsync(db, currentUser, zone.ProviderType, zone.ProviderId, cancellationToken);
        await ZoneWorkers.ApplyAsync(db, zone, request.WorkerIds ?? [], cancellationToken);
        await db.SaveChanges(cancellationToken);
        return await ZoneDtos.LoadAsync(db, zone.Id, cancellationToken);
    }
}

public record DeletePunchZoneCommand(int Id) : IRequest;

public class DeletePunchZoneCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<DeletePunchZoneCommand>
{
    public async Task Handle(DeletePunchZoneCommand request, CancellationToken cancellationToken)
    {
        var zone = await db.PunchZones.FirstOrDefaultAsync(z => z.Id == request.Id && !z.IsDeleted, cancellationToken)
                   ?? throw new NotFoundException($"Punch zone {request.Id} not found");
        await AttendanceAccess.EnsureOwnerOrAdminAsync(db, currentUser, zone.ProviderType, zone.ProviderId, cancellationToken);
        zone.IsDeleted = true;
        zone.IsActive = false;
        await db.SaveChanges(cancellationToken);
    }
}

/// <summary>"Try from where you stand" (§3.6): the distance and what the verdict would be — nothing is recorded.</summary>
public record TestPunchZoneRequest(string ProviderType, int ProviderId, double Lat, double Lng, int AccuracyMeters, bool IsMocked = false) : IRequest<ZoneTestDto>;

public record ZoneTestDto(int? ZoneId, string? ZoneName, int? DistanceMeters, int? RadiusMeters, string WouldBe, string? Reason, string Message);

public class TestPunchZoneRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<TestPunchZoneRequest, ZoneTestDto>
{
    public async Task<ZoneTestDto> Handle(TestPunchZoneRequest request, CancellationToken cancellationToken)
    {
        await AttendanceAccess.EnsureOwnerOrAdminAsync(db, currentUser, request.ProviderType, request.ProviderId, cancellationToken);
        var settings = await AttendanceSettings.ReadAsync(db, cancellationToken);
        var zones = await db.PunchZones.AsNoTracking()
            .Where(z => z.ProviderType == request.ProviderType && z.ProviderId == request.ProviderId && z.IsActive && !z.IsDeleted)
            .Select(z => new ZoneCandidate(z.Id, z.Name, z.Lat, z.Lng, z.RadiusMeters)).ToListAsync(cancellationToken);
        var check = ZoneRules.Evaluate(zones, request.Lat, request.Lng, request.AccuracyMeters, request.IsMocked, settings);
        return new ZoneTestDto(check.ZoneId, check.ZoneName, check.DistanceMeters, check.RadiusMeters, check.Verdict, check.Reason,
            PunchTexts.VerdictMessage(check, settings));
    }
}

public record PunchAttemptDto(int PunchId, int ProviderManagerId, string? WorkerName, string Direction, DateTime At, string Verdict, string? Reason,
    int? DistanceMeters, int? AccuracyMeters, bool IsMocked, string? ZoneName, int? AttendanceDayId, string? DeviceId);

/// <summary>Owner, admin or ViewAttendance: refused and review punches (§3.8 "rejected attempts").</summary>
public record GetPunchAttemptsRequest(string ProviderType, int ProviderId, DateTime? From, DateTime? To, string? Verdict) : IRequest<List<PunchAttemptDto>>;

public class GetPunchAttemptsRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<GetPunchAttemptsRequest, List<PunchAttemptDto>>
{
    public async Task<List<PunchAttemptDto>> Handle(GetPunchAttemptsRequest request, CancellationToken cancellationToken)
    {
        await AttendanceAccess.EnsureCanViewAsync(db, currentUser, request.ProviderType, request.ProviderId, cancellationToken);
        var fromUtc = request.From is DateTime f ? JordanTime.ToUtc(f) : DateTime.UtcNow.AddDays(-30);
        var toUtc = request.To is DateTime t ? JordanTime.ToUtc(t) : DateTime.UtcNow.AddDays(1);
        var verdict = string.IsNullOrWhiteSpace(request.Verdict) ? null : request.Verdict.Trim().ToLowerInvariant();

        return await db.Punches.AsNoTracking()
            .Where(p => !p.IsDeleted && p.Verdict != PunchVerdict.Accepted && p.At >= fromUtc && p.At <= toUtc
                        && p.ProviderManager.ProviderType == request.ProviderType && p.ProviderManager.ProviderId == request.ProviderId
                        && (verdict == null || p.Verdict == verdict))
            .OrderByDescending(p => p.At)
            .Select(p => new PunchAttemptDto(p.Id, p.ProviderManagerId, p.ProviderManager.User.Name, p.Direction, p.At, p.Verdict, p.Reason,
                p.DistanceMeters, p.AccuracyMeters, p.IsMocked,
                db.PunchZones.Where(z => z.Id == p.PunchZoneId).Select(z => z.Name).FirstOrDefault(), p.AttendanceDayId, p.DeviceId))
            .Take(500)
            .ToListAsync(cancellationToken);
    }
}
