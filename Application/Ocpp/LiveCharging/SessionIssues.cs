using Cable.Core;
using Cable.Core.Constants;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using FluentValidation;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.LiveCharging;

// ---------------------------------------------------------------------------
// §13.1 — "problem with this session": a complaint of kind ChargingSession on the existing complaint
// system, tied to the session (station, charger, plug, times, energy, cost come from it).
// ---------------------------------------------------------------------------

/// <summary>What the driver sees: Open (sent) → InReview → Resolved | Closed. Pure mapping of ComplaintStatus.</summary>
public static class SessionIssueStatus
{
    public const string Open = "Open";
    public const string InReview = "InReview";
    public const string Resolved = "Resolved";
    public const string Closed = "Closed";

    public static string Of(int status) => (ComplaintStatus)status switch
    {
        ComplaintStatus.New => Open,
        ComplaintStatus.Opened or ComplaintStatus.FollowUp => InReview,
        ComplaintStatus.Solved => Resolved,
        _ => Closed,
    };

    /// <summary>Counts toward "one open issue per session".</summary>
    public static bool IsOpen(int status) => Of(status) is Open or InReview;

    public static readonly int[] OpenStatuses = [(int)ComplaintStatus.New, (int)ComplaintStatus.Opened, (int)ComplaintStatus.FollowUp];

    public static string TextAr(string state) => state switch
    {
        Open => "مُرسلة",
        InReview => "قيد المراجعة",
        Resolved => "تم الحل",
        _ => "مُغلقة",
    };

    public static string TextEn(string state) => state switch
    {
        Open => "Sent",
        InReview => "Under review",
        Resolved => "Resolved",
        _ => "Closed",
    };
}

public record SessionIssueCreatedDto(int IssueId, string Status);

public record SessionIssueDto(
    int IssueId,
    int SessionId,
    string? Reason,
    string ReasonText,
    string Note,
    string? ImageFileName,
    /// <summary>Open | InReview | Resolved | Closed.</summary>
    string Status,
    string StatusText,
    /// <summary>The raw ComplaintStatus value (0 New … 6 SystemIssue).</summary>
    int StatusCode,
    string? Reply,
    DateTime CreatedAt,
    DateTime? ModifiedAt);

/// <summary>
/// Driver: report a problem with one of their own sessions — while it runs or up to 7 days after it ended; one open
/// issue per session (409 ISSUE_ALREADY_OPEN). Reason WrongCost | StoppedEarly | NoEnergy | CableStuck | PlugBroken | Other.
/// </summary>
public record ReportMySessionIssueCommand(int TransactionId, string Reason, string? Note = null, string? ImageFileName = null) : IRequest<SessionIssueCreatedDto>;

public class ReportMySessionIssueCommandValidator : AbstractValidator<ReportMySessionIssueCommand>
{
    public ReportMySessionIssueCommandValidator()
    {
        RuleFor(x => x.Reason).Must(r => SessionIssueReason.Parse(r) is not null)
            .WithMessage(_ => Lang.Pick("السبب لازم يكون واحد من: ", "Reason must be one of: ") + string.Join(", ", SessionIssueReason.All));
        RuleFor(x => x.Note).MaximumLength(450);
        RuleFor(x => x.ImageFileName).MaximumLength(260);
    }
}

