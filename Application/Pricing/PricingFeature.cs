using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Domain.Enitites;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Pricing;

// ---------------------------------------------------------------------------
// Time-of-use tariff — the backend as source of truth (PRICE_ALERTS_BE_SPEC §2–3).
// ---------------------------------------------------------------------------

public record TouWindowDto(string Key, int StartMin, int EndMin, string Tier, int PriceFils, string NameEn, string NameAr);

public record TouTariffDto(int Version, DateTime EffectiveFrom, string Timezone, string Currency, string Unit, List<TouWindowDto> Windows);

/// <summary>Public, anonymous: the active tariff. The app caches it and refreshes on every open.</summary>
public record GetTouTariffRequest : IRequest<TouTariffDto>;

public class GetTouTariffRequestHandler(IApplicationDbContext db) : IRequestHandler<GetTouTariffRequest, TouTariffDto>
{
    public async Task<TouTariffDto> Handle(GetTouTariffRequest request, CancellationToken cancellationToken)
        => await TouTariffLoader.LoadAsync(db, cancellationToken)
           ?? throw new NotFoundException("No active time-of-use tariff is configured");
}

public static class TouTariffLoader
{
    public static async Task<TouTariffDto?> LoadAsync(IApplicationDbContext db, CancellationToken ct)
    {
        var t = await db.TouTariffs.AsNoTracking()
            .Where(x => x.IsActive && !x.IsDeleted)
            .OrderByDescending(x => x.Version)
            .Select(x => new
            {
                x.Version, x.EffectiveFrom, x.Timezone, x.Currency, x.Unit,
                Windows = x.Windows.OrderBy(w => w.SortOrder).Select(w => new TouWindowDto(w.Key, w.StartMin, w.EndMin, w.Tier, w.PriceFils, w.NameEn, w.NameAr)).ToList(),
            })
            .FirstOrDefaultAsync(ct);
        return t is null ? null : new TouTariffDto(t.Version, t.EffectiveFrom, t.Timezone, t.Currency, t.Unit, t.Windows);
    }
}

/// <summary>Admin: replace the windows (and optionally the effective date); the version increases by one so the apps reschedule.</summary>
public record UpdateTouTariffCommand(List<TouWindowDto> Windows, DateTime? EffectiveFrom, string? Note) : IRequest<TouTariffDto>;

