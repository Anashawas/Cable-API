using Application.Common.Interfaces;
using Application.Common.Security;
using Application.Ocpp.LiveCharging;
using Cable.Core;
using Cable.Core.Constants;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.Attendance.Leave;

public static class LeaveKind { public const string Paid = "paid", Unpaid = "unpaid", Sick = "sick"; public static readonly string[] All = [Paid, Unpaid, Sick]; }
public static class LeaveStatus { public const string Pending = "pending", Approved = "approved", Rejected = "rejected", Cancelled = "cancelled"; }
public static class LeaveCodes
{
    public const string InsufficientBalance = "INSUFFICIENT_BALANCE", OverlapsExisting = "OVERLAPS_EXISTING", LeaveNotPending = "LEAVE_NOT_PENDING",
        CannotDecideOwn = "CANNOT_DECIDE_OWN", OwnerOnly = "OWNER_ONLY", AlreadyStarted = "LEAVE_ALREADY_STARTED", AttachmentNotAllowed = "ATTACHMENT_NOT_ALLOWED";
}

public record LeaveBucketDto(int Total, int Used, int Pending, int Remaining);
public record LeaveBalanceDto(int ProviderManagerId, int Year, LeaveBucketDto Annual, LeaveBucketDto Sick, int UnpaidUsed);

public record LeaveRequestDto(int Id, int ProviderManagerId, string? WorkerName, string Kind, DateOnly FromDate, DateOnly ToDate, int DaysCounted, string? Reason,
    bool HasAttachment, string Status, string? RequestedByName, string? DecidedByName, DateTime? DecidedAt, string? DecisionNote, DateTime CreatedAt,
    /// <summary>What the caller may do with it now.</summary>
    bool CanApprove, bool CanCancel);

/// <summary>§3.9 rules shared by the commands: counting days, balances, who decides.</summary>
public static class LeaveRules
{
    public const int MaxDays = 60, SickBackdateDays = 7, OwnerBackdateDays = 30;

