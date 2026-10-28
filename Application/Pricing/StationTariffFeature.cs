using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Constants;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Domain.Enitites;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Pricing;

// ---------------------------------------------------------------------------
// 4.4 — a station's real price. A station may sell at its own time-of-use tariff
// (same windows shape as the national one); its sessions are priced with it —
// live cost so far, the final price, the JOD charge limit, the driver screens.
// No own tariff = the national tariff, exactly as before. Stored as TouTariff rows
// with ChargingPointId set; versions count per station; one active row.
// ---------------------------------------------------------------------------

/// <summary>The tariff that prices a station's sessions and where it comes from.</summary>
/// <param name="StationId">Set when the station's own tariff applies; null = the national tariff.</param>
public sealed record EffectiveTariff(TouTariffDto Tariff, int? StationId)
{
    public string Source => StationId is null ? StationTariffs.SourceNational : StationTariffs.SourceStation;
}

public static class StationTariffs
{
    public const string SourceNational = "national";
    public const string SourceStation = "station";

    /// <summary>The station's own active tariff, or null.</summary>
    public static async Task<TouTariffDto?> LoadOwnAsync(IApplicationDbContext db, int chargingPointId, CancellationToken ct)
    {
        var t = await db.TouTariffs.AsNoTracking()
            .Where(x => x.ChargingPointId == chargingPointId && x.IsActive && !x.IsDeleted)
            .OrderByDescending(x => x.Version)
            .Select(x => new
            {
                x.Version, x.EffectiveFrom, x.Timezone, x.Currency, x.Unit,
                Windows = x.Windows.OrderBy(w => w.SortOrder).Select(w => new TouWindowDto(w.Key, w.StartMin, w.EndMin, w.Tier, w.PriceFils, w.NameEn, w.NameAr)).ToList(),
            })
            .FirstOrDefaultAsync(ct);
        return t is null || t.Windows.Count == 0 ? null : new TouTariffDto(t.Version, t.EffectiveFrom, t.Timezone, t.Currency, t.Unit, t.Windows);
    }

    /// <summary>The station's own tariff, else the national one (<paramref name="national"/> when the caller already loaded it).</summary>
    public static async Task<EffectiveTariff?> LoadEffectiveAsync(IApplicationDbContext db, int chargingPointId, CancellationToken ct, TouTariffDto? national = null)
    {
        var own = await LoadOwnAsync(db, chargingPointId, ct);
        if (own is not null) return new EffectiveTariff(own, chargingPointId);
        national ??= await TouTariffLoader.LoadAsync(db, ct);
        return national is null ? null : new EffectiveTariff(national, null);
    }

    /// <summary>Same, from a charger row id (OcppChargePoint.Id) — what OCPP code has at hand.</summary>
    public static async Task<EffectiveTariff?> LoadForChargerAsync(IApplicationDbContext db, int ocppChargePointId, CancellationToken ct, TouTariffDto? national = null)
    {
        var stationId = await db.OcppChargePoints.AsNoTracking()
            .Where(c => c.Id == ocppChargePointId)
            .Select(c => (int?)c.ChargingPointId)
            .FirstOrDefaultAsync(ct);
        if (stationId is int id) return await LoadEffectiveAsync(db, id, ct, national);
        national ??= await TouTariffLoader.LoadAsync(db, ct);
        return national is null ? null : new EffectiveTariff(national, null);
    }
}

// ---- DTOs -------------------------------------------------------------------

/// <summary>The window in force now and the next one, for "now: off-peak · 183 fils/kWh — peak from 5:00 pm".</summary>
public record TariffNowDto(
    string Key,
    string Tier,
    string NameEn,
    string NameAr,
    int PriceFils,
    /// <summary>When this window ends (= when <see cref="Next"/> starts).</summary>
    DateTime EndsAt,
    TouWindowDto? Next,
    string TextAr,
    string TextEn);

public record StationTariffDto(
    int ChargingPointId,
    /// <summary>station = the station's own tariff; national = the national tariff applies.</summary>
    string Source,
    bool HasOwnTariff,
    TouTariffDto Tariff,
    TariffNowDto? Now);

