using Application.Attendance.Punching;
using Application.Attendance.Reports;
using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Constants;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Attendance.Manual;

/// <summary>
/// §3.8 — the owner, or a worker with ManageAttendance, starts or ends another worker's shift by hand ("his phone is
/// broken"). Time = now; a note is mandatory; never for oneself; the owner hears about it when a supervisor did it.
/// </summary>
public record ManualPunchCommand(string ProviderType, int ProviderId, int WorkerId, string Direction, string? Note) : IRequest<ManualPunchResult>;

public record ManualPunchResult(int PunchId, DateTime At, string Direction, string State, int WorkedMinutes, string Message);

public class ManualPunchCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, AttendanceNotifier notifier) : IRequestHandler<ManualPunchCommand, ManualPunchResult>
{
    public async Task<ManualPunchResult> Handle(ManualPunchCommand request, CancellationToken ct)
    {
        var callerId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var direction = (request.Direction ?? "").Trim().ToLowerInvariant();
        if (direction is not (PunchDirection.In or PunchDirection.Out)) throw new DataValidationException("Direction", "Direction must be 'in' or 'out'");
        if (string.IsNullOrWhiteSpace(request.Note))
            throw new CodedHttpException(422, AttendanceCodes.NoteRequired, Lang.Pick("الملاحظة إجبارية للتسجيل اليدوي", "A note is required for a manual entry"));

        // Who may: owner / admin, or a usable worker with ManageAttendance (never for themself).
        var isOwner = await AdminRoleGuard.IsAdminAsync(db, currentUser, ct) || await AttendanceAccess.IsOwnerAsync(db, request.ProviderType, request.ProviderId, callerId, ct);
        if (!isOwner) await AttendanceAccess.EnsureWorkerAsync(db, currentUser, request.ProviderType, request.ProviderId, WorkerPrivileges.ManageAttendance, ct);
        else await AttendanceAccess.EnsurePlanAsync(db, request.ProviderType, request.ProviderId, ct);

        var target = await db.ProviderManagers.AsNoTracking()
                         .FirstOrDefaultAsync(x => x.Id == request.WorkerId && x.ProviderType == request.ProviderType && x.ProviderId == request.ProviderId && !x.IsDeleted, ct)
                     ?? throw new NotFoundException($"Worker {request.WorkerId} not found at this provider");
        if (target.UserId == callerId && !isOwner)
            throw new CodedHttpException(403, AttendanceCodes.CannotRecordSelf, Lang.Pick("ما بتقدر تسجّل دوامك يدوياً — بصمتك لازم تكون من موبايلك", "You cannot record your own shift by hand — punch from your phone"));

        var settings = await AttendanceSettings.ReadAsync(db, ct);
        var schedules = await ShiftResolver.LoadAsync(db, target.Id, ct);

        Punch punch; AttendanceDay day;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.ProviderManagers.FromSqlRaw("SELECT * FROM [ProviderManager] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {0}", target.Id).AsNoTracking().FirstAsync(ct);
            var now = DateTime.UtcNow;
            var openDay = await db.AttendanceDays.Include(d => d.Punches).Where(d => d.ProviderManagerId == target.Id && d.IsOpen && !d.IsDeleted).OrderByDescending(d => d.WorkDate).FirstOrDefaultAsync(ct);
            var (workDate, shift) = ShiftResolver.ResolveForIn(schedules, now, settings);

            if (direction == PunchDirection.In)
            {
                if (openDay is not null && openDay.WorkDate == workDate)
                    throw new CodedHttpException(409, AttendanceCodes.AlreadyIn, Lang.Pick("الموظف مسجّل حضور أصلاً", "The worker is already punched in"));
                day = await db.AttendanceDays.Include(d => d.Punches).FirstOrDefaultAsync(d => d.ProviderManagerId == target.Id && d.WorkDate == workDate && !d.IsDeleted, ct)
                      ?? new AttendanceDay { ProviderManagerId = target.Id, WorkDate = workDate, ShiftStartUtc = shift?.StartUtc, ShiftEndUtc = shift?.EndUtc, Status = AttendanceStatus.Present };
                if (day.Id == 0) db.AttendanceDays.Add(day);
                if (await AttendanceAccess.IsHolidayAsync(db, workDate, ct)) day.Status = AttendanceStatus.Holiday;
            }
            else
            {
                day = openDay ?? throw new CodedHttpException(409, AttendanceCodes.NotIn, Lang.Pick("الموظف مش مسجّل حضور", "The worker is not punched in"));
            }

            punch = new Punch
            {
                ProviderManagerId = target.Id, WorkDate = day.WorkDate, Direction = direction, At = now, Verdict = PunchVerdict.Accepted,
                Source = PunchSource.Manual, RecordedByUserId = callerId, Note = request.Note.Trim(),
            };
            day.Punches.Add(punch);
            day.Flags = AttendanceFlags.Add(day.Flags, AttendanceFlag.Manual);
            AttendanceMath.Recompute(day, shift?.GraceMinutes ?? settings.DefaultGraceMinutes);
            await db.SaveChanges(ct);
            await tx.CommitAsync(ct);
        }