    /// <summary>Days in the range that count: the worker's off days (from the shifts) and public holidays are left out.</summary>
    public static async Task<List<DateOnly>> CountedDaysAsync(IApplicationDbContext db, int providerManagerId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var schedules = await ShiftResolver.LoadAsync(db, providerManagerId, ct);
        var holidays = (await db.PublicHolidays.AsNoTracking().Where(h => !h.IsDeleted && h.Date >= from && h.Date <= to).Select(h => h.Date).ToListAsync(ct)).ToHashSet();
        var days = new List<DateOnly>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (holidays.Contains(d)) continue;
            // With a schedule, an off day does not count; without any schedule every day counts (decision 13).
            if (ShiftResolver.ScheduleFor(schedules, d) is not null && ShiftResolver.ForDate(schedules, d) is null) continue;
            days.Add(d);
        }
        return days;
    }

    public static async Task<LeaveBalanceDto> BalanceAsync(IApplicationDbContext db, ProviderManager pm, int year, CancellationToken ct)
    {
        var from = new DateOnly(year, 1, 1); var to = new DateOnly(year, 12, 31);
        var rows = await db.LeaveRequests.AsNoTracking()
            .Where(l => l.ProviderManagerId == pm.Id && !l.IsDeleted && l.FromDate >= from && l.FromDate <= to && (l.Status == LeaveStatus.Approved || l.Status == LeaveStatus.Pending))
            .Select(l => new { l.Kind, l.Status, l.DaysCounted }).ToListAsync(ct);
        LeaveBucketDto Bucket(string kind, int total)
        {
            var used = rows.Where(r => r.Kind == kind && r.Status == LeaveStatus.Approved).Sum(r => r.DaysCounted);
            var pending = rows.Where(r => r.Kind == kind && r.Status == LeaveStatus.Pending).Sum(r => r.DaysCounted);
            return new LeaveBucketDto(total, used, pending, Math.Max(0, total - used));
        }
        return new LeaveBalanceDto(pm.Id, year, Bucket(LeaveKind.Paid, pm.AnnualLeaveDays), Bucket(LeaveKind.Sick, pm.SickLeaveDays),
            rows.Where(r => r.Kind == LeaveKind.Unpaid && r.Status == LeaveStatus.Approved).Sum(r => r.DaysCounted));
    }

    /// <summary>Owner / admin → (true, null); a usable worker with the effective ApproveLeave → (false, their assignment); else 403.</summary>
    public static async Task<(bool IsOwner, ProviderManager? Approver)> EnsureApproverAsync(IApplicationDbContext db, ICurrentUserService currentUser, string providerType, int providerId, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        if (await AdminRoleGuard.IsAdminAsync(db, currentUser, ct) || await AttendanceAccess.IsOwnerAsync(db, providerType, providerId, userId, ct))
        {
            await AttendanceAccess.EnsurePlanAsync(db, providerType, providerId, ct);
            return (true, null);
        }
        var w = await AttendanceAccess.EnsureWorkerAsync(db, currentUser, providerType, providerId, WorkerPrivileges.ApproveLeave, ct);
        return (false, w.Assignment);
    }

    public static bool IsSupervisor(ProviderManager pm) => WorkerPrivileges.Has(pm.Privileges, WorkerPrivileges.ApproveLeave);

    public static void ValidateRange(string kind, DateOnly from, DateOnly to, bool byOwner)
    {
        if (!LeaveKind.All.Contains(kind)) throw new DataValidationException("Kind", "Kind must be paid | unpaid | sick");
        if (to < from) throw new DataValidationException("ToDate", Lang.Pick("تاريخ النهاية قبل البداية", "toDate is before fromDate"));
        if (to.DayNumber - from.DayNumber + 1 > MaxDays) throw new DataValidationException("ToDate", Lang.Pick($"الحد الأقصى {MaxDays} يوم", $"At most {MaxDays} days"));
        if (from.Year != to.Year) throw new DataValidationException("ToDate", Lang.Pick("الإجازة لازم تكون ضمن سنة وحدة", "A leave must stay within one year"));
        var today = DateOnly.FromDateTime(JordanTime.FromUtc(DateTime.UtcNow));
        var earliest = byOwner ? today.AddDays(-OwnerBackdateDays) : kind == LeaveKind.Sick ? today.AddDays(-SickBackdateDays) : today;
        if (from < earliest)
            throw new DataValidationException("FromDate", kind == LeaveKind.Sick && !byOwner
                ? Lang.Pick($"المرضية بأثر رجعي لحد {SickBackdateDays} أيام", $"Sick leave can be backdated at most {SickBackdateDays} days")
                : Lang.Pick("التاريخ لازم يكون اليوم أو بعده", "The date must be today or later"));
    }

    public static async Task EnsureNoOverlapAsync(IApplicationDbContext db, int providerManagerId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (await db.LeaveRequests.AsNoTracking().AnyAsync(l => l.ProviderManagerId == providerManagerId && !l.IsDeleted
                && (l.Status == LeaveStatus.Pending || l.Status == LeaveStatus.Approved) && l.FromDate <= to && l.ToDate >= from, ct))
            throw new ConflictException(LeaveCodes.OverlapsExisting, Lang.Pick("في طلب إجازة ثاني بنفس التواريخ", "Another leave request overlaps these dates"));
    }

    /// <summary>At approval: another request may have been approved meanwhile over the same dates.</summary>
    public static async Task EnsureNoOverlapOnApproveAsync(IApplicationDbContext db, LeaveRequest leave, CancellationToken ct)
    {
        if (await db.LeaveRequests.AsNoTracking().AnyAsync(l => l.ProviderManagerId == leave.ProviderManagerId && l.Id != leave.Id && !l.IsDeleted
                && l.Status == LeaveStatus.Approved && l.FromDate <= leave.ToDate && l.ToDate >= leave.FromDate, ct))
            throw new ConflictException(LeaveCodes.OverlapsExisting, Lang.Pick("في إجازة مقبولة بنفس التواريخ", "An approved leave overlaps these dates"));
    }

    public static async Task EnsureBalanceAsync(IApplicationDbContext db, ProviderManager pm, string kind, DateOnly from, int days, CancellationToken ct)
    {
        if (kind == LeaveKind.Unpaid) return;
        var balance = await BalanceAsync(db, pm, from.Year, ct);
        var bucket = kind == LeaveKind.Sick ? balance.Sick : balance.Annual;
        if (bucket.Used + bucket.Pending + days > bucket.Total)
            throw new CodedHttpException(422, LeaveCodes.InsufficientBalance,
                Lang.Pick($"رصيدك ما بيكفي — المتبقي {Math.Max(0, bucket.Total - bucket.Used - bucket.Pending)} يوم", $"Not enough balance — {Math.Max(0, bucket.Total - bucket.Used - bucket.Pending)} day(s) left"),
                new Dictionary<string, object?> { ["remaining"] = Math.Max(0, bucket.Total - bucket.Used - bucket.Pending), ["requested"] = days, ["pending"] = bucket.Pending });
    }

    /// <summary>Approved: every counted day becomes a leave day (absent → leave; days with punches are left alone).</summary>
    public static async Task ApplyToAttendanceAsync(IApplicationDbContext db, LeaveRequest leave, CancellationToken ct)
    {
        var dates = await CountedDaysAsync(db, leave.ProviderManagerId, leave.FromDate, leave.ToDate, ct);
        var existing = await db.AttendanceDays.Include(d => d.Punches)
            .Where(d => d.ProviderManagerId == leave.ProviderManagerId && !d.IsDeleted && d.WorkDate >= leave.FromDate && d.WorkDate <= leave.ToDate).ToListAsync(ct);
        var schedules = await ShiftResolver.LoadAsync(db, leave.ProviderManagerId, ct);
        foreach (var date in dates)
        {
            var day = existing.FirstOrDefault(d => d.WorkDate == date);
            if (day is null)
            {
                var shift = ShiftResolver.ForDate(schedules, date);
                db.AttendanceDays.Add(new AttendanceDay { ProviderManagerId = leave.ProviderManagerId, WorkDate = date, ShiftStartUtc = shift?.StartUtc, ShiftEndUtc = shift?.EndUtc, Status = AttendanceStatus.Leave });
            }
            else if (!day.Punches.Any(p => !p.IsDeleted && p.Verdict != PunchVerdict.Rejected))
                day.Status = AttendanceStatus.Leave;
        }
    }

    /// <summary>Cancelled after approval: leave days with no punches go back (future rows removed, past ones → absent).</summary>
    public static async Task RemoveFromAttendanceAsync(IApplicationDbContext db, LeaveRequest leave, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(JordanTime.FromUtc(DateTime.UtcNow));
        var days = await db.AttendanceDays.Include(d => d.Punches)
            .Where(d => d.ProviderManagerId == leave.ProviderManagerId && !d.IsDeleted && d.Status == AttendanceStatus.Leave && d.WorkDate >= leave.FromDate && d.WorkDate <= leave.ToDate).ToListAsync(ct);
        foreach (var day in days.Where(d => !d.Punches.Any(p => !p.IsDeleted)))
        {
            if (day.WorkDate >= today) day.IsDeleted = true;
            else day.Status = day.ShiftStartUtc is null ? AttendanceStatus.Off : AttendanceStatus.Absent;
        }
    }

    public static async Task<LeaveRequestDto> ToDtoAsync(IApplicationDbContext db, LeaveRequest l, int callerUserId, bool callerIsOwner, bool callerApproves, CancellationToken ct)
    {
        var ids = new[] { l.RequestedByUserId, l.DecidedByUserId ?? 0 }.Where(x => x > 0).ToList();
        var names = await db.UserAccounts.AsNoTracking().Where(u => ids.Contains(u.Id) || u.Id == l.ProviderManager.UserId).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var today = DateOnly.FromDateTime(JordanTime.FromUtc(DateTime.UtcNow));
        var isOwnRequest = l.ProviderManager.UserId == callerUserId;
        var canApprove = l.Status == LeaveStatus.Pending && !isOwnRequest && (callerIsOwner || (callerApproves && !IsSupervisor(l.ProviderManager)));
        var canCancel = (l.Status == LeaveStatus.Pending && (isOwnRequest || callerIsOwner)) || (l.Status == LeaveStatus.Approved && l.FromDate > today && (isOwnRequest || callerIsOwner));
        return new LeaveRequestDto(l.Id, l.ProviderManagerId, names.GetValueOrDefault(l.ProviderManager.UserId), l.Kind, l.FromDate, l.ToDate, l.DaysCounted, l.Reason,
            l.AttachmentFileName is not null, l.Status, names.GetValueOrDefault(l.RequestedByUserId), l.DecidedByUserId is int d ? names.GetValueOrDefault(d) : null,
            l.DecidedAt, l.DecisionNote, l.CreatedAt, canApprove, canCancel);
    }
}

