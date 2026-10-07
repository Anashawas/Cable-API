using Application.Settings;
using Cable.Core.Constants;
using Cable.Core.Emuns;
using Cable.Core.Enums;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Subscriptions.Queries;

public record PaymentDto(
    int Id,
    string ReferenceNo,
    decimal Amount,
    string Currency,
    int Method,
    string MethodName,
    DateTime PaidDate,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    int PayerId,
    string PayerName,
    string? PayerPhone,
    string? Note,
    string? ReceiptImageUrl,
    bool HasGeneratedReceipt,
    /// <summary>Relative API path that streams the PDF; null until a receipt exists.</summary>
    string? ReceiptDownloadPath,
    bool IsVoid,
    string? VoidReason,
    DateTime? VoidedAt,
    DateTime CreatedAt,
    int? CreatedBy);

public record SubscriptionDto(
    int Id,
    string EntityType,
    int EntityId,
    string EntityName,
    int? PlanMonths,
    DateTime StartDate,
    DateTime ExpiresAt,
    /// <summary>Active | ExpiringSoon | InGrace | Expired | SwitchedOff — computed, never stored.</summary>
    string Status,
    bool IsOn,
    bool IsSwitchedOff,
    DateTime? SwitchedOffAt,
    string GraceMode,
    int GraceDays,
    bool GraceIsOverride,
    /// <summary>When the AfterDays rule will switch it off; null under manual grace.</summary>
    DateTime? AutoOffAt,
    int DaysUntilExpiry,
    string? Note,
    List<PaymentDto> Payments);

public record PayerDto(
    int Id,
    int? UserAccountId,
    string? Name,
    string? Phone,
    string? Email,
    bool HasWhatsApp,
    bool OptOut,
    string? Note,
    int PaymentsCount,
    DateTime? LastPaidAt);

/// <summary>One mapping for every read of a subscription, so status rules never fork.</summary>
public static class SubscriptionDtoMapper
{
    public const string ReceiptPathFormat = "/api/subscriptions/payments/{0}/receipt";

    public static async Task<List<SubscriptionDto>> MapAsync(
        IApplicationDbContext db, IUploadFileService files, IEnumerable<Subscription> subscriptions,
        CancellationToken ct)
    {
        var list = subscriptions.ToList();
        if (list.Count == 0) return [];

        var grace = await AppSettingsProvider.GetSubscriptionGraceAsync(db, ct);
        var names = await ResolveEntityNamesAsync(db, list, ct);
        var now = DateTime.UtcNow;

        return list.Select(s =>
        {
            var eff = SubscriptionPeriodCalculator.EffectiveGrace(s, grace);
            return new SubscriptionDto(
                s.Id, s.EntityType, s.EntityId,
                names.GetValueOrDefault((s.EntityType, s.EntityId), $"#{s.EntityId}"),
                s.PlanMonths, s.StartDate, s.ExpiresAt,
                SubscriptionPeriodCalculator.StatusOf(s, eff, now),
                SubscriptionPeriodCalculator.IsOn(s, eff, now),
                s.IsSwitchedOff, s.SwitchedOffAt,
                eff.Mode.ToString(), eff.Days, s.GraceMode != null,
                SubscriptionPeriodCalculator.AutoOffAt(s, eff),
                (int)Math.Floor((s.ExpiresAt - now).TotalDays),
                s.Note,
                s.Payments
                    .Where(p => !p.IsDeleted)
                    .OrderByDescending(p => p.PaidDate)
                    .Select(p => MapPayment(p, files))
                    .ToList());
        }).ToList();
    }