        await notifier.NotifyManualAsync(punch.Id, ct);
        var message = direction == PunchDirection.In
            ? Lang.Pick($"بدأ دوام الموظف {PunchTexts.Clock(punch.At)} (يدوي)", $"Shift started at {PunchTexts.Clock(punch.At)} (manual)")
            : Lang.Pick($"أنهى دوام الموظف {PunchTexts.Clock(punch.At)} · {PunchTexts.Duration(day.MinutesWorked)} (يدوي)", $"Shift ended at {PunchTexts.Clock(punch.At)} · {PunchTexts.Duration(day.MinutesWorked)} (manual)");
        return new ManualPunchResult(punch.Id, punch.At, direction, direction, day.MinutesWorked, message);
    }
}

/// <summary>
/// §3.8 — the owner corrects a day: the first "in", the last "out" (added when missing), the status, or confirms a
/// review. Every change is written to the history with the note; the day is flagged "corrected"; the worker is told.
/// Times are Jordan time. Nothing is ever deleted.
/// </summary>
public record CorrectAttendanceDayCommand(int AttendanceDayId, DateTime? InAt, DateTime? OutAt, string? Status, bool? ConfirmReview, string? Note) : IRequest<AttendanceDayRowDto>;

public class CorrectAttendanceDayCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, AttendanceNotifier notifier) : IRequestHandler<CorrectAttendanceDayCommand, AttendanceDayRowDto>
{
    private static readonly string[] AllowedStatuses = [AttendanceStatus.Present, AttendanceStatus.MissingOut, AttendanceStatus.Absent, AttendanceStatus.Leave, AttendanceStatus.Holiday, AttendanceStatus.Off];