public class UpdateTouTariffCommandValidator : AbstractValidator<UpdateTouTariffCommand>
{
    public UpdateTouTariffCommandValidator()
    {
        RuleFor(x => x.Windows).NotEmpty().Must(w => w.Select(x => x.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() == w.Count).WithMessage("Window keys must be unique");
        RuleFor(x => x.Windows).Must(w => w.Sum(x => x.EndMin - x.StartMin) == 1440).WithMessage("The windows must cover exactly 24 hours (1440 minutes) in total");
        RuleForEach(x => x.Windows).ChildRules(w =>
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

public class UpdateTouTariffCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<UpdateTouTariffCommand, TouTariffDto>
{
    public async Task<TouTariffDto> Handle(UpdateTouTariffCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);

        var current = await db.TouTariffs.Include(t => t.Windows)
            .Where(t => t.IsActive && !t.IsDeleted).OrderByDescending(t => t.Version)
            .FirstOrDefaultAsync(cancellationToken);

        var next = new TouTariff
        {
            Version = (current?.Version ?? 0) + 1,
            // The admin types a Jordan wall-clock date; stored as UTC like every other datetime (the API's JSON converter shows Jordan time).
            EffectiveFrom = request.EffectiveFrom is DateTime ef ? JordanTime.ToUtc(ef) : DateTime.UtcNow,
            Timezone = current?.Timezone ?? "Asia/Amman",
            Currency = current?.Currency ?? "JOD",
            Unit = current?.Unit ?? "fils/kWh",
            IsActive = true,
            Note = request.Note,
        };
        var order = 0;
        foreach (var w in request.Windows.OrderBy(x => x.StartMin))
            next.Windows.Add(new TouTariffWindow { Key = w.Key, NameEn = w.NameEn, NameAr = w.NameAr, StartMin = w.StartMin, EndMin = w.EndMin, Tier = w.Tier, PriceFils = w.PriceFils, SortOrder = ++order });

        if (current is not null) current.IsActive = false;   // history kept; only one active row
        db.TouTariffs.Add(next);
        await db.SaveChanges(cancellationToken);

        return (await TouTariffLoader.LoadAsync(db, cancellationToken))!;
    }
}

// ---------------------------------------------------------------------------
// User preferences (§3.2–3.3)
// ---------------------------------------------------------------------------

public record PriceAlertPrefsDto(bool Enabled, int LeadMinutes, List<string> Windows);

public static class PriceAlertRules
{
    public static readonly int[] LeadMinutesAllowed = [15, 30, 45, 60];
}

public record GetMyPriceAlertsRequest : IRequest<PriceAlertPrefsDto>;

public class GetMyPriceAlertsRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<GetMyPriceAlertsRequest, PriceAlertPrefsDto>
{
    public async Task<PriceAlertPrefsDto> Handle(GetMyPriceAlertsRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var row = await db.UserPriceAlerts.AsNoTracking().FirstOrDefaultAsync(x => x.UserAccountId == userId, cancellationToken);
        return row is null
            ? new PriceAlertPrefsDto(false, 15, [])
            : new PriceAlertPrefsDto(row.IsEnabled, row.LeadMinutes, PriceAlertWindows.Parse(row.Windows));
    }
}

public record SetMyPriceAlertsCommand(bool Enabled, int LeadMinutes, List<string> Windows) : IRequest<PriceAlertPrefsDto>;

public class SetMyPriceAlertsCommandValidator : AbstractValidator<SetMyPriceAlertsCommand>
{
    public SetMyPriceAlertsCommandValidator()
    {
        RuleFor(x => x.LeadMinutes).Must(m => PriceAlertRules.LeadMinutesAllowed.Contains(m)).WithMessage("leadMinutes must be 15, 30, 45 or 60");
        RuleFor(x => x.Windows).NotNull().Must(w => w.Count <= 20);
    }
}

public class SetMyPriceAlertsCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<SetMyPriceAlertsCommand, PriceAlertPrefsDto>
{
    public async Task<PriceAlertPrefsDto> Handle(SetMyPriceAlertsCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");

        var tariff = await TouTariffLoader.LoadAsync(db, cancellationToken) ?? throw new NotFoundException("No active time-of-use tariff is configured");
        var known = tariff.Windows.Select(w => w.Key).ToHashSet(StringComparer.Ordinal);
        var unknown = request.Windows.Where(k => !known.Contains(k)).ToList();
        if (unknown.Count > 0)
            throw new DataValidationException(nameof(request.Windows), "Unknown window key(s): " + string.Join(", ", unknown) + ". Valid: " + string.Join(", ", known));

        var row = await db.UserPriceAlerts.FirstOrDefaultAsync(x => x.UserAccountId == userId, cancellationToken);
        if (row is null) { row = new UserPriceAlert { UserAccountId = userId }; db.UserPriceAlerts.Add(row); }
        row.IsEnabled = request.Enabled;
        row.LeadMinutes = request.LeadMinutes;
        row.Windows = PriceAlertWindows.Serialize(request.Windows);
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChanges(cancellationToken);

        return new PriceAlertPrefsDto(row.IsEnabled, row.LeadMinutes, PriceAlertWindows.Parse(row.Windows));
    }
}

public static class PriceAlertWindows
{
    public static List<string> Parse(string csv) => csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
    public static string Serialize(IEnumerable<string> keys) => string.Join(",", keys.Select(k => k.Trim()).Where(k => k.Length > 0).Distinct());
}

// ---------------------------------------------------------------------------
// Quiet hours (§6) and the alert plan — shared by the job and the admin preview
// ---------------------------------------------------------------------------

public static class PriceAlertQuietHours
{
    public const string FromKey = "PriceAlerts.QuietFrom";
    public const string ToKey = "PriceAlerts.QuietTo";
    public static readonly TimeOnly DefaultFrom = new(23, 30);
    public static readonly TimeOnly DefaultTo = new(6, 30);

    public static async Task<(TimeOnly From, TimeOnly To)> GetAsync(IApplicationDbContext db, CancellationToken ct)
    {
        var rows = await db.AppSettings.AsNoTracking()
            .Where(s => (s.Key == FromKey || s.Key == ToKey) && !s.IsDeleted)
            .Select(s => new { s.Key, s.Value }).ToListAsync(ct);
        var from = TimeOnly.TryParse(rows.FirstOrDefault(r => r.Key == FromKey)?.Value, out var f) ? f : DefaultFrom;
        var to = TimeOnly.TryParse(rows.FirstOrDefault(r => r.Key == ToKey)?.Value, out var t) ? t : DefaultTo;
        return (from, to);
    }

    /// <summary>True when the wall-clock time falls inside the quiet span (which may cross midnight).</summary>
    public static bool IsQuiet(TimeOnly t, TimeOnly from, TimeOnly to) =>
        from <= to ? t >= from && t < to : t >= from || t < to;
}

/// <summary>One alert the job would (or did) send: which window, for which Jordan date, at which Jordan time, with which lead.</summary>
public record PlannedPriceAlert(string WindowKey, DateOnly AlertDate, DateTime AlertAtLocal, DateTime WindowStartLocal, int LeadMinutes, bool Quiet, TouWindowDto Window, TouWindowDto Current);

public static class PriceAlertPlanner
{
    /// <summary>
    /// Alerts whose moment falls in (nowLocal − lookBack, nowLocal]. A ten-minute look-back (the
    /// job runs every five) tolerates a late run; the log's unique index prevents duplicates.
    /// </summary>
    public static List<PlannedPriceAlert> Due(TouTariffDto tariff, DateTime nowLocal, TimeSpan lookBack, (TimeOnly From, TimeOnly To) quiet)
    {
        var due = new List<PlannedPriceAlert>();
        foreach (var w in tariff.Windows)
        foreach (var lead in PriceAlertRules.LeadMinutesAllowed)
        {
            // The window starts once per day at StartMin; consider yesterday, today and tomorrow so a
            // lead that crosses midnight (e.g. 05:00 − 60 min on the previous day) is still found.
            for (var dayOffset = -1; dayOffset <= 1; dayOffset++)
            {
                var date = DateOnly.FromDateTime(nowLocal).AddDays(dayOffset);
                var windowStart = date.ToDateTime(TimeOnly.MinValue).AddMinutes(w.StartMin % 1440);
                var alertAt = windowStart.AddMinutes(-lead);
                if (alertAt <= nowLocal - lookBack || alertAt > nowLocal) continue;

                var current = tariff.Windows.FirstOrDefault(x => Contains(x, alertAt)) ?? w;
                due.Add(new PlannedPriceAlert(w.Key, DateOnly.FromDateTime(windowStart), alertAt, windowStart, lead,
                    PriceAlertQuietHours.IsQuiet(TimeOnly.FromDateTime(alertAt), quiet.From, quiet.To), w, current));
            }
        }
        return due;
    }

    /// <summary>Is this wall-clock moment inside the window (handles windows that cross midnight).</summary>
    public static bool Contains(TouWindowDto w, DateTime local)
    {
        var m = local.Hour * 60 + local.Minute;
        return w.EndMin <= 1440 ? m >= w.StartMin && m < w.EndMin
                                : m >= w.StartMin || m < w.EndMin - 1440;
    }
}

/// <summary>§5.1–5.2: the notification text. Prices come from the tariff at send time, never from a resource file.</summary>
public static class PriceAlertComposer
{
    public static (string Title, string Body) Compose(PlannedPriceAlert a, string language)
    {
        var ar = !string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);
        var drops = a.Window.PriceFils < a.Current.PriceFils;
        var emoji = drops ? " 🔋" : " ⚡";
        var name = ar ? a.Window.NameAr : a.Window.NameEn;
        if (ar)
        {
            var when = a.LeadMinutes == 60 ? "ساعة" : a.LeadMinutes + " دقيقة";
            return ("باقي " + when + " على " + name, "السعر رح " + (drops ? "ينزل" : "يرتفع") + " لـ " + a.Window.PriceFils + " فلس" + emoji);
        }
        var inWhen = a.LeadMinutes == 60 ? "1 hour" : a.LeadMinutes + " min";
        return (name + " in " + inWhen, "Price " + (drops ? "drops" : "rises") + " to " + a.Window.PriceFils + " fils" + emoji);
    }
}

// ---------------------------------------------------------------------------
// Admin preview: who would be alerted at a given Jordan time — nothing is sent
// ---------------------------------------------------------------------------

public record PriceAlertPreviewItemDto(string WindowKey, DateOnly AlertDate, DateTime AlertAtLocal, DateTime WindowStartLocal, int LeadMinutes, bool Quiet, int Subscribers, int AlreadySent, int WithToken, string TitleAr, string BodyAr, string TitleEn, string BodyEn);

public record PreviewPriceAlertsRequest(DateTime? AtJordanLocal, int LookBackMinutes = 10) : IRequest<List<PriceAlertPreviewItemDto>>;

public class PreviewPriceAlertsRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<PreviewPriceAlertsRequest, List<PriceAlertPreviewItemDto>>
{
    public async Task<List<PriceAlertPreviewItemDto>> Handle(PreviewPriceAlertsRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var tariff = await TouTariffLoader.LoadAsync(db, cancellationToken) ?? throw new NotFoundException("No active time-of-use tariff is configured");
        var quiet = await PriceAlertQuietHours.GetAsync(db, cancellationToken);
        var nowLocal = request.AtJordanLocal ?? JordanTime.FromUtc(DateTime.UtcNow);

        var result = new List<PriceAlertPreviewItemDto>();
        foreach (var a in PriceAlertPlanner.Due(tariff, nowLocal, TimeSpan.FromMinutes(Math.Clamp(request.LookBackMinutes, 1, 1440)), quiet))
        {
            var subscribers = await db.UserPriceAlerts.AsNoTracking()
                .Where(p => p.IsEnabled && p.LeadMinutes == a.LeadMinutes && ("," + p.Windows + ",").Contains("," + a.WindowKey + ","))
                .Select(p => p.UserAccountId).ToListAsync(cancellationToken);
            var sent = await db.PriceAlertLogs.AsNoTracking()
                .CountAsync(l => l.WindowKey == a.WindowKey && l.AlertDate == a.AlertDate && subscribers.Contains(l.UserAccountId), cancellationToken);
            var withToken = await db.NotificationTokens.AsNoTracking()
                .Where(t => subscribers.Contains(t.UserId) && t.AppType == Cable.Core.Enums.FirebaseAppType.UserApp)
                .Select(t => t.UserId).Distinct().CountAsync(cancellationToken);
            var (tAr, bAr) = PriceAlertComposer.Compose(a, "ar");
            var (tEn, bEn) = PriceAlertComposer.Compose(a, "en");
            // Local wall-clock values go out as UTC so the API's Jordan-time JSON converter shows them unchanged.
            result.Add(new PriceAlertPreviewItemDto(a.WindowKey, a.AlertDate, JordanTime.ToUtc(a.AlertAtLocal), JordanTime.ToUtc(a.WindowStartLocal), a.LeadMinutes, a.Quiet, subscribers.Count, sent, withToken, tAr, bAr, tEn, bEn));
        }
        return result;
    }
}