    public static PaymentDto MapPayment(Payment p, IUploadFileService files)
    {
        var (name, phone) = DisplayPayer(p.Payer);
        return new PaymentDto(
            p.Id, p.ReferenceNo, p.Amount, p.Currency,
            p.Method, PaymentRecorder.MethodLabel((PaymentMethod)p.Method).En,
            p.PaidDate, p.PeriodStart, p.PeriodEnd,
            p.PayerId, name, phone, p.Note,
            string.IsNullOrEmpty(p.ReceiptImageFileName)
                ? null
                : files.GetFilePath(UploadFileFolders.CableAttachments, p.ReceiptImageFileName),
            !string.IsNullOrEmpty(p.GeneratedReceiptFileName),
            string.IsNullOrEmpty(p.GeneratedReceiptFileName) ? null : string.Format(ReceiptPathFormat, p.Id),
            p.IsVoid, p.VoidReason, p.VoidedAt, p.CreatedAt, p.CreatedBy);
    }

    /// <summary>Requires Payer.UserAccount to be loaded when the payer is user-linked.</summary>
    public static (string Name, string? Phone) DisplayPayer(Payer payer)
        => payer.UserAccountId != null
            ? (payer.UserAccount?.Name ?? $"User #{payer.UserAccountId}", PhoneNumberUtility.ToE164OrOriginal(payer.UserAccount?.Phone))
            : (payer.Name ?? "—", PhoneNumberUtility.ToE164OrOriginal(payer.Phone));

    public static PayerDto MapPayer(Payer p)
    {
        var (name, phone) = DisplayPayer(p);
        var valid = p.Payments.Where(x => !x.IsDeleted && !x.IsVoid).ToList();
        return new PayerDto(
            p.Id, p.UserAccountId, name, phone,
            p.UserAccountId != null ? p.UserAccount?.Email : p.Email,
            p.HasWhatsApp, p.OptOut, p.Note,
            valid.Count,
            valid.Count == 0 ? null : valid.Max(x => x.PaidDate));
    }

    private static async Task<Dictionary<(string, int), string>> ResolveEntityNamesAsync(
        IApplicationDbContext db, List<Subscription> subs, CancellationToken ct)
    {
        var result = new Dictionary<(string, int), string>();

        var cpIds = subs.Where(s => s.EntityType == SubscriptionEntityTypes.StationPremium).Select(s => s.EntityId).Distinct().ToList();
        if (cpIds.Count > 0)
            foreach (var x in await db.ChargingPoints.AsNoTracking().Where(c => cpIds.Contains(c.Id)).Select(c => new { c.Id, c.Name }).ToListAsync(ct))
                result[(SubscriptionEntityTypes.StationPremium, x.Id)] = x.Name;

        var bIds = subs.Where(s => s.EntityType == SubscriptionEntityTypes.Banner).Select(s => s.EntityId).Distinct().ToList();
        if (bIds.Count > 0)
            foreach (var x in await db.Banners.AsNoTracking().Where(b => bIds.Contains(b.Id)).Select(b => new { b.Id, b.Name }).ToListAsync(ct))
                result[(SubscriptionEntityTypes.Banner, x.Id)] = x.Name;

        var spIds = subs.Where(s => s.EntityType == SubscriptionEntityTypes.ServiceProviderPremium).Select(s => s.EntityId).Distinct().ToList();
        if (spIds.Count > 0)
            foreach (var x in await db.ServiceProviders.AsNoTracking().Where(p => spIds.Contains(p.Id)).Select(p => new { p.Id, p.Name }).ToListAsync(ct))
                result[(SubscriptionEntityTypes.ServiceProviderPremium, x.Id)] = x.Name;

        var ocppIds = subs.Where(s => s.EntityType == SubscriptionEntityTypes.OcppConnect).Select(s => s.EntityId).Distinct().ToList();
        if (ocppIds.Count > 0)
            foreach (var x in await db.ChargingPoints.AsNoTracking().Where(c => ocppIds.Contains(c.Id)).Select(c => new { c.Id, c.Name }).ToListAsync(ct))
                result[(SubscriptionEntityTypes.OcppConnect, x.Id)] = x.Name;

        return result;
    }
}
