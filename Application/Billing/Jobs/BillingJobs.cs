using Application.Billing.Subscriptions;
using Application.Settings;
using Application.Subscriptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Billing.Jobs;

/// <summary>
/// Partner billing jobs (Hangfire, registered in WebApi/Program.cs). Every push is keyed in
/// BillingEvent so the same event is never sent twice, and each event is only sent inside a
/// 48-hour window after it happens — deploying this never floods partners with old news.
/// </summary>
public interface IBillingJobs
{
    /// <summary>Daily: owner pushes 7 days before, 1 day before, on expiry, at grace end (§2.6); trial ending in 2 days / ended (§2b.6).</summary>
    Task<int> SendSubscriptionRemindersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Every 10 minutes: activation pushes, trial end (back to free, no grace), bundle pushes at 20% / 10% / 0%,
    /// and admin alerts for unlimited plans over their daily soft cap or monthly break-even (alert only — scanning never stops).
    /// </summary>
    Task<int> RunBillingWatchAsync(CancellationToken cancellationToken = default);
}

public sealed class BillingJobs(
    IApplicationDbContext db,
    INotificationService notificationService,
    ILogger<BillingJobs> logger) : IBillingJobs
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(48);
    private BillingNotifier Notifier => new(db, notificationService, logger);

    public async Task<int> SendSubscriptionRemindersAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var grace = await AppSettingsProvider.GetSubscriptionGraceAsync(db, cancellationToken);
        var plans = await PlanCatalog.LoadAllAsync(db, cancellationToken);
        var sent = 0;

        var types = PlanInputValidatorTypes;
        var rows = await db.Subscriptions.AsNoTracking()
            .Where(s => !s.IsDeleted && types.Contains(s.EntityType)
                        && ((s.ExpiresAt > now.AddDays(-3) && s.ExpiresAt < now.AddDays(8))
                            || (s.IsSwitchedOff && s.SwitchedOffAt > now.AddDays(-3))))
            .ToListAsync(cancellationToken);
        var rowIds = rows.Select(r => r.Id).ToList();
        var trials = await db.PlanSubscriptions.AsNoTracking()
            .Where(r => r.IsTrial && r.SubscriptionId != null && rowIds.Contains(r.SubscriptionId.Value) && !r.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var s in rows)
        {
            // A period that is only a trial gets the trial texts instead.
            if (trials.Any(t => t.SubscriptionId == s.Id && t.EndsAt >= s.ExpiresAt.AddMinutes(-1))) continue;

            var providerType = BillingProviderTypes.OfEntityType(s.EntityType)!;
            var plan = PlanCatalog.PlanForEntityType(plans, s.EntityType);
            var (nameAr, nameEn) = plan is null ? PaymentRecorderLabels(s.EntityType) : (plan.NameAr, plan.NameEn);
            var g = SubscriptionPeriodCalculator.EffectiveGrace(s, grace);
            var e = s.ExpiresAt;
            var stamp = e.ToString("yyyyMMddHHmm");
            var data = new Dictionary<string, string> { ["entityType"] = s.EntityType, ["subscriptionId"] = s.Id.ToString() };
            string? key = null; SubscriptionReminderTexts.Kind kind = default;

            if (!s.IsSwitchedOff && InWindow(now, e.AddDays(-7)) && now < e.AddDays(-1)) { key = $"sub:{s.Id}:exp7:{stamp}"; kind = SubscriptionReminderTexts.Kind.SevenDays; }
            else if (!s.IsSwitchedOff && now >= e.AddDays(-1) && now < e) { key = $"sub:{s.Id}:exp1:{stamp}"; kind = SubscriptionReminderTexts.Kind.OneDay; }
            else if (InWindow(now, e) && !(s.IsSwitchedOff && s.SwitchedOffAt < e)) { key = $"sub:{s.Id}:expired:{stamp}"; kind = SubscriptionReminderTexts.Kind.Expired; }
            else if (s.IsSwitchedOff && s.SwitchedOffAt is { } off && off >= e && InWindow(now, off)) { key = $"sub:{s.Id}:graceEnd:{stamp}"; kind = SubscriptionReminderTexts.Kind.GraceEnded; }
            if (key is null) continue;

            var autoOff = SubscriptionPeriodCalculator.AutoOffAt(s, g);
            var stillOn = SubscriptionPeriodCalculator.IsOn(s, g, now);
            if (await Notifier.NotifyOwnerAsync(key, "subscription" + kind, providerType, s.EntityId, null, s.Id,
                    lang => SubscriptionReminderTexts.Compose(kind, lang, nameAr, nameEn, e, autoOff, stillOn), data, cancellationToken))
                sent++;
        }

        // Trials: 2 days before the end, and the end (decision 17: no grace afterwards).
        var trialRecords = await db.PlanSubscriptions.AsNoTracking()
            .Where(r => r.IsTrial && r.Status == PlanSubscriptionStatuses.Active && !r.IsDeleted && r.EndsAt != null
                        && r.EndsAt > now.AddDays(-3) && r.EndsAt < now.AddDays(3))
            .ToListAsync(cancellationToken);
        foreach (var t in trialRecords)
        {
            if (await ConvertedAsync(t, cancellationToken)) continue;
            var end = t.EndsAt!.Value;
            var data = new Dictionary<string, string> { ["planSubscriptionId"] = t.Id.ToString(), ["planKey"] = t.PlanKey };
            if (now >= end.AddDays(-2) && now < end)
            {
                if (await Notifier.NotifyOwnerAsync($"trial:{t.Id}:ending", "trialEnding", t.ProviderType, t.ProviderId, t.Id, t.SubscriptionId,
                        lang => (Lang.Pick(lang, "تجربتك بتخلص قريباً", "Your trial ends soon"),
                                 Lang.Pick(lang, $"تجربتك المجانية لباقة {t.PlanNameAr} بتخلص بعد يومين — اشترك لتكمل", $"Your free {t.PlanNameEn} trial ends in 2 days — subscribe to continue")),
                        data, cancellationToken)) sent++;
            }
            else if (InWindow(now, end))
            {
                if (await Notifier.NotifyOwnerAsync($"trial:{t.Id}:ended", "trialEnded", t.ProviderType, t.ProviderId, t.Id, t.SubscriptionId,
                        lang => (Lang.Pick(lang, "انتهت تجربتك المجانية", "Your free trial has ended"),
                                 Lang.Pick(lang, $"انتهت تجربة باقة {t.PlanNameAr} — اشترك لترجع المميزات", $"Your {t.PlanNameEn} trial has ended — subscribe to get the features back")),
                        data, cancellationToken)) sent++;
            }
        }

        logger.LogInformation("Billing reminders: {Count} push(es) sent", sent);
        return sent;
    }

    public async Task<int> RunBillingWatchAsync(CancellationToken cancellationToken = default)
    {
        var count = 0;
        count += await EndTrialsAsync(cancellationToken);
        count += await SendActivationsAsync(cancellationToken);
        count += await SendBundleThresholdsAsync(cancellationToken);
        count += await CheckUnlimitedPlansAsync(cancellationToken);
        logger.LogInformation("Billing watch: {Count} event(s)", count);
        return count;
    }

    // ---- trial end -------------------------------------------------------------

    private async Task<int> EndTrialsAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var ended = await db.PlanSubscriptions.AsNoTracking()
            .Where(r => r.IsTrial && r.Status == PlanSubscriptionStatuses.Active && !r.IsDeleted
                        && r.PlanCategory == PlanCategories.Subscription && r.EndsAt != null && r.EndsAt <= now && r.EndsAt > now.AddDays(-30))
            .ToListAsync(ct);
        var switchedOff = 0;
        // Idempotent without an event row: a row already off, or extended by a payment, is left alone.
        foreach (var t in ended)
        {
            var plan = await db.BillingPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == t.BillingPlanId, ct);
            if (plan is null) continue;
            var entityTypes = new List<string>();
            if (PlanCatalog.LegacyEntityTypeFor(plan, t.ProviderType) is { } et) entityTypes.Add(et);
            var includedIds = PlanCatalog.Includes(plan);
            foreach (var inc in await db.BillingPlans.AsNoTracking().Where(p => includedIds.Contains(p.Id)).ToListAsync(ct))
                if (PlanCatalog.LegacyEntityTypeFor(inc, t.ProviderType) is { } iet) entityTypes.Add(iet);

            foreach (var type in entityTypes.Distinct())
            {
                var row = await db.Subscriptions.FirstOrDefaultAsync(s => s.EntityType == type && s.EntityId == t.ProviderId && !s.IsDeleted, ct);
                if (row is null || row.IsSwitchedOff || row.ExpiresAt > t.EndsAt!.Value.AddMinutes(1)) continue;   // paid / extended since
                var paidBeyond = await db.Payments.AnyAsync(p => p.SubscriptionId == row.Id && !p.IsVoid && !p.IsDeleted && p.PeriodEnd > t.EndsAt, ct);
                if (paidBeyond) continue;
                await LegacySubscriptionBridge.SwitchOffAsync(db, row, null, ct);
                switchedOff++;
            }
            await db.SaveChanges(ct);
        }
        return switchedOff;
    }

    private async Task<bool> ConvertedAsync(PlanSubscription trial, CancellationToken ct)
    {
        if (trial.SubscriptionId is not int sid || trial.EndsAt is null) return false;
        var row = await db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sid, ct);
        return row is not null && row.ExpiresAt > trial.EndsAt.Value.AddMinutes(1);
    }

    // ---- activation --------------------------------------------------------------

    private async Task<int> SendActivationsAsync(CancellationToken ct)
    {
        var since = DateTime.UtcNow - Window;
        string[] sources = [PlanSubscriptionSources.Payment, PlanSubscriptionSources.Admin, PlanSubscriptionSources.Upgrade, PlanSubscriptionSources.Bundle];
        var fresh = await db.PlanSubscriptions.AsNoTracking()
            .Where(r => !r.IsDeleted && !r.IsTrial && r.Status == PlanSubscriptionStatuses.Active && r.CreatedAt >= since && sources.Contains(r.Source))
            .ToListAsync(ct);
        var sent = 0;
        foreach (var r in fresh)
        {
            var until = r.EndsAt is { } e ? JordanTime.FromUtc(e).ToString("yyyy-MM-dd") : null;
            var data = new Dictionary<string, string> { ["planSubscriptionId"] = r.Id.ToString(), ["planKey"] = r.PlanKey };
            var isBundle = r.PlanCategory == PlanCategories.Loyalty;
            if (await Notifier.NotifyOwnerAsync($"activated:{r.Id}", isBundle ? "bundleActivated" : "subscriptionActivated", r.ProviderType, r.ProviderId, r.Id, r.SubscriptionId,
                    lang => isBundle
                        ? (Lang.Pick(lang, "تفعّلت حزمة المسحات", "Your scan bundle is active"),
                           r.ScansIncluded is int n
                               ? Lang.Pick(lang, $"عندك {n} مسحة{(until is null ? "" : $" لحد {until}")}", $"You have {n} scans{(until is null ? "" : $" until {until}")}")
                               : Lang.Pick(lang, $"مسح غير محدود لحد {until}", $"Unlimited scans until {until}"))
                        : (Lang.Pick(lang, "تفعّلت باقتك", "Your plan is active"),
                           Lang.Pick(lang, $"تفعّلت باقتك {r.PlanNameAr} لحد {until}", $"Your {r.PlanNameEn} plan is active until {until}")),
                    data, ct))
                sent++;
        }
        return sent;
    }

    // ---- bundle thresholds ---------------------------------------------------------

    private async Task<int> SendBundleThresholdsAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var bundles = await db.PlanSubscriptions.AsNoTracking()
            .Where(r => !r.IsDeleted && r.PlanCategory == PlanCategories.Loyalty && r.Status == PlanSubscriptionStatuses.Active
                        && r.ScansIncluded != null && r.StartsAt <= now && (r.EndsAt == null || r.EndsAt > now))
            .ToListAsync(ct);
        var sent = 0;
        foreach (var b in bundles)
        {
            var remaining = Math.Max(0, b.ScansIncluded!.Value - b.ScansUsed);
            var pct = BillingMath.PercentRemaining(b.ScansIncluded, b.ScansUsed) ?? 100;
            int? threshold = remaining == 0 ? 0 : pct <= 10 ? 10 : pct <= 20 ? 20 : null;
            if (threshold is null) continue;

            // Higher thresholds that were jumped over are recorded silently so they are never sent late.
            foreach (var higher in BillingSettings.BundleThresholds.Where(t => t > threshold))
                await Notifier.RecordAsync(new BillingEvent { EventKey = $"bundle:{b.Id}:{higher}", Kind = "bundleThreshold", Audience = "owner",
                    ProviderType = b.ProviderType, ProviderId = b.ProviderId, PlanSubscriptionId = b.Id }, ct);

            var total = b.ScansIncluded.Value;
            var data = new Dictionary<string, string>
            {
                ["planSubscriptionId"] = b.Id.ToString(), ["scansRemaining"] = remaining.ToString(), ["scansTotal"] = total.ToString(), ["threshold"] = threshold.Value.ToString()
            };
            if (await Notifier.NotifyOwnerAsync($"bundle:{b.Id}:{threshold}", "bundleThreshold", b.ProviderType, b.ProviderId, b.Id, null,
                    lang => threshold == 0
                        ? (Lang.Pick(lang, "خلصت حزمة المسحات", "Your scan bundle is used up"),
                           Lang.Pick(lang, "جدّد لتكمل تعطي نقاط لزباينك", "Renew to keep giving your customers points"))
                        : (Lang.Pick(lang, "قرّبت حزمتك تخلص", "Your scan bundle is running low"),
                           Lang.Pick(lang, $"باقي {remaining} من {total} مسحة — جدّد قبل ما تخلص", $"{remaining} of {total} scans left — renew before it runs out")),
                    data, ct))
                sent++;
        }
        return sent;
    }

    // ---- unlimited plans: soft cap and break-even (admin alerts only) --------------

    private async Task<int> CheckUnlimitedPlansAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var unlimited = await db.PlanSubscriptions.AsNoTracking()
            .Where(r => !r.IsDeleted && r.PlanCategory == PlanCategories.Loyalty && r.Status == PlanSubscriptionStatuses.Active
                        && r.ScansIncluded == null && r.StartsAt <= now && r.EndsAt > now)
            .ToListAsync(ct);
        if (unlimited.Count == 0) return 0;

        var planIds = unlimited.Select(r => r.BillingPlanId).Distinct().ToList();
        var caps = await db.BillingPlans.AsNoTracking().Where(p => planIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.DailySoftCap, ct);
        var local = JordanTime.FromUtc(now);
        var dayStart = JordanTime.ToUtc(new DateTime(local.Year, local.Month, local.Day, 0, 0, 0, DateTimeKind.Unspecified));
        var sent = 0;

        foreach (var r in unlimited)
        {
            var names = await Plans.ProviderNames.ResolveAsync(db, [(r.ProviderType, r.ProviderId)], ct);
            var name = names.GetValueOrDefault((r.ProviderType, r.ProviderId)) ?? $"#{r.ProviderId}";
            var data = new Dictionary<string, string> { ["planSubscriptionId"] = r.Id.ToString() };

            if (caps.GetValueOrDefault(r.BillingPlanId) is int cap)
            {
                var today = await db.PartnerTransactionBundleScans.AsNoTracking()
                    .CountAsync(m => m.ConsumedPlanSubscriptionId == r.Id && m.ConsumedAt >= dayStart && m.RefundedAt == null, ct);
                if (today > cap && await Notifier.NotifyAdminsAsync($"cap:{r.Id}:{local:yyyyMMdd}", "adminSoftCap", r.ProviderType, r.ProviderId, r.Id,
                        lang => (Lang.Pick(lang, "تجاوز الحد اليومي للمسح", "Daily scan soft cap exceeded"),
                                 Lang.Pick(lang, $"{name}: {today} مسحة اليوم (الحد {cap}) — راجع المسحات", $"{name}: {today} scans today (cap {cap}) — please review")),
                        data, ct))
                    sent++;
            }

            var be = await BreakEvenCalculator.ComputeAsync(db, r, ct);
            if (be.State == "over" && await Notifier.NotifyAdminsAsync($"be:{r.Id}:{local:yyyyMM}", "adminBreakEven", r.ProviderType, r.ProviderId, r.Id,
                    lang => (Lang.Pick(lang, "المحطة تجاوزت نقطة التعادل", "Station passed break-even"),
                             Lang.Pick(lang, $"{name}: {be.ScansUsed} مسحة هالشهر من {be.BreakEvenScans} (التعادل)", $"{name}: {be.ScansUsed} scans this month vs break-even {be.BreakEvenScans}")),
                    data, ct))
                sent++;
        }
        return sent;
    }

    private static bool InWindow(DateTime now, DateTime trigger) => now >= trigger && now < trigger + Window;

    private static readonly string[] PlanInputValidatorTypes = Plans.PlanInputValidator.SellableEntityTypes;

    private static (string Ar, string En) PaymentRecorderLabels(string entityType)
    {
        var (en, ar) = PaymentRecorder.EntityLabel(entityType);
        return (ar, en);
    }
}