// ---- queries --------------------------------------------------------------------------------------------------------

/// <summary>The balance of a worker for a year: the worker themself, or owner / admin / ApproveLeave.</summary>
public record GetLeaveBalanceRequest(string ProviderType, int ProviderId, int? WorkerId, int? Year) : IRequest<LeaveBalanceDto>;

public class GetLeaveBalanceRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<GetLeaveBalanceRequest, LeaveBalanceDto>
{
    public async Task<LeaveBalanceDto> Handle(GetLeaveBalanceRequest request, CancellationToken ct)
    {
        var pm = await LeaveAccess.TargetAsync(db, currentUser, request.ProviderType, request.ProviderId, request.WorkerId, ct);
        return await LeaveRules.BalanceAsync(db, pm, request.Year ?? JordanTime.FromUtc(DateTime.UtcNow).Year, ct);
    }
}

/// <summary>The requests: a worker sees their own; owner / admin / ApproveLeave see everyone's.</summary>
public record GetLeaveRequestsRequest(string ProviderType, int ProviderId, int? WorkerId, string? Status, DateOnly? From, DateOnly? To) : IRequest<List<LeaveRequestDto>>;

public class GetLeaveRequestsRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<GetLeaveRequestsRequest, List<LeaveRequestDto>>
{
    public async Task<List<LeaveRequestDto>> Handle(GetLeaveRequestsRequest request, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var (isOwner, approver, own) = await LeaveAccess.ViewerAsync(db, currentUser, request.ProviderType, request.ProviderId, ct);
        var q = db.LeaveRequests.AsNoTracking().Include(l => l.ProviderManager)
            .Where(l => !l.IsDeleted && l.ProviderManager.ProviderType == request.ProviderType && l.ProviderManager.ProviderId == request.ProviderId);
        if (own is not null) q = q.Where(l => l.ProviderManagerId == own.Id);
        else if (request.WorkerId is int w) q = q.Where(l => l.ProviderManagerId == w);
        if (!string.IsNullOrWhiteSpace(request.Status)) { var s = request.Status.Trim().ToLowerInvariant(); q = q.Where(l => l.Status == s); }
        if (request.From is DateOnly f) q = q.Where(l => l.ToDate >= f);
        if (request.To is DateOnly t) q = q.Where(l => l.FromDate <= t);
        var rows = await q.OrderByDescending(l => l.Status == LeaveStatus.Pending).ThenByDescending(l => l.FromDate).Take(500).ToListAsync(ct);
        var result = new List<LeaveRequestDto>();
        foreach (var l in rows) result.Add(await LeaveRules.ToDtoAsync(db, l, userId, isOwner, approver is not null, ct));
        return result;
    }
}

