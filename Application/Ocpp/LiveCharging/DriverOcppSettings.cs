using System.Globalization;
using System.Net;
using Application.Common.Models.Emails;
using Cable.Core;
using Cable.Core.Constants;
using Cable.Core.Enums;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Ocpp.LiveCharging;

// ---------------------------------------------------------------------------
// §13.2 — the driver's Cable Connect settings (email receipts, off by default) and the receipt email itself.
// ---------------------------------------------------------------------------

/// <param name="EmailReceipts">Email the receipt PDF when a session ends. Default false.</param>
/// <param name="HasEmail">The account has an email address; without one nothing is sent (the app asks for it).</param>
public record DriverOcppSettingsDto(bool EmailReceipts, bool HasEmail);

/// <summary>Driver: their Cable Connect settings (defaults when never saved).</summary>
public record GetMyOcppSettingsRequest : IRequest<DriverOcppSettingsDto>;

public class GetMyOcppSettingsRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetMyOcppSettingsRequest, DriverOcppSettingsDto>
{
    public async Task<DriverOcppSettingsDto> Handle(GetMyOcppSettingsRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        return await DriverOcppSettings.LoadAsync(db, userId, cancellationToken);
    }
}

/// <summary>Driver: change their Cable Connect settings. A null field is left as it is.</summary>
public record UpdateMyOcppSettingsCommand(bool? EmailReceipts) : IRequest<DriverOcppSettingsDto>;

public class UpdateMyOcppSettingsCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<UpdateMyOcppSettingsCommand, DriverOcppSettingsDto>
{
    public async Task<DriverOcppSettingsDto> Handle(UpdateMyOcppSettingsCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");

        if (request.EmailReceipts is bool emailReceipts)
        {
            var row = await db.UserOcppSettings.FirstOrDefaultAsync(s => s.UserId == userId && !s.IsDeleted, cancellationToken);
            if (row is null)
            {
                row = new UserOcppSetting { UserId = userId };
                db.UserOcppSettings.Add(row);
            }
            row.EmailReceipts = emailReceipts;
            await db.SaveChanges(cancellationToken);
        }

        return await DriverOcppSettings.LoadAsync(db, userId, cancellationToken);
    }
}

internal static class DriverOcppSettings
{
    public static async Task<DriverOcppSettingsDto> LoadAsync(IApplicationDbContext db, int userId, CancellationToken ct)
    {
        var emailReceipts = await db.UserOcppSettings.AsNoTracking()
            .Where(s => s.UserId == userId && !s.IsDeleted)
            .Select(s => s.EmailReceipts)
            .FirstOrDefaultAsync(ct);
        var email = await db.UserAccounts.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct);
        return new DriverOcppSettingsDto(emailReceipts, !string.IsNullOrWhiteSpace(email));
    }
}