/// <summary>§2.6 texts, Arabic and English.</summary>
public static class SubscriptionReminderTexts
{
    public enum Kind { SevenDays, OneDay, Expired, GraceEnded }

    public static (string Title, string Body) Compose(Kind kind, string? lang, string nameAr, string nameEn, DateTime expiresAtUtc, DateTime? graceEndsUtc, bool stillOn)
    {
        string D(DateTime utc) => JordanTime.FromUtc(utc).ToString("yyyy-MM-dd");
        return kind switch
        {
            Kind.SevenDays => (Lang.Pick(lang, "باقتك بتنتهي قريباً", "Your plan ends soon"),
                Lang.Pick(lang, $"باقتك {nameAr} بتنتهي بعد 7 أيام", $"Your {nameEn} plan ends in 7 days")),
            Kind.OneDay => (Lang.Pick(lang, "باقتك بتنتهي بكرة", "Your plan ends tomorrow"),
                Lang.Pick(lang, $"باقتك {nameAr} بتنتهي بكرة — جدّد لتحافظ على المميزات", $"Your {nameEn} plan ends tomorrow — renew to keep the features")),
            Kind.Expired => (Lang.Pick(lang, "انتهت باقتك", "Your plan has ended"),
                graceEndsUtc is { } g
                    ? Lang.Pick(lang, $"انتهت باقتك {nameAr} — عندك فترة سماح لحد {D(g)}", $"Your {nameEn} plan has ended — you have a grace period until {D(g)}")
                    : stillOn
                        ? Lang.Pick(lang, $"انتهت باقتك {nameAr} — جدّد لتحافظ على المميزات", $"Your {nameEn} plan has ended — renew to keep the features")
                        : Lang.Pick(lang, $"انتهت باقتك {nameAr}", $"Your {nameEn} plan has ended")),
            _ => (Lang.Pick(lang, "توقّفت مميزات باقتك", "Your plan's features are off"),
                Lang.Pick(lang, $"توقّفت مميزات الباقة {nameAr} — جدّد لترجعها", $"The {nameEn} features are now off — renew to get them back")),
        };
    }
}