internal static class LeaveAccess
{
    /// <summary>The worker whose leave is meant: the caller's own assignment, or workerId for owner / admin / ApproveLeave.</summary>
    public static async Task<ProviderManager> TargetAsync(IApplicationDbContext db, ICurrentUserService currentUser, string providerType, int providerId, int? workerId, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var own = await db.ProviderManagers.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId && x.ProviderType == providerType && x.ProviderId == providerId && !x.IsDeleted, ct);
        if (workerId is null || (own is not null && own.Id == workerId))
        {
            if (own is not null) { await AttendanceAccess.EnsureWorkerAsync(db, currentUser, providerType, providerId, WorkerPrivileges.Attendance, ct); return own; }
            if (workerId is null) throw new DataValidationException("WorkerId", "workerId is required");
        }
        await LeaveRules.EnsureApproverAsync(db, currentUser, providerType, providerId, ct);
        return await db.ProviderManagers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == workerId && x.ProviderType == providerType && x.ProviderId == providerId && !x.IsDeleted, ct)
               ?? throw new NotFoundException($"Worker {workerId} not found at this provider");
    }

    /// <summary>(isOwner, approver assignment, own assignment when the caller is a plain worker).</summary>
    public static async Task<(bool IsOwner, ProviderManager? Approver, ProviderManager? Own)> ViewerAsync(IApplicationDbContext db, ICurrentUserService currentUser, string providerType, int providerId, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        if (await AdminRoleGuard.IsAdminAsync(db, currentUser, ct) || await AttendanceAccess.IsOwnerAsync(db, providerType, providerId, userId, ct)) return (true, null, null);
        var w = await AttendanceAccess.EnsureWorkerAsync(db, currentUser, providerType, providerId, WorkerPrivileges.Attendance, ct);
        return w.Effective.Contains(WorkerPrivileges.ApproveLeave, StringComparer.Ordinal) ? (false, w.Assignment, null) : (false, null, w.Assignment);
    }
}