/// <summary>
/// §13.2: emails the session receipt PDF to a driver who opted in (UserOcppSetting.EmailReceipts) and has an email.
/// Called by the "charging finished" job after pricing / points; at most once per session (OcppReceiptEmail).
/// Never throws: every failure is logged (and recorded as Failed when the send itself failed).
/// </summary>
public class SessionReceiptEmailer(
    IApplicationDbContext db,
    ILiveChargingPdfService pdf,
    IEmailService email,
    ILogger<SessionReceiptEmailer> log)
{
    public const string Sent = "Sent";
    public const string Failed = "Failed";

    /// <returns>True when an email went out.</returns>
    public async Task<bool> SendIfOptedInAsync(int transactionId, CancellationToken ct)
    {
        try
        {
            var tx = await db.OcppTransactions.AsNoTracking()
                .Where(t => t.Id == transactionId && !t.IsOpen && !t.WasRejected && !t.IsOrphan
                            && t.StartedByUserId != null && t.StartSource != OcppStartSource.Operator)
                .Select(t => new
                {
                    UserId = t.StartedByUserId!.Value,
                    StationName = t.ChargePoint.ChargingPoint.Name,
                    t.StartedAt, t.EnergyKwh, t.CostFils,
                })
                .FirstOrDefaultAsync(ct);
            if (tx is null) return false;

            var optedIn = await db.UserOcppSettings.AsNoTracking()
                .AnyAsync(s => s.UserId == tx.UserId && s.EmailReceipts && !s.IsDeleted, ct);
            if (!optedIn) return false;

            var user = await db.UserAccounts.AsNoTracking()
                .Where(u => u.Id == tx.UserId)
                .Select(u => new { u.Name, u.Email })
                .FirstOrDefaultAsync(ct);
            var to = user?.Email?.Trim();
            if (string.IsNullOrWhiteSpace(to) || !email.IsValidEmail(to))
            {
                log.LogInformation("Receipt email: session {TransactionId} — user {UserId} opted in but has no valid email", transactionId, tx.UserId);
                return false;
            }

            if (await db.OcppReceiptEmails.AsNoTracking().AnyAsync(e => e.OcppTransactionId == transactionId, ct))
                return false;   // already sent (or tried) for this session

            // The user's device language (latest token), Arabic when unknown — like the pushes.
            var language = await db.NotificationTokens.AsNoTracking()
                .Where(n => n.UserId == tx.UserId && n.AppType == FirebaseAppType.UserApp)
                .OrderByDescending(n => n.UpdatedAt)
                .Select(n => n.Language)
                .FirstOrDefaultAsync(ct);

            var file = await SessionReceiptBuilder.BuildAsync(db, pdf, transactionId, null, ct);
            var receiptNo = Path.GetFileNameWithoutExtension(file.FileName);
            var (subject, html) = ReceiptEmailTexts.Compose(language, user!.Name, tx.StationName, JordanTime.FromUtc(tx.StartedAt),
                tx.EnergyKwh, tx.CostFils, receiptNo);

            var result = await email.SendEmailAsync(new EmailMessage
            {
                To = to,
                ToName = user.Name,
                Subject = subject,
                HtmlBody = html,
                Attachments = [new EmailAttachment { FileName = file.FileName, Content = file.Content, ContentType = file.ContentType }],
            }, ct);

            db.OcppReceiptEmails.Add(new OcppReceiptEmail
            {
                OcppTransactionId = transactionId,
                UserId = tx.UserId,
                Email = to.Length > 256 ? to[..256] : to,
                Status = result.Success ? Sent : Failed,
                Error = result.Success ? null : Truncate($"{result.ErrorCode}: {result.Message}", 500),
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChanges(ct);

            if (!result.Success)
                log.LogWarning("Receipt email: session {TransactionId} to user {UserId} failed — {Code} {Message}",
                    transactionId, tx.UserId, result.ErrorCode, result.Message);
            return result.Success;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Receipt email: session {TransactionId} could not be emailed", transactionId);
            return false;
        }
    }

    private static string Truncate(string s, int max) => s.Length > max ? s[..max] : s;
}

/// <summary>The receipt email's subject and body, in the user's device language ("en" → English, else Arabic).</summary>
public static class ReceiptEmailTexts
{
    public static (string Subject, string Html) Compose(string? language, string? name, string stationName, DateTime startedAtLocal,
        decimal? energyKwh, int? costFils, string receiptNo)
    {
        var en = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);
        var e = (string? s) => WebUtility.HtmlEncode(s ?? "");
        var date = startedAtLocal.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var kwh = energyKwh?.ToString("0.###", CultureInfo.InvariantCulture) ?? "—";
        var jod = costFils is int f ? (f / 1000m).ToString("0.000", CultureInfo.InvariantCulture) : "—";
        var hello = string.IsNullOrWhiteSpace(name) ? null : name.Trim();

        var subject = Lang.Pick(language, $"إيصال الشحن {receiptNo} — {stationName}", $"Charging receipt {receiptNo} — {stationName}");

        var html = en
            ? $"""
               <div dir="ltr" style="font-family:Arial,sans-serif;font-size:14px;color:#222">
                 <p>{(hello is null ? "Hello," : $"Hello {e(hello)},")}</p>
                 <p>Thank you for charging with Cable. Your receipt is attached (PDF).</p>
                 <table style="border-collapse:collapse">
                   <tr><td style="padding:2px 12px 2px 0;color:#666">Station</td><td>{e(stationName)}</td></tr>
                   <tr><td style="padding:2px 12px 2px 0;color:#666">Started</td><td>{date}</td></tr>
                   <tr><td style="padding:2px 12px 2px 0;color:#666">Energy</td><td>{kwh} kWh</td></tr>
                   <tr><td style="padding:2px 12px 2px 0;color:#666">Total</td><td>{jod} JOD</td></tr>
                   <tr><td style="padding:2px 12px 2px 0;color:#666">Receipt no.</td><td>{e(receiptNo)}</td></tr>
                 </table>
                 <p style="color:#666;font-size:12px">You get this email because receipt emails are on in the Cable app (Settings). You can switch them off there.</p>
               </div>
               """
            : $"""
               <div dir="rtl" style="font-family:Tahoma,Arial,sans-serif;font-size:14px;color:#222">
                 <p>{(hello is null ? "مرحبا،" : $"مرحبا {e(hello)}،")}</p>
                 <p>شكراً لشحنك مع كيبل. مرفق إيصال الجلسة (PDF).</p>
                 <table style="border-collapse:collapse">
                   <tr><td style="padding:2px 0 2px 12px;color:#666">المحطة</td><td>{e(stationName)}</td></tr>
                   <tr><td style="padding:2px 0 2px 12px;color:#666">البداية</td><td>{date}</td></tr>
                   <tr><td style="padding:2px 0 2px 12px;color:#666">الطاقة</td><td>{kwh} ك.و.س</td></tr>
                   <tr><td style="padding:2px 0 2px 12px;color:#666">المجموع</td><td>{jod} دينار</td></tr>
                   <tr><td style="padding:2px 0 2px 12px;color:#666">رقم الإيصال</td><td>{e(receiptNo)}</td></tr>
                 </table>
                 <p style="color:#666;font-size:12px">وصلك هذا الإيميل لأن إرسال الإيصالات بالإيميل مفعّل بتطبيق كيبل (الإعدادات). بتقدر توقفه من هناك.</p>
               </div>
               """;
        return (subject, html);
    }
}