    public async Task<AttendanceDayRowDto> Handle(CorrectAttendanceDayCommand request, CancellationToken ct)
    {
        var callerId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        if (string.IsNullOrWhiteSpace(request.Note))
            throw new CodedHttpException(422, AttendanceCodes.NoteRequired, Lang.Pick("الملاحظة إجبارية للتصحيح", "A note is required for a correction"));
        if (request.InAt is null && request.OutAt is null && request.Status is null && request.ConfirmReview is not true)
            throw new DataValidationException("InAt", Lang.Pick("ما في شي يتعدّل", "Nothing to change"));

        var day = await db.AttendanceDays.Include(d => d.Punches).Include(d => d.ProviderManager)
                      .FirstOrDefaultAsync(d => d.Id == request.AttendanceDayId && !d.IsDeleted, ct)
                  ?? throw new NotFoundException($"Attendance day {request.AttendanceDayId} not found");
        var pm = day.ProviderManager;
        await AttendanceAccess.EnsureOwnerOrAdminAsync(db, currentUser, pm.ProviderType, pm.ProviderId, ct);
        await AttendanceAccess.EnsurePlanAsync(db, pm.ProviderType, pm.ProviderId, ct);

        var note = request.Note.Trim();
        var now = DateTime.UtcNow;
        var changes = new List<(string Field, string? From, string? To)>();
        var punches = day.Punches.Where(p => !p.IsDeleted && p.Verdict != PunchVerdict.Rejected).OrderBy(p => p.At).ToList();

        if (request.InAt is DateTime inLocal)
        {
            var inUtc = JordanTime.ToUtc(inLocal);
            if (inUtc > now) throw new DataValidationException("InAt", Lang.Pick("الوقت بالمستقبل", "The time is in the future"));
            var first = punches.FirstOrDefault(p => p.Direction == PunchDirection.In);
            changes.Add((PunchDirection.In, first?.At is DateTime f ? Clock(f) : null, Clock(inUtc)));
            if (first is null) day.Punches.Add(new Punch { ProviderManagerId = pm.Id, WorkDate = day.WorkDate, Direction = PunchDirection.In, At = inUtc, Verdict = PunchVerdict.Accepted, Source = PunchSource.Correction, RecordedByUserId = callerId, Note = note });
            else { first.At = inUtc; first.Source = PunchSource.Correction; first.RecordedByUserId = callerId; first.Note = note; }
        }
        if (request.OutAt is DateTime outLocal)
        {
            var outUtc = JordanTime.ToUtc(outLocal);
            if (outUtc > now) throw new DataValidationException("OutAt", Lang.Pick("الوقت بالمستقبل", "The time is in the future"));
            var last = punches.LastOrDefault(p => p.Direction == PunchDirection.Out);
            var lastIn = day.Punches.Where(p => !p.IsDeleted && p.Direction == PunchDirection.In).OrderByDescending(p => p.At).FirstOrDefault();
            if (last is not null && lastIn is not null && lastIn.At > last.At) last = null;   // the day is open again after that out: add a new one
            changes.Add((PunchDirection.Out, last?.At is DateTime l ? Clock(l) : null, Clock(outUtc)));
            if (last is null) day.Punches.Add(new Punch { ProviderManagerId = pm.Id, WorkDate = day.WorkDate, Direction = PunchDirection.Out, At = outUtc, Verdict = PunchVerdict.Accepted, Source = PunchSource.Correction, RecordedByUserId = callerId, Note = note });
            else { last.At = outUtc; last.Source = PunchSource.Correction; last.RecordedByUserId = callerId; last.Note = note; }
            if (day.AutoClosedAt is not null && day.Status == AttendanceStatus.MissingOut) day.Status = AttendanceStatus.Present;
        }
        if (request.Status is string status)
        {
            status = status.Trim();
            if (!AllowedStatuses.Contains(status, StringComparer.Ordinal)) throw new DataValidationException("Status", "Status must be present | missingOut | absent | leave | holiday | off");
            if (status is AttendanceStatus.Absent or AttendanceStatus.Off && day.Punches.Any(p => !p.IsDeleted && p.Verdict != PunchVerdict.Rejected))
                throw new DataValidationException("Status", Lang.Pick("اليوم فيه بصمات — ما بيصير غياب أو عطلة", "The day has punches — it cannot be absent or off"));
            if (status != day.Status) { changes.Add(("status", day.Status, status)); day.Status = status; }
        }
        if (request.ConfirmReview is true && AttendanceFlags.Parse(day.Flags).Contains(AttendanceFlag.Review))
        {
            changes.Add(("review", "review", "confirmed"));
            var flags = AttendanceFlags.Parse(day.Flags); flags.Remove(AttendanceFlag.Review);
            day.Flags = flags.Count == 0 ? null : string.Join(",", flags);
            foreach (var p in day.Punches.Where(p => p.Verdict == PunchVerdict.Review)) p.Verdict = PunchVerdict.Accepted;
        }
        if (changes.Count == 0) return await RowAsync(day, ct);

        var grace = (await ShiftResolver.LoadAsync(db, pm.Id, ct)) is var schedules && ShiftResolver.ScheduleFor(schedules, day.WorkDate) is { } sch ? sch.GraceMinutes : AttendanceSettings.Defaults.DefaultGraceMinutes;
        AttendanceMath.Recompute(day, grace);
        day.Flags = AttendanceFlags.Add(day.Flags, AttendanceFlag.Corrected);
        foreach (var (field, from, to) in changes)
            db.AttendanceCorrections.Add(new AttendanceCorrection { AttendanceDayId = day.Id, Field = field, FromValue = from, ToValue = to, Note = note, CorrectedByUserId = callerId, CorrectedAt = now });
        await db.SaveChanges(ct);

        var dateText = day.WorkDate.ToString("d/M");
        var ar = string.Join(" · ", changes.Select(c => c.Field switch
        {
            "in" => $"عدّل المالك حضورك يوم {dateText} لـ{c.To}", "out" => $"عدّل المالك انصرافك يوم {dateText} لـ{c.To}",
            "status" => $"عدّل المالك حالة يوم {dateText} إلى {c.To}", _ => $"أكّد المالك بصمتك يوم {dateText}",
        }));
        var en = string.Join(" · ", changes.Select(c => c.Field switch
        {
            "in" => $"The owner set your punch-in on {dateText} to {c.To}", "out" => $"The owner set your punch-out on {dateText} to {c.To}",
            "status" => $"The owner set {dateText} to {c.To}", _ => $"The owner confirmed your punch on {dateText}",
        }));
        await notifier.NotifyCorrectionAsync(day.Id, ar, en, ct);
        return await RowAsync(day, ct);
    }