// ---- commands -------------------------------------------------------------------------------------------------------

public record LeavePreviewDto(string Kind, int DaysCounted, int RemainingAfter, List<DateOnly> Days);

/// <summary>Before sending: "3 days will be deducted · 6 left after".</summary>
public record PreviewLeaveRequest(string ProviderType, int ProviderId, int? WorkerId, string Kind, DateOnly FromDate, DateOnly ToDate) : IRequest<LeavePreviewDto>;

public class PreviewLeaveRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<PreviewLeaveRequest, LeavePreviewDto>
{
    public async Task<LeavePreviewDto> Handle(PreviewLeaveRequest request, CancellationToken ct)
    {
        var kind = (request.Kind ?? "").Trim().ToLowerInvariant();
        var pm = await LeaveAccess.TargetAsync(db, currentUser, request.ProviderType, request.ProviderId, request.WorkerId, ct);
        LeaveRules.ValidateRange(kind, request.FromDate, request.ToDate, byOwner: pm.UserId != currentUser.UserId);
        var days = await LeaveRules.CountedDaysAsync(db, pm.Id, request.FromDate, request.ToDate, ct);
        var balance = await LeaveRules.BalanceAsync(db, pm, request.FromDate.Year, ct);
        var bucket = kind == LeaveKind.Sick ? balance.Sick : kind == LeaveKind.Paid ? balance.Annual : null;
        return new LeavePreviewDto(kind, days.Count, bucket is null ? 0 : bucket.Total - bucket.Used - bucket.Pending - days.Count, days);
    }
}

/// <summary>The worker asks for leave (§3.9 step 1); the owner and ApproveLeave workers are pushed.</summary>
public record CreateLeaveRequestCommand(string ProviderType, int ProviderId, string Kind, DateOnly FromDate, DateOnly ToDate, string? Reason) : IRequest<LeaveRequestDto>;

public class CreateLeaveRequestCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, AttendanceNotifier notifier) : IRequestHandler<CreateLeaveRequestCommand, LeaveRequestDto>
{
    public async Task<LeaveRequestDto> Handle(CreateLeaveRequestCommand request, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var kind = (request.Kind ?? "").Trim().ToLowerInvariant();
        var worker = await AttendanceAccess.EnsureWorkerAsync(db, currentUser, request.ProviderType, request.ProviderId, WorkerPrivileges.Attendance, ct);
        var pm = worker.Assignment;
        LeaveRules.ValidateRange(kind, request.FromDate, request.ToDate, byOwner: false);
        await LeaveRules.EnsureNoOverlapAsync(db, pm.Id, request.FromDate, request.ToDate, ct);
        var days = await LeaveRules.CountedDaysAsync(db, pm.Id, request.FromDate, request.ToDate, ct);
        if (days.Count == 0) throw new DataValidationException("FromDate", Lang.Pick("كل الأيام المختارة عطل", "Every selected day is an off day"));
        await LeaveRules.EnsureBalanceAsync(db, pm, kind, request.FromDate, days.Count, ct);

        var leave = new LeaveRequest
        {
            ProviderManagerId = pm.Id, Kind = kind, FromDate = request.FromDate, ToDate = request.ToDate, DaysCounted = days.Count,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(), Status = LeaveStatus.Pending, RequestedByUserId = userId,
        };
        db.LeaveRequests.Add(leave);
        await db.SaveChanges(ct);
        await notifier.NotifyLeaveRequestAsync(leave.Id, ct);
        leave.ProviderManager = pm;
        return await LeaveRules.ToDtoAsync(db, leave, userId, false, false, ct);
    }
}

