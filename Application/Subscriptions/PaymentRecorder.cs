using Application.Settings;
using Cable.Core;
using Cable.Core.Constants;
using Cable.Core.Emuns;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Subscriptions;

/// <summary>
/// The one place a payment is recorded. Both the new RecordPayment endpoint and
/// the legacy PATCH /premium go through here, so payer reuse, period rules,
/// entity sync and receipt generation cannot diverge between them.
/// </summary>
public sealed class PaymentRecorder(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IReceiptPdfService receipts,
    IUploadFileService files,
    ILogger<PaymentRecorder> logger)
{
    public record PayerInput(
        int? PayerId,
        int? UserAccountId,
        string? Name,
        string? Phone,
        bool HasWhatsApp,
        string? Email,
        string? Note);

    public record Recorded(Subscription Subscription, Payment Payment, string? ReceiptError);

    /// <summary>
    /// Records a payment. Pass <paramref name="planMonths"/> for the normal flow;
    /// pass <paramref name="explicitPeriodEnd"/> only from the legacy premium
    /// endpoint, which takes an expiry date instead of a plan.
    /// </summary>
    public async Task<Recorded> RecordAsync(
        string entityType, int entityId,
        int? planMonths, DateTime? explicitPeriodEnd,
        decimal amount, string? currency, PaymentMethod method, DateTime paidDateUtc,
        PayerInput payerInput, string? note, string? receiptImageFileName,
        CancellationToken ct)
    {
        if (!SubscriptionEntityTypes.IsValid(entityType))
            throw new DataValidationException("EntityType", $"Unknown entity type '{entityType}'");
        if (planMonths is null && explicitPeriodEnd is null)
            throw new DataValidationException("PlanMonths", "A plan length is required");

        var entityName = await ResolveEntityNameAsync(entityType, entityId, ct)
                         ?? throw new NotFoundException($"{entityType} with id {entityId} not found");

        var now = DateTime.UtcNow;
        var payer = await ResolvePayerAsync(payerInput, ct);

        var subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.EntityType == entityType && s.EntityId == entityId && !s.IsDeleted, ct);

        DateTime periodStart, periodEnd;
        if (planMonths is > 0)
            (periodStart, periodEnd) = SubscriptionPeriodCalculator.NextPeriod(subscription, paidDateUtc, planMonths.Value, now);
        else
        {
            periodStart = paidDateUtc;
            periodEnd = explicitPeriodEnd!.Value;
            if (periodEnd <= periodStart)
                throw new DataValidationException("ExpiresAt", "Expiry must be after the payment date");
        }

        if (subscription == null)
        {
            subscription = new Subscription
            {
                EntityType = entityType,
                EntityId = entityId,
                StartDate = periodStart,
                ExpiresAt = periodEnd,
                PlanMonths = planMonths
            };
            db.Subscriptions.Add(subscription);
        }
        else
        {
            // A payment on a switched-off or lapsed subscription revives it.
            subscription.ExpiresAt = periodEnd > subscription.ExpiresAt ? periodEnd : subscription.ExpiresAt;
            subscription.PlanMonths = planMonths ?? subscription.PlanMonths;
            subscription.IsSwitchedOff = false;
            subscription.SwitchedOffAt = null;
            subscription.SwitchedOffByUserId = null;
        }

        var payment = new Payment
        {
            Subscription = subscription,
            Payer = payer,
            Amount = amount,
            Currency = string.IsNullOrWhiteSpace(currency) ? "JOD" : currency!.Trim().ToUpperInvariant(),
            Method = (int)method,
            PaidDate = paidDateUtc,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Note = note,
            ReceiptImageFileName = receiptImageFileName,
            // Placeholder until the identity is known; replaced right after the first save.
            ReferenceNo = $"PENDING-{Guid.NewGuid():N}"
        };
        db.Payments.Add(payment);

        await SubscriptionEntitySync.ApplyAsync(db, subscription, on: true, now, ct);
        await db.SaveChanges(ct);

        payment.ReferenceNo = $"RCP-{JordanTime.FromUtc(paidDateUtc):yyyy}-{payment.Id:D6}";
        var receiptError = await GenerateReceiptAsync(subscription, payment, payer, entityName, ct);
        await db.SaveChanges(ct);

        return new Recorded(subscription, payment, receiptError);
    }

    /// <summary>
    /// Reuse before create: by id, then by linked user, then by phone. A user-
    /// linked payer stores nothing that the user row already holds.
    /// </summary>
    public async Task<Payer> ResolvePayerAsync(PayerInput input, CancellationToken ct)
    {
        if (input.PayerId is int id)
        {
            var existing = await db.Payers.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct)
                           ?? throw new NotFoundException($"Payer with id {id} not found");
            if (input.HasWhatsApp) existing.HasWhatsApp = true;
            return existing;
        }

        if (input.UserAccountId is int userId)
        {
            var user = await db.UserAccounts.AsNoTracking()
                           .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct)
                       ?? throw new NotFoundException($"User with id {userId} not found");
            var linked = await db.Payers.FirstOrDefaultAsync(p => p.UserAccountId == userId && !p.IsDeleted, ct);
            if (linked != null) { if (input.HasWhatsApp) linked.HasWhatsApp = true; return linked; }
            var created = new Payer { UserAccountId = user.Id, HasWhatsApp = input.HasWhatsApp, Note = input.Note };
            db.Payers.Add(created);
            return created;
        }

        var phone = string.IsNullOrWhiteSpace(input.Phone)
            ? null
            : PhoneNumberUtility.NormalizePhoneNumber(input.Phone) ?? input.Phone!.Trim();
        if (string.IsNullOrWhiteSpace(input.Name) && phone is null)
            throw new DataValidationException("Payer", "A payer needs an id, a user, or at least a name or phone");

        if (phone != null)
        {
            var byPhone = await db.Payers.FirstOrDefaultAsync(p => p.Phone == phone && !p.IsDeleted, ct);
            if (byPhone != null)
            {
                if (!string.IsNullOrWhiteSpace(input.Name)) byPhone.Name = input.Name!.Trim();
                if (!string.IsNullOrWhiteSpace(input.Email)) byPhone.Email = input.Email!.Trim();
                if (input.HasWhatsApp) byPhone.HasWhatsApp = true;
                return byPhone;
            }
        }

        var fresh = new Payer
        {
            Name = input.Name?.Trim(),
            Phone = phone,
            Email = input.Email?.Trim(),
            HasWhatsApp = input.HasWhatsApp,
            Note = input.Note
        };
        db.Payers.Add(fresh);
        return fresh;
    }

    /// <summary>
    /// Best effort: a receipt that fails to render must not un-record a payment
    /// that has already been taken. The error comes back to the caller instead.
    /// </summary>
    public async Task<string?> GenerateReceiptAsync(Subscription s, Payment p, Payer payer, string entityName,
        CancellationToken ct)
    {
        try
        {
            var (payerName, payerPhone) = await DisplayPayerAsync(payer, ct);
            var (labelEn, labelAr) = EntityLabel(s.EntityType);
            var (methodEn, methodAr) = MethodLabel((PaymentMethod)p.Method);

            var pdf = receipts.Generate(new ReceiptData(
                p.ReferenceNo,
                JordanTime.FromUtc(DateTime.UtcNow),
                labelEn, labelAr, entityName,
                p.Amount, p.Currency, methodEn, methodAr,
                JordanTime.FromUtc(p.PaidDate),
                JordanTime.FromUtc(p.PeriodStart),
                JordanTime.FromUtc(p.PeriodEnd),
                s.PlanMonths,
                payerName, payerPhone, p.Note));

            p.GeneratedReceiptFileName = await files.SaveBytesAsync(pdf, ".pdf", UploadFileFolders.CableReceipts, ct);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Receipt generation failed for payment {PaymentId}", p.Id);
            return ex.Message;
        }
    }

    public async Task<(string Name, string? Phone)> DisplayPayerAsync(Payer payer, CancellationToken ct)
    {
        if (payer.UserAccountId is int uid)
        {
            var u = await db.UserAccounts.AsNoTracking()
                .Where(x => x.Id == uid)
                .Select(x => new { x.Name, x.Phone })
                .FirstOrDefaultAsync(ct);
            return (u?.Name ?? $"User #{uid}", PhoneNumberUtility.ToE164OrOriginal(u?.Phone));
        }
        return (payer.Name ?? "—", PhoneNumberUtility.ToE164OrOriginal(payer.Phone));
    }

    public async Task<string?> ResolveEntityNameAsync(string entityType, int entityId, CancellationToken ct)
        => entityType switch
        {
            SubscriptionEntityTypes.StationPremium => await db.ChargingPoints.AsNoTracking()
                .Where(x => x.Id == entityId && !x.IsDeleted).Select(x => x.Name).FirstOrDefaultAsync(ct),
            SubscriptionEntityTypes.Banner => await db.Banners.AsNoTracking()
                .Where(x => x.Id == entityId && !x.IsDeleted).Select(x => x.Name).FirstOrDefaultAsync(ct),
            SubscriptionEntityTypes.ServiceProviderPremium => await db.ServiceProviders.AsNoTracking()
                .Where(x => x.Id == entityId && !x.IsDeleted).Select(x => x.Name).FirstOrDefaultAsync(ct),
            SubscriptionEntityTypes.OcppConnect => await db.ChargingPoints.AsNoTracking()
                .Where(x => x.Id == entityId && !x.IsDeleted).Select(x => x.Name).FirstOrDefaultAsync(ct),
            _ => null
        };

    public static (string En, string Ar) EntityLabel(string entityType) => entityType switch
    {
        SubscriptionEntityTypes.StationPremium => ("Station premium", "اشتراك بريميوم المحطة"),
        SubscriptionEntityTypes.Banner => ("Banner ad", "إعلان بانر"),
        SubscriptionEntityTypes.ServiceProviderPremium => ("Service provider premium", "اشتراك بريميوم مزوّد الخدمة"),
        SubscriptionEntityTypes.OcppConnect => ("Cable Connect (live chargers)", "كيبل كونكت (الشواحن المباشرة)"),
        _ => (entityType, entityType)
    };

    public static (string En, string Ar) MethodLabel(PaymentMethod m) => m switch
    {
        PaymentMethod.CliQ => ("CliQ", "كليك"),
        PaymentMethod.Cash => ("Cash", "نقداً"),
        _ => (m.ToString(), m.ToString())
    };
}