    private static string Clock(DateTime utc) => JordanTime.FromUtc(utc).ToString("HH:mm");

    private async Task<AttendanceDayRowDto> RowAsync(AttendanceDay day, CancellationToken ct)
    {
        var name = await db.UserAccounts.AsNoTracking().Where(u => u.Id == day.ProviderManager.UserId).Select(u => u.Name).FirstOrDefaultAsync(ct);
        var zones = await db.PunchZones.AsNoTracking().Where(z => z.ProviderType == day.ProviderManager.ProviderType && z.ProviderId == day.ProviderManager.ProviderId).ToDictionaryAsync(z => z.Id, z => z.Name, ct);
        var recorderIds = day.Punches.Where(p => p.RecordedByUserId != null).Select(p => p.RecordedByUserId!.Value).Distinct().ToList();
        var recorders = recorderIds.Count == 0 ? new Dictionary<int, string?>() : await db.UserAccounts.AsNoTracking().Where(u => recorderIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return AttendanceTable.Row(day, day.ProviderManagerId, name, zones, recorders);
    }
}

/// <summary>Owner adds a day that has no row (e.g. a forgotten day): creates it, then applies the same correction rules.</summary>
public record CreateAttendanceDayCommand(string ProviderType, int ProviderId, int WorkerId, DateOnly WorkDate, DateTime? InAt, DateTime? OutAt, string? Status, string? Note) : IRequest<AttendanceDayRowDto>;

public class CreateAttendanceDayCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IMediator mediator) : IRequestHandler<CreateAttendanceDayCommand, AttendanceDayRowDto>
{
    public async Task<AttendanceDayRowDto> Handle(CreateAttendanceDayCommand request, CancellationToken ct)
    {
        await AttendanceAccess.EnsureOwnerOrAdminAsync(db, currentUser, request.ProviderType, request.ProviderId, ct);
        var pm = await db.ProviderManagers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.WorkerId && x.ProviderType == request.ProviderType && x.ProviderId == request.ProviderId && !x.IsDeleted, ct)
                 ?? throw new NotFoundException($"Worker {request.WorkerId} not found at this provider");
        var today = DateOnly.FromDateTime(JordanTime.FromUtc(DateTime.UtcNow));
        if (request.WorkDate > today) throw new DataValidationException("WorkDate", Lang.Pick("التاريخ بالمستقبل", "The date is in the future"));