/// <summary>Owner records a leave directly (§3.9 step 6): approved at once, may be backdated up to 30 days.</summary>
public record DirectLeaveCommand(string ProviderType, int ProviderId, int WorkerId, string Kind, DateOnly FromDate, DateOnly ToDate, string? Reason) : IRequest<LeaveRequestDto>;

public class DirectLeaveCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, AttendanceNotifier notifier) : IRequestHandler<DirectLeaveCommand, LeaveRequestDto>
{
    public async Task<LeaveRequestDto> Handle(DirectLeaveCommand request, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var kind = (request.Kind ?? "").Trim().ToLowerInvariant();
        await AttendanceAccess.EnsureOwnerOrAdminAsync(db, currentUser, request.ProviderType, request.ProviderId, ct);
        await AttendanceAccess.EnsurePlanAsync(db, request.ProviderType, request.ProviderId, ct);
        var pm = await db.ProviderManagers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.WorkerId && x.ProviderType == request.ProviderType && x.ProviderId == request.ProviderId && !x.IsDeleted, ct)
                 ?? throw new NotFoundException($"Worker {request.WorkerId} not found at this provider");
        LeaveRules.ValidateRange(kind, request.FromDate, request.ToDate, byOwner: true);
        await LeaveRules.EnsureNoOverlapAsync(db, pm.Id, request.FromDate, request.ToDate, ct);
        var days = await LeaveRules.CountedDaysAsync(db, pm.Id, request.FromDate, request.ToDate, ct);
        if (days.Count == 0) throw new DataValidationException("FromDate", Lang.Pick("كل الأيام المختارة عطل", "Every selected day is an off day"));
        await LeaveRules.EnsureBalanceAsync(db, pm, kind, request.FromDate, days.Count, ct);

        var now = DateTime.UtcNow;
        var leave = new LeaveRequest
        {
            ProviderManagerId = pm.Id, Kind = kind, FromDate = request.FromDate, ToDate = request.ToDate, DaysCounted = days.Count,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(), Status = LeaveStatus.Approved,
            RequestedByUserId = userId, DecidedByUserId = userId, DecidedAt = now,
        };
        db.LeaveRequests.Add(leave);
        await LeaveRules.ApplyToAttendanceAsync(db, leave, ct);
        await db.SaveChanges(ct);
        await notifier.NotifyLeaveDecisionAsync(leave.Id, ct);
        leave.ProviderManager = pm;
        return await LeaveRules.ToDtoAsync(db, leave, userId, true, false, ct);
    }
}

/// <summary>
/// Approve / reject (owner, admin, or ApproveLeave — never one's own; a supervisor's own request is the owner's to
/// decide) or cancel (the worker before approval, or anyone allowed before the leave starts; the balance comes back).
/// </summary>
public record DecideLeaveCommand(int LeaveId, string Status, string? Note) : IRequest<LeaveRequestDto>;