public static class StationTariffView
{
    public static StationTariffDto Build(int chargingPointId, EffectiveTariff effective, DateTime nowUtc)
    {
        var (window, endsAt) = SessionPricer.WindowSpan(effective.Tariff, nowUtc);
        TariffNowDto? now = null;
        if (window is not null)
        {
            var next = SessionPricer.WindowNow(effective.Tariff, endsAt.AddSeconds(1));
            now = new TariffNowDto(window.Key, window.Tier, window.NameEn, window.NameAr, window.PriceFils, endsAt,
                next is null || next.Key == window.Key ? null : next,
                $"الآن: {window.NameAr} · {window.PriceFils} فلس/kWh",
                $"Now: {window.NameEn} · {window.PriceFils} fils/kWh");
        }
        return new StationTariffDto(chargingPointId, effective.Source, effective.StationId is not null, effective.Tariff, now);
    }

    public static async Task<StationTariffDto> LoadAsync(IApplicationDbContext db, int chargingPointId, CancellationToken ct)
    {
        var exists = await db.ChargingPoints.AsNoTracking().AnyAsync(c => c.Id == chargingPointId && !c.IsDeleted, ct);
        if (!exists) throw new NotFoundException("cannot find charging point " + chargingPointId);
        var effective = await StationTariffs.LoadEffectiveAsync(db, chargingPointId, ct)
                        ?? throw new NotFoundException("No active time-of-use tariff is configured");
        return Build(chargingPointId, effective, DateTime.UtcNow);
    }
}