        var day = await db.AttendanceDays.FirstOrDefaultAsync(d => d.ProviderManagerId == pm.Id && d.WorkDate == request.WorkDate && !d.IsDeleted, ct);
        if (day is null)
        {
            var shift = ShiftResolver.ForDate(await ShiftResolver.LoadAsync(db, pm.Id, ct), request.WorkDate);
            day = new AttendanceDay { ProviderManagerId = pm.Id, WorkDate = request.WorkDate, ShiftStartUtc = shift?.StartUtc, ShiftEndUtc = shift?.EndUtc,
                Status = request.Status ?? (await AttendanceAccess.IsHolidayAsync(db, request.WorkDate, ct) ? AttendanceStatus.Holiday : shift is null ? AttendanceStatus.Off : AttendanceStatus.Absent) };
            db.AttendanceDays.Add(day);
            await db.SaveChanges(ct);
        }
        return await mediator.Send(new CorrectAttendanceDayCommand(day.Id, request.InAt, request.OutAt, request.Status, null, request.Note), ct);
    }
}

public record AttendanceCorrectionDto(int Id, string Field, string? FromValue, string? ToValue, string Note, string? CorrectedByName, DateTime CorrectedAt);
public record AttendancePunchDto(int Id, string Direction, DateTime At, string Verdict, string? Reason, string Source, string? ZoneName, int? DistanceMeters, int? AccuracyMeters, bool IsMocked, string? RecordedByName, string? Note);
public record AttendanceDayHistoryDto(AttendanceDayRowDto Day, List<AttendancePunchDto> Punches, List<AttendanceCorrectionDto> Corrections);

/// <summary>Owner, admin or ViewAttendance: a day's punches and every correction.</summary>
public record GetAttendanceDayHistoryRequest(int AttendanceDayId) : IRequest<AttendanceDayHistoryDto>;

public class GetAttendanceDayHistoryRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<GetAttendanceDayHistoryRequest, AttendanceDayHistoryDto>
{
    public async Task<AttendanceDayHistoryDto> Handle(GetAttendanceDayHistoryRequest request, CancellationToken ct)
    {
        var day = await db.AttendanceDays.AsNoTracking().Include(d => d.Punches).Include(d => d.ProviderManager).ThenInclude(p => p.User)
                      .FirstOrDefaultAsync(d => d.Id == request.AttendanceDayId && !d.IsDeleted, ct)
                  ?? throw new NotFoundException($"Attendance day {request.AttendanceDayId} not found");
        var pm = day.ProviderManager;
        if (pm.UserId != currentUser.UserId) await AttendanceAccess.EnsureCanViewAsync(db, currentUser, pm.ProviderType, pm.ProviderId, ct);

        var zones = await db.PunchZones.AsNoTracking().Where(z => z.ProviderType == pm.ProviderType && z.ProviderId == pm.ProviderId).ToDictionaryAsync(z => z.Id, z => z.Name, ct);
        var userIds = day.Punches.Where(p => p.RecordedByUserId != null).Select(p => p.RecordedByUserId!.Value).ToList();
        var corrections = await db.AttendanceCorrections.AsNoTracking().Where(c => c.AttendanceDayId == day.Id).OrderBy(c => c.CorrectedAt).ToListAsync(ct);
        userIds.AddRange(corrections.Select(c => c.CorrectedByUserId));
        var names = userIds.Count == 0 ? new Dictionary<int, string?>() : await db.UserAccounts.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        return new AttendanceDayHistoryDto(
            AttendanceTable.Row(day, pm.Id, pm.User.Name, zones, names),
            day.Punches.Where(p => !p.IsDeleted).OrderBy(p => p.At).Select(p => new AttendancePunchDto(p.Id, p.Direction, p.At, p.Verdict, p.Reason, p.Source,
                p.PunchZoneId is int z ? zones.GetValueOrDefault(z) : null, p.DistanceMeters, p.AccuracyMeters, p.IsMocked,
                p.RecordedByUserId is int r ? names.GetValueOrDefault(r) : null, p.Note)).ToList(),
            corrections.Select(c => new AttendanceCorrectionDto(c.Id, c.Field, c.FromValue, c.ToValue, c.Note, names.GetValueOrDefault(c.CorrectedByUserId), c.CorrectedAt)).ToList());
    }
}