public class DecideLeaveCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, AttendanceNotifier notifier) : IRequestHandler<DecideLeaveCommand, LeaveRequestDto>
{
    public async Task<LeaveRequestDto> Handle(DecideLeaveCommand request, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var status = (request.Status ?? "").Trim().ToLowerInvariant();
        if (status is not (LeaveStatus.Approved or LeaveStatus.Rejected or LeaveStatus.Cancelled))
            throw new DataValidationException("Status", "Status must be approved | rejected | cancelled");

        var leave = await db.LeaveRequests.Include(l => l.ProviderManager).FirstOrDefaultAsync(l => l.Id == request.LeaveId && !l.IsDeleted, ct)
                    ?? throw new NotFoundException($"Leave request {request.LeaveId} not found");
        var pm = leave.ProviderManager;
        var today = DateOnly.FromDateTime(JordanTime.FromUtc(DateTime.UtcNow));
        var now = DateTime.UtcNow;
        var isOwnRequest = pm.UserId == userId;
        var isOwner = await AdminRoleGuard.IsAdminAsync(db, currentUser, ct) || await AttendanceAccess.IsOwnerAsync(db, pm.ProviderType, pm.ProviderId, userId, ct);

        if (status == LeaveStatus.Cancelled)
        {
            if (!isOwner && !isOwnRequest) throw new ForbiddenAccessException("Only the worker or the owner can cancel this request.");
            if (leave.Status is LeaveStatus.Rejected or LeaveStatus.Cancelled)
                throw new CodedHttpException(422, LeaveCodes.LeaveNotPending, Lang.Pick("الطلب منتهي أصلاً", "This request is already closed"));
            if (leave.Status == LeaveStatus.Approved && leave.FromDate <= today && !isOwner)
                throw new CodedHttpException(422, LeaveCodes.AlreadyStarted, Lang.Pick("الإجازة بلّشت — بلّغ المالك", "The leave has already started — tell the owner"));
            if (leave.Status == LeaveStatus.Approved) await LeaveRules.RemoveFromAttendanceAsync(db, leave, ct);
            leave.Status = LeaveStatus.Cancelled; leave.CancelledAt = now; leave.CancelledByUserId = userId; leave.DecisionNote = request.Note?.Trim();
            await db.SaveChanges(ct);
            if (!isOwnRequest) await notifier.NotifyLeaveDecisionAsync(leave.Id, ct);
            return await LeaveRules.ToDtoAsync(db, leave, userId, isOwner, false, ct);
        }

        if (leave.Status != LeaveStatus.Pending)
            throw new CodedHttpException(422, LeaveCodes.LeaveNotPending, Lang.Pick("الطلب مش معلّق", "This request is not pending"));
        if (isOwnRequest)
            throw new CodedHttpException(403, LeaveCodes.CannotDecideOwn, Lang.Pick("ما بتقدر تقرّر بإجازتك", "You cannot decide your own leave"));
        if (!isOwner)
        {
            await AttendanceAccess.EnsureWorkerAsync(db, currentUser, pm.ProviderType, pm.ProviderId, WorkerPrivileges.ApproveLeave, ct);
            if (LeaveRules.IsSupervisor(pm))
                throw new CodedHttpException(403, LeaveCodes.OwnerOnly, Lang.Pick("إجازة المشرف بيقرّرها المالك بس", "A supervisor's leave is decided by the owner only"));
        }
        else await AttendanceAccess.EnsurePlanAsync(db, pm.ProviderType, pm.ProviderId, ct);

        if (status == LeaveStatus.Approved)
        {
            await LeaveRules.EnsureNoOverlapOnApproveAsync(db, leave, ct);
            await LeaveRules.ApplyToAttendanceAsync(db, leave, ct);
        }
        leave.Status = status; leave.DecidedByUserId = userId; leave.DecidedAt = now; leave.DecisionNote = request.Note?.Trim();
        await db.SaveChanges(ct);
        await notifier.NotifyLeaveDecisionAsync(leave.Id, ct);
        return await LeaveRules.ToDtoAsync(db, leave, userId, isOwner, !isOwner, ct);
    }
}

/// <summary>Owner: the per-worker balances (decision 14).</summary>
public record SetLeaveSettingsCommand(int ProviderManagerId, int AnnualLeaveDays, int SickLeaveDays) : IRequest<LeaveBalanceDto>;

public class SetLeaveSettingsCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<SetLeaveSettingsCommand, LeaveBalanceDto>
{
    public async Task<LeaveBalanceDto> Handle(SetLeaveSettingsCommand request, CancellationToken ct)
    {
        var pm = await db.ProviderManagers.FirstOrDefaultAsync(x => x.Id == request.ProviderManagerId && !x.IsDeleted, ct)
                 ?? throw new NotFoundException($"Worker assignment {request.ProviderManagerId} not found");
        await AttendanceAccess.EnsureOwnerOrAdminAsync(db, currentUser, pm.ProviderType, pm.ProviderId, ct);
        if (request.AnnualLeaveDays is < 0 or > 90 || request.SickLeaveDays is < 0 or > 90)
            throw new DataValidationException("AnnualLeaveDays", Lang.Pick("الرصيد بين 0 و90 يوم", "Balances must be between 0 and 90 days"));
        pm.AnnualLeaveDays = request.AnnualLeaveDays; pm.SickLeaveDays = request.SickLeaveDays;
        await db.SaveChanges(ct);
        return await LeaveRules.BalanceAsync(db, pm, JordanTime.FromUtc(DateTime.UtcNow).Year, ct);
    }
}