public class ReportMySessionIssueCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IBackgroundJobClient jobs)
    : IRequestHandler<ReportMySessionIssueCommand, SessionIssueCreatedDto>
{
    public async Task<SessionIssueCreatedDto> Handle(ReportMySessionIssueCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var tx = await db.OcppTransactions.AsNoTracking()
            .Where(t => t.Id == request.TransactionId && t.StartedByUserId == userId && !t.WasRejected && !t.IsOrphan)
            .Select(t => new { t.Id, t.IsOpen, t.StoppedAt, t.ChargePoint.ChargingPointId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(Lang.Pick("ما لقينا هاي الجلسة بحسابك.", "Cannot find this session in your account."));

        if (!tx.IsOpen && tx.StoppedAt is DateTime stopped && DateTime.UtcNow - stopped > LiveChargingLimits.IssueWindow)
            throw new UnprocessableException(LiveChargingErrorCodes.IssueWindowClosed, Lang.Pick(
                "مرّ أكثر من 7 أيام على هاي الجلسة. تواصل معنا من صفحة الدعم.",
                "This session ended more than 7 days ago. Contact us from the support page."));

        var alreadyOpen = await db.UserComplaints.AsNoTracking()
            .AnyAsync(c => c.OcppTransactionId == tx.Id && !c.IsDeleted && SessionIssueStatus.OpenStatuses.Contains(c.Status), cancellationToken);
        if (alreadyOpen)
            throw new ConflictException(LiveChargingErrorCodes.IssueAlreadyOpen, Lang.Pick(
                "في شكوى مفتوحة على هاي الجلسة. رح نرد عليك عليها.",
                "There is already an open complaint on this session. We will answer it there."));

        var reason = SessionIssueReason.Parse(request.Reason)!;
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        var complaint = new UserComplaint
        {
            UserId = userId,
            ChargingPointId = tx.ChargingPointId,
            Kind = UserComplaintKind.ChargingSession,
            OcppTransactionId = tx.Id,
            Reason = reason,
            // Note is what the existing complaint screens show: the driver's words, or the reason when they wrote none.
            Note = note ?? $"{SessionIssueReason.DescribeAr(reason)} / {SessionIssueReason.Describe(reason)}",
            // The photo is attached afterwards (POST …/issues/{issueId}/photo), so it can only be this driver's own file.
            ImageFileName = null,
            Status = (int)ComplaintStatus.New,
        };
        db.UserComplaints.Add(complaint);
        await db.SaveChanges(cancellationToken);

        var id = complaint.Id;
        try { jobs.Enqueue<LiveChargingNotifications>(n => n.NotifySessionIssueAsync(id, CancellationToken.None)); }
        catch { /* stored; the push is best-effort */ }

        return new SessionIssueCreatedDto(complaint.Id, SessionIssueStatus.Of(complaint.Status));
    }
}

/// <summary>Driver: the issues they reported on one of their sessions, newest first, with status and the reply.</summary>
public record GetMySessionIssuesRequest(int TransactionId) : IRequest<List<SessionIssueDto>>;

public class GetMySessionIssuesRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetMySessionIssuesRequest, List<SessionIssueDto>>
{
    public async Task<List<SessionIssueDto>> Handle(GetMySessionIssuesRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var mine = await db.OcppTransactions.AsNoTracking().AnyAsync(t => t.Id == request.TransactionId && t.StartedByUserId == userId, cancellationToken);
        if (!mine) throw new NotFoundException(Lang.Pick("ما لقينا هاي الجلسة بحسابك.", "Cannot find this session in your account."));

        var rows = await db.UserComplaints.AsNoTracking()
            .Where(c => c.OcppTransactionId == request.TransactionId && c.UserId == userId && !c.IsDeleted)
            .OrderByDescending(c => c.Id)
            .Select(c => new { c.Id, c.Reason, c.Note, c.ImageFileName, c.Status, c.Reply, c.CreatedAt, c.ModifiedAt })
            .ToListAsync(cancellationToken);

        return rows.Select(c =>
        {
            var state = SessionIssueStatus.Of(c.Status);
            return new SessionIssueDto(c.Id, request.TransactionId, c.Reason,
                Lang.Pick(SessionIssueReason.DescribeAr(c.Reason), SessionIssueReason.Describe(c.Reason)), c.Note, c.ImageFileName,
                state, Lang.Pick(SessionIssueStatus.TextAr(state), SessionIssueStatus.TextEn(state)), c.Status, c.Reply, c.CreatedAt, c.ModifiedAt);
        }).ToList();
    }
}

/// <summary>The session behind a ChargingSession complaint, as the partner / admin complaint lists show it.</summary>
public record ComplaintSessionDto(
    int SessionId,
    int ChargerId,
    string? ChargerName,
    int ConnectorId,
    DateTime StartedAt,
    DateTime? StoppedAt,
    decimal? EnergyKwh,
    int? CostFils,
    decimal? CostJod,
    string StartMethod,
    string? StopReasonText,
    string? ReceiptNo);

public static class ComplaintSessionEnricher
{
    /// <summary>Session summaries for the given complaint rows (only ChargingSession ones have one), keyed by session id.</summary>
    public static async Task<Dictionary<int, ComplaintSessionDto>> LoadAsync(IApplicationDbContext db, IEnumerable<int?> sessionIds, CancellationToken ct)
    {
        var ids = sessionIds.Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, ComplaintSessionDto>();
        var rows = await db.OcppTransactions.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Select(t => new
            {
                t.Id, t.OcppChargePointId, t.ChargePoint.DisplayName, t.ConnectorId, t.StartedAt, t.StoppedAt, t.EnergyKwh, t.CostFils,
                Method = t.StartMethod ?? t.StartSource, t.StopReason, t.ChargeLimitKind, t.ChargeLimitValue, t.ChargeLimitReachedAt, t.ReceiptNo,
            })
            .ToListAsync(ct);
        return rows.ToDictionary(t => t.Id, t =>
        {
            var stop = Application.Ocpp.Commands.ChargeLimits.ChargeLimitDto.StopText(t.StopReason, t.ChargeLimitKind, t.ChargeLimitValue, t.ChargeLimitReachedAt);
            return new ComplaintSessionDto(t.Id, t.OcppChargePointId, t.DisplayName, t.ConnectorId, t.StartedAt, t.StoppedAt, t.EnergyKwh,
                t.CostFils, t.CostFils is int f ? f / 1000m : null, t.Method, Lang.Pick(stop.Ar ?? "", stop.En ?? "") is { Length: > 0 } s ? s : null, t.ReceiptNo);
        });
    }
}
