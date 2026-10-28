using System.Globalization;
using System.Text.Json;
using Application.Common.Security;
using Application.NotificationInbox.Helpers;
using Cable.Core.Constants;
using Cable.Core.Emuns;
using Cable.Core.Enums;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Ocpp.LiveCharging;

/// <summary>
/// The live-charging (B) pushes, run by the API's Hangfire server (enqueued with
/// <c>jobs.Enqueue&lt;LiveChargingNotifications&gt;(…)</c>) so a slow Firebase never holds up the request:
/// a plug report → the station's owner and managers; a session issue → owner, managers and admins; an issue's
/// status change → the driver. Push in each device's language, plus an inbox row. Best-effort throughout.
/// </summary>
public class LiveChargingNotifications(IApplicationDbContext db, INotificationService notifications, ILogger<LiveChargingNotifications> logger)
{
    public const string ConnectorReportType = "ocpp_connector_report";
    public const string SessionIssueType = "ocpp_session_issue";
    public const string SessionIssueStatusType = "ocpp_session_issue_status";

    /// <summary>"A driver could not charge from plug 2 on the right-hand charger — the charger answered: Rejected".</summary>
    public async Task NotifyConnectorReportAsync(int reportId, CancellationToken cancellationToken = default)
    {
        var r = await db.OcppConnectorReports.AsNoTracking()
            .Where(x => x.Id == reportId)
            .Select(x => new
            {
                x.Id, x.ChargingPointId, x.OcppChargePointId, x.ConnectorId, x.Reason, x.CommandStatus, x.CommandResultStatus, x.CommandErrorCode,
                x.ChargePoint.DisplayName, OcppId = x.ChargePoint.ChargePointId, StationName = x.ChargePoint.ChargingPoint.Name,
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (r is null) return;

        var recipients = await StationTeamAsync(r.ChargingPointId, includeAdmins: false, cancellationToken);
        var charger = string.IsNullOrWhiteSpace(r.DisplayName) ? r.OcppId : r.DisplayName.Trim();
        var answer = r.CommandResultStatus ?? r.CommandStatus;

        (string, string) Text(string? lang)
        {
            var reason = Lang.Pick(lang, OcppConnectorReportReason.DescribeAr(r.Reason), OcppConnectorReportReason.Describe(r.Reason));
            var tail = answer is null ? reason : Lang.Pick(lang, "الشاحن ردّ: ", "the charger answered: ") + answer;
            return (Lang.Pick(lang, "بلاغ عن مقبس — " + r.StationName, "Plug report — " + r.StationName),
                Lang.Pick(lang,
                    $"سائق ما قدر يشحن من المقبس {r.ConnectorId} بالشاحن {charger} — {tail}",
                    $"A driver could not charge from plug {r.ConnectorId} on {charger} — {tail}"));
        }

        var data = new Dictionary<string, string>
        {
            ["type"] = ConnectorReportType,
            ["reportId"] = r.Id.ToString(CultureInfo.InvariantCulture),
            ["chargingPointId"] = r.ChargingPointId.ToString(CultureInfo.InvariantCulture),
            ["chargerId"] = r.OcppChargePointId.ToString(CultureInfo.InvariantCulture),
            ["connectorId"] = r.ConnectorId.ToString(CultureInfo.InvariantCulture),
        };
        await SendAsync(recipients, Text, ConnectorReportType, data, appType: null, cancellationToken);
    }

    /// <summary>A driver flagged a session ("problem with this session"): the station team and the admins.</summary>
    public async Task NotifySessionIssueAsync(int complaintId, CancellationToken cancellationToken = default)
    {
        var c = await IssueAsync(complaintId, cancellationToken);
        if (c is null) return;

        var recipients = await StationTeamAsync(c.ChargingPointId, includeAdmins: true, cancellationToken);
        (string, string) Text(string? lang) => (
            Lang.Pick(lang, "شكوى على جلسة شحن — " + c.StationName, "Charging session complaint — " + c.StationName),
            Lang.Pick(lang,
                $"جلسة #{c.SessionId}: {SessionIssueReason.DescribeAr(c.Reason)}" + (string.IsNullOrWhiteSpace(c.Note) ? "" : " — " + Short(c.Note)),
                $"Session #{c.SessionId}: {SessionIssueReason.Describe(c.Reason)}" + (string.IsNullOrWhiteSpace(c.Note) ? "" : " — " + Short(c.Note))));

        var data = IssueData(SessionIssueType, c.Id, c.SessionId, c.ChargingPointId, c.Status);
        await SendAsync(recipients, Text, SessionIssueType, data, appType: null, cancellationToken);
    }

    /// <summary>The driver: "your complaint is under review" / "resolved" (+ the reply).</summary>
    public async Task NotifySessionIssueStatusAsync(int complaintId, CancellationToken cancellationToken = default)
    {
        var c = await IssueAsync(complaintId, cancellationToken);
        if (c is null) return;

        var state = SessionIssueStatus.Of(c.Status);
        (string, string) Text(string? lang) => (
            Lang.Pick(lang, "تحديث على شكوى الشحن", "Update on your charging complaint"),
            Lang.Pick(lang, SessionIssueStatus.TextAr(state), SessionIssueStatus.TextEn(state))
                + " · " + c.StationName
                + (string.IsNullOrWhiteSpace(c.Reply) ? "" : " — " + Short(c.Reply)));

        var data = IssueData(SessionIssueStatusType, c.Id, c.SessionId, c.ChargingPointId, c.Status);
        await SendAsync([c.UserId], Text, SessionIssueStatusType, data, FirebaseAppType.UserApp, cancellationToken);
    }

    private sealed record IssueRow(int Id, int UserId, int ChargingPointId, string StationName, int SessionId, string? Reason, string? Note, string? Reply, int Status);

    private async Task<IssueRow?> IssueAsync(int complaintId, CancellationToken ct)
    {
        var c = await db.UserComplaints.AsNoTracking()
            .Where(x => x.Id == complaintId && x.Kind == UserComplaintKind.ChargingSession && x.OcppTransactionId != null)
            .Select(x => new IssueRow(x.Id, x.UserId, x.ChargingPointId, x.ChargingPoint.Name, x.OcppTransactionId!.Value, x.Reason, x.Note, x.Reply, x.Status))
            .FirstOrDefaultAsync(ct);
        return c;
    }

    private static Dictionary<string, string> IssueData(string type, int issueId, int sessionId, int chargingPointId, int status) => new()
    {
        ["type"] = type,
        ["issueId"] = issueId.ToString(CultureInfo.InvariantCulture),
        ["sessionId"] = sessionId.ToString(CultureInfo.InvariantCulture),
        ["chargingPointId"] = chargingPointId.ToString(CultureInfo.InvariantCulture),
        ["status"] = SessionIssueStatus.Of(status),
    };

    private static string Short(string s) => s.Length <= 80 ? s : s[..80] + "…";

    /// <summary>Owner + active managers of the station (+ admins).</summary>
    private async Task<List<int>> StationTeamAsync(int chargingPointId, bool includeAdmins, CancellationToken ct)
    {
        var ids = await db.ProviderManagers.AsNoTracking()
            .Where(m => m.ProviderType == "ChargingPoint" && m.ProviderId == chargingPointId && m.IsActive && !m.IsDeleted)
            .Select(m => m.UserId)
            .ToListAsync(ct);
        var owner = await db.ChargingPoints.AsNoTracking().Where(c => c.Id == chargingPointId).Select(c => c.OwnerId).FirstOrDefaultAsync(ct);
        if (owner is int o) ids.Add(o);
        if (includeAdmins)
            ids.AddRange(await db.UserAccounts.AsNoTracking()
                .Where(u => u.RoleId == AdminRoleGuard.AdminRoleId && u.IsActive && !u.IsDeleted)
                .Select(u => u.Id)
                .ToListAsync(ct));
        return ids.Distinct().ToList();
    }

    /// <summary>Push per (app, language) and one inbox row per user in that user's language (Arabic when unknown).</summary>
    private async Task SendAsync(List<int> userIds, Func<string?, (string Title, string Body)> text, string typeName,
        Dictionary<string, string> data, FirebaseAppType? appType, CancellationToken ct)
    {
        if (userIds.Count == 0) return;
        try
        {
            var tokens = await db.NotificationTokens.AsNoTracking()
                .Where(t => userIds.Contains(t.UserId) && (appType == null || t.AppType == appType))
                .Select(t => new { t.UserId, t.Token, t.Language, t.AppType })
                .ToListAsync(ct);

            foreach (var group in tokens.GroupBy(t => (t.AppType, En: string.Equals(t.Language, "en", StringComparison.OrdinalIgnoreCase))))
            {
                var (title, body) = text(group.Key.En ? "en" : "ar");
                try
                {
                    await notifications.SendMessagesAsync(group.Select(t => t.Token).Distinct().ToList(), title, body, group.Key.AppType, data);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "LiveCharging push {Type}: FCM send failed ({AppType})", typeName, group.Key.AppType);
                }
            }

            var typeId = await db.NotificationTypes.AsNoTracking()
                .Where(t => t.Name == typeName)
                .Select(t => (int?)t.Id)
                .FirstOrDefaultAsync(ct) ?? 1;
            var languageOf = tokens.GroupBy(t => t.UserId).ToDictionary(g => g.Key, g => g.First().Language);
            var json = JsonSerializer.Serialize(data);
            foreach (var byLang in userIds.GroupBy(u => string.Equals(languageOf.GetValueOrDefault(u), "en", StringComparison.OrdinalIgnoreCase)))
            {
                var (title, body) = text(byLang.Key ? "en" : "ar");
                await NotificationInboxHelper.CreateNotificationInboxRecordsAsync(db, byLang, typeId, title, body, null, json, ct);
            }
            logger.LogInformation("LiveCharging push {Type} → {Users} user(s), {Devices} device(s)", typeName, userIds.Count, tokens.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "LiveCharging push {Type} failed", typeName);
        }
    }
}