/// <summary>The national tariff's window rules, shared by the station tariff (same shape).</summary>
public class TouWindowsValidator : AbstractValidator<List<TouWindowDto>>
{
    public TouWindowsValidator()
    {
        RuleFor(x => x).NotEmpty().Must(w => w.Select(x => x.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() == w.Count).WithMessage("Window keys must be unique");
        RuleFor(x => x).Must(w => w.Sum(x => x.EndMin - x.StartMin) == 1440).WithMessage("The windows must cover exactly 24 hours (1440 minutes) in total");
        RuleForEach(x => x).ChildRules(w =>
        {
            w.RuleFor(x => x.Key).NotEmpty().MaximumLength(30).Matches("^[A-Za-z][A-Za-z0-9_]*$");
            w.RuleFor(x => x.NameEn).NotEmpty().MaximumLength(60);
            w.RuleFor(x => x.NameAr).NotEmpty().MaximumLength(60);
            w.RuleFor(x => x.Tier).Must(t => t is "offPeak" or "partial" or "peak").WithMessage("Tier must be offPeak, partial or peak");
            w.RuleFor(x => x.StartMin).InclusiveBetween(0, 1439);
            w.RuleFor(x => x.EndMin).GreaterThan(x => x.StartMin).LessThanOrEqualTo(2879);
            w.RuleFor(x => x.PriceFils).InclusiveBetween(1, 9999);
        });
    }
}

// ---- Public (driver app) ----------------------------------------------------

/// <summary>Public: the tariff that prices this station's sessions and the window in force now.</summary>
public record GetStationTariffRequest(int ChargingPointId) : IRequest<StationTariffDto>;

public class GetStationTariffRequestHandler(IApplicationDbContext db) : IRequestHandler<GetStationTariffRequest, StationTariffDto>
{
    public Task<StationTariffDto> Handle(GetStationTariffRequest request, CancellationToken cancellationToken)
        => StationTariffView.LoadAsync(db, request.ChargingPointId, cancellationToken);
}

// ---- Owner (partner app), read-only ----------------------------------------

public record GetProviderStationTariffRequest(int ChargingPointId) : IRequest<StationTariffDto>;

public class GetProviderStationTariffRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetProviderStationTariffRequest, StationTariffDto>
{
    public async Task<StationTariffDto> Handle(GetProviderStationTariffRequest request, CancellationToken cancellationToken)
    {
        await ProviderAccessGuard.EnsureCanActForProviderAsync(db, currentUser, ProviderAccessGuard.ChargingPoint, request.ChargingPointId,
            cancellationToken, requiredPrivilege: WorkerPrivileges.ConnectView);
        return await StationTariffView.LoadAsync(db, request.ChargingPointId, cancellationToken);
    }
}

// ---- Admin ------------------------------------------------------------------

public record GetAdminStationTariffRequest(int ChargingPointId) : IRequest<StationTariffDto>;

public class GetAdminStationTariffRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetAdminStationTariffRequest, StationTariffDto>
{
    public async Task<StationTariffDto> Handle(GetAdminStationTariffRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        return await StationTariffView.LoadAsync(db, request.ChargingPointId, cancellationToken);
    }
}

/// <summary>Admin: give the station its own tariff (replaces the previous one; version + 1). Applies to sessions priced from now on.</summary>
public record SetStationTariffCommand(int ChargingPointId, List<TouWindowDto> Windows, string? Note) : IRequest<StationTariffDto>;

public class SetStationTariffCommandValidator : AbstractValidator<SetStationTariffCommand>
{
    public SetStationTariffCommandValidator()
    {
        RuleFor(x => x.ChargingPointId).GreaterThan(0);
        RuleFor(x => x.Windows).NotNull().SetValidator(new TouWindowsValidator());
        RuleFor(x => x.Note).MaximumLength(300);
    }
}

public class SetStationTariffCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<SetStationTariffCommand, StationTariffDto>
{
    public async Task<StationTariffDto> Handle(SetStationTariffCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var exists = await db.ChargingPoints.AsNoTracking().AnyAsync(c => c.Id == request.ChargingPointId && !c.IsDeleted, cancellationToken);
        if (!exists) throw new NotFoundException("cannot find charging point " + request.ChargingPointId);

        var rows = await db.TouTariffs.Where(t => t.ChargingPointId == request.ChargingPointId && !t.IsDeleted).ToListAsync(cancellationToken);
        var national = await TouTariffLoader.LoadAsync(db, cancellationToken);

        var next = new TouTariff
        {
            ChargingPointId = request.ChargingPointId,
            Version = (rows.Count == 0 ? 0 : rows.Max(r => r.Version)) + 1,
            EffectiveFrom = DateTime.UtcNow,
            Timezone = national?.Timezone ?? "Asia/Amman",
            Currency = national?.Currency ?? "JOD",
            Unit = national?.Unit ?? "fils/kWh",
            IsActive = true,
            Note = request.Note,
        };
        var order = 0;
        foreach (var w in request.Windows.OrderBy(x => x.StartMin))
            next.Windows.Add(new TouTariffWindow { Key = w.Key, NameEn = w.NameEn, NameAr = w.NameAr, StartMin = w.StartMin, EndMin = w.EndMin, Tier = w.Tier, PriceFils = w.PriceFils, SortOrder = ++order });

        foreach (var r in rows) r.IsActive = false;   // history kept; one active row per station
        db.TouTariffs.Add(next);
        await db.SaveChanges(cancellationToken);

        return await StationTariffView.LoadAsync(db, request.ChargingPointId, cancellationToken);
    }
}

/// <summary>Admin: drop the station's own tariff — its sessions are priced with the national tariff again.</summary>
public record RemoveStationTariffCommand(int ChargingPointId) : IRequest<StationTariffDto>;

public class RemoveStationTariffCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<RemoveStationTariffCommand, StationTariffDto>
{
    public async Task<StationTariffDto> Handle(RemoveStationTariffCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var rows = await db.TouTariffs.Where(t => t.ChargingPointId == request.ChargingPointId && t.IsActive && !t.IsDeleted).ToListAsync(cancellationToken);
        if (rows.Count == 0) throw new NotFoundException("This station has no own tariff");
        foreach (var r in rows) r.IsActive = false;
        await db.SaveChanges(cancellationToken);
        return await StationTariffView.LoadAsync(db, request.ChargingPointId, cancellationToken);
    }
}