// ---- attachment (sick leave) ------------------------------------------------------------------------------------------

public record UploadLeaveAttachmentCommand(int LeaveId, IFormFile? File) : IRequest<LeaveRequestDto>;

public class UploadLeaveAttachmentCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IUploadFileService files) : IRequestHandler<UploadLeaveAttachmentCommand, LeaveRequestDto>
{
    private static readonly string[] Allowed = [".jpg", ".jpeg", ".png", ".webp", ".pdf"];

    public async Task<LeaveRequestDto> Handle(UploadLeaveAttachmentCommand request, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var leave = await db.LeaveRequests.Include(l => l.ProviderManager).FirstOrDefaultAsync(l => l.Id == request.LeaveId && !l.IsDeleted, ct)
                    ?? throw new NotFoundException($"Leave request {request.LeaveId} not found");
        var pm = leave.ProviderManager;
        var isOwner = await AdminRoleGuard.IsAdminAsync(db, currentUser, ct) || await AttendanceAccess.IsOwnerAsync(db, pm.ProviderType, pm.ProviderId, userId, ct);
        if (!isOwner && pm.UserId != userId) throw new ForbiddenAccessException("Only the worker or the owner can attach to this request.");
        if (leave.Kind != LeaveKind.Sick || leave.Status is LeaveStatus.Rejected or LeaveStatus.Cancelled)
            throw new CodedHttpException(422, LeaveCodes.AttachmentNotAllowed, Lang.Pick("المرفق للإجازة المرضية المعلّقة أو المقبولة بس", "Attachments are for pending or approved sick leave only"));
        if (request.File is null || request.File.Length == 0) throw new DataValidationException("File", Lang.Pick("أرفق الملف بالحقل 'file'", "Attach the file as form field 'file'"));
        if (!Allowed.Contains(Path.GetExtension(request.File.FileName).ToLowerInvariant())) throw new DataValidationException("File", Lang.Pick("الملف لازم يكون صورة أو PDF", "Only jpg, png, webp or pdf"));
        if (request.File.Length > 5 * 1024 * 1024) throw new DataValidationException("File", Lang.Pick("حجم الملف لازم يكون أقل من 5 ميغا", "The file must be smaller than 5 MB"));

        leave.AttachmentFileName = await files.SaveFileAsync(request.File, UploadFileFolders.CableLeaveAttachments, ct);
        await db.SaveChanges(ct);
        return await LeaveRules.ToDtoAsync(db, leave, userId, isOwner, false, ct);
    }
}

/// <summary>Owner, admin or the worker (decision 36: a supervisor sees the kind and dates only).</summary>
public record GetLeaveAttachmentRequest(int LeaveId) : IRequest<LiveChargingFile>;

public class GetLeaveAttachmentRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser, IUploadFileService files) : IRequestHandler<GetLeaveAttachmentRequest, LiveChargingFile>
{
    public async Task<LiveChargingFile> Handle(GetLeaveAttachmentRequest request, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var leave = await db.LeaveRequests.AsNoTracking().Include(l => l.ProviderManager).FirstOrDefaultAsync(l => l.Id == request.LeaveId && !l.IsDeleted, ct)
                    ?? throw new NotFoundException($"Leave request {request.LeaveId} not found");
        var pm = leave.ProviderManager;
        var isOwner = await AdminRoleGuard.IsAdminAsync(db, currentUser, ct) || await AttendanceAccess.IsOwnerAsync(db, pm.ProviderType, pm.ProviderId, userId, ct);
        if (!isOwner && pm.UserId != userId) throw new ForbiddenAccessException("The medical report is for the owner only.");
        if (leave.AttachmentFileName is null) throw new NotFoundException(Lang.Pick("ما في مرفق", "No attachment"));

        byte[] content;
        try { content = await files.GetFileAsync(UploadFileFolders.CableLeaveAttachments, leave.AttachmentFileName, ct); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { throw new NotFoundException(Lang.Pick("المرفق مش موجود", "The attachment is missing")); }
        var ext = Path.GetExtension(leave.AttachmentFileName).ToLowerInvariant();
        var type = ext switch { ".pdf" => "application/pdf", ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
        return new LiveChargingFile(content, type, leave.AttachmentFileName);
    }
}
