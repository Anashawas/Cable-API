using Application.Common.Security;
using Cable.Core;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Billing.Plans;

// ---------------------------------------------------------------------------
// Create
// ---------------------------------------------------------------------------

public record CreatePlanCommand(PlanInput Plan) : IRequest<PlanSaveResult>;

public class CreatePlanCommandHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<CreatePlanCommand, PlanSaveResult>
{
    public async Task<PlanSaveResult> Handle(CreatePlanCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        var input = request.Plan ?? throw new DataValidationException("Plan", "A plan body is required");

        var category = PlanCategories.Normalize(input.Category)
                       ?? throw new DataValidationException("Category", Lang.Pick("الفئة Subscription أو Loyalty", "category must be Subscription or Loyalty"));
        var key = (input.Key ?? "").Trim().ToLowerInvariant();

        var others = await PlanCatalog.LoadAllAsync(applicationDbContext, cancellationToken);
        if (await applicationDbContext.BillingPlans.AnyAsync(p => p.Key == key, cancellationToken))
            throw new ConflictException(BillingErrorCodes.PlanKeyTaken, Lang.Pick($"المفتاح '{key}' مستعمل", $"The key '{key}' is already used"));
        PlanInputValidator.Validate(input, category, others, null, key);

        var plan = new BillingPlan { Key = key, Category = category };
        PlanWriter.Apply(plan, input, category);
        foreach (var (o, i) in (input.PriceOptions ?? []).Select((o, i) => (o, i)))
            plan.PriceOptions.Add(new BillingPlanPriceOption { Months = o.Months, PriceJod = o.PriceJod, Scans = o.Scans, SortOrder = o.SortOrder ?? i, IsActive = true });

        applicationDbContext.BillingPlans.Add(plan);
        await applicationDbContext.SaveChanges(cancellationToken);

        applicationDbContext.BillingPlanChangeLogs.Add(new BillingPlanChangeLog
        {
            BillingPlanId = plan.Id,
            ChangedByUserId = currentUserService.UserId,
            ChangedAt = DateTime.UtcNow,
            Changes = BillingJson.Write(new List<PlanChange> { new("created") }),
            Note = null
        });
        await applicationDbContext.SaveChanges(cancellationToken);

        return new PlanSaveResult(PlanDtoMapper.Map(plan, 0), [], 0, false);
    }
}

// ---------------------------------------------------------------------------
// Update (key and category fixed). Price / rule edits apply to new subscriptions
// and renewals only — records carry snapshots; feature edits apply at once.
// ---------------------------------------------------------------------------

public record UpdatePlanCommand(int Id, PlanInput Plan, string? Note = null) : IRequest<PlanSaveResult>;

public class UpdatePlanCommandHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<UpdatePlanCommand, PlanSaveResult>
{
    public async Task<PlanSaveResult> Handle(UpdatePlanCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        var input = request.Plan ?? throw new DataValidationException("Plan", "A plan body is required");

        var plan = await PlanCatalog.LoadAsync(applicationDbContext, request.Id, cancellationToken, track: true);
        if (PlanCategories.Normalize(input.Category) is { } c && c != plan.Category)
            throw new DataValidationException("Category", Lang.Pick("لا يمكن تغيير فئة الباقة بعد إنشائها", "A plan's category cannot change after creation"));
        if (plan.Key == PlanKeys.Free && !input.IsActive)
            throw new DataValidationException("IsActive", Lang.Pick("الباقة المجانية لا تُؤرشف", "The free plan cannot be archived"));

        var others = await PlanCatalog.LoadAllAsync(applicationDbContext, cancellationToken);
        PlanInputValidator.Validate(input, plan.Category, others.Where(p => p.Id != plan.Id).ToList(), plan.Id, plan.Key);

        var before = PlanDiff.Of(plan);
        PlanWriter.Apply(plan, input, plan.Category);

        var inputs = input.PriceOptions ?? [];
        foreach (var (o, i) in inputs.Select((o, i) => (o, i)))
        {
            var existing = o.Id is int id ? plan.PriceOptions.FirstOrDefault(x => x.Id == id && !x.IsDeleted) : null;
            if (o.Id is not null && existing is null)
                throw new UnprocessableException(BillingErrorCodes.InvalidPriceOption, Lang.Pick($"خيار السعر {o.Id} غير موجود", $"Price option {o.Id} does not belong to this plan"));
            if (existing is null)
                plan.PriceOptions.Add(new BillingPlanPriceOption { Months = o.Months, PriceJod = o.PriceJod, Scans = o.Scans, SortOrder = o.SortOrder ?? i, IsActive = true });
            else
            {
                existing.Months = o.Months;
                existing.PriceJod = o.PriceJod;
                existing.Scans = o.Scans;
                existing.SortOrder = o.SortOrder ?? i;
                existing.IsActive = true;
            }
        }
        // Options left out are retired, never deleted: records keep their snapshot, renewals pick a live option.
        var keep = inputs.Where(o => o.Id is not null).Select(o => o.Id!.Value).ToHashSet();
        foreach (var o in plan.PriceOptions.Where(o => o.Id > 0 && !keep.Contains(o.Id) && !o.IsDeleted))
            o.IsActive = false;

        await applicationDbContext.SaveChanges(cancellationToken);
        return await PlanWriter.LogAsync(applicationDbContext, plan, before, currentUserService.UserId, request.Note, cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// Archive / reactivate (no delete)
// ---------------------------------------------------------------------------

public record SetPlanActiveCommand(int Id, bool IsActive, string? Note = null) : IRequest<PlanSaveResult>;

public class SetPlanActiveCommandHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<SetPlanActiveCommand, PlanSaveResult>
{
    public async Task<PlanSaveResult> Handle(SetPlanActiveCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        var plan = await PlanCatalog.LoadAsync(applicationDbContext, request.Id, cancellationToken, track: true);
        if (plan.Key == PlanKeys.Free && !request.IsActive)
            throw new DataValidationException("IsActive", Lang.Pick("الباقة المجانية لا تُؤرشف", "The free plan cannot be archived"));

        var before = PlanDiff.Of(plan);
        plan.IsActive = request.IsActive;
        await applicationDbContext.SaveChanges(cancellationToken);
        return await PlanWriter.LogAsync(applicationDbContext, plan, before, currentUserService.UserId, request.Note, cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// Feature × plan matrix (admin settings screen)
// ---------------------------------------------------------------------------

public record PlanFeaturesInput(int PlanId, List<string> Features);

public record UpdatePlanFeatureMatrixCommand(List<PlanFeaturesInput> Plans, string? Note = null) : IRequest<List<PlanSaveResult>>;

public class UpdatePlanFeatureMatrixCommandHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<UpdatePlanFeatureMatrixCommand, List<PlanSaveResult>>
{
    public async Task<List<PlanSaveResult>> Handle(UpdatePlanFeatureMatrixCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        var unknown = (request.Plans ?? []).SelectMany(p => p.Features ?? []).Where(f => !PlanFeatureCatalog.IsKnown(f)).Distinct().ToList();
        if (unknown.Count > 0)
            throw new UnprocessableException(BillingErrorCodes.UnknownFeature,
                Lang.Pick($"مفتاح ميزة غير موجود بالكتالوج: {string.Join(", ", unknown)}", $"Unknown feature key(s): {string.Join(", ", unknown)}"),
                new Dictionary<string, object?> { ["unknown"] = unknown });

        var results = new List<PlanSaveResult>();
        foreach (var item in request.Plans ?? [])
        {
            var plan = await PlanCatalog.LoadAsync(applicationDbContext, item.PlanId, cancellationToken, track: true);
            var before = PlanDiff.Of(plan);
            plan.Features = BillingJson.Write((item.Features ?? []).Distinct().ToList());
            await applicationDbContext.SaveChanges(cancellationToken);
            results.Add(await PlanWriter.LogAsync(applicationDbContext, plan, before, currentUserService.UserId, request.Note, cancellationToken));
        }
        return results;
    }
}

// ---------------------------------------------------------------------------
// Shared write helpers
// ---------------------------------------------------------------------------

internal static class PlanWriter
{
    public static void Apply(BillingPlan plan, PlanInput input, string category)
    {
        plan.NameAr = input.NameAr.Trim();
        plan.NameEn = input.NameEn.Trim();
        plan.DescriptionAr = string.IsNullOrWhiteSpace(input.DescriptionAr) ? null : input.DescriptionAr.Trim();
        plan.DescriptionEn = string.IsNullOrWhiteSpace(input.DescriptionEn) ? null : input.DescriptionEn.Trim();
        plan.AppliesTo = BillingJson.Write((input.AppliesTo ?? []).Select(a => BillingProviderTypes.All.First(t => string.Equals(t, a, StringComparison.OrdinalIgnoreCase))).Distinct().ToList());
        plan.Features = BillingJson.Write((input.Features ?? []).Distinct().ToList());
        plan.IncludesPlanIds = BillingJson.Write((input.IncludesPlanIds ?? []).Distinct().ToList());
        plan.LegacyEntityTypes = (input.LegacyEntityTypes ?? []).Count == 0 ? null : string.Join(",", input.LegacyEntityTypes!.Distinct());
        plan.PointsRule = category == PlanCategories.Loyalty && input.PointsRule is not null
            ? BillingJson.Write(PointsRuleValidator.Canonical(input.PointsRule))
            : null;
        plan.TrialDays = input.TrialDays;
        plan.TrialScans = category == PlanCategories.Loyalty ? input.TrialScans : 0;
        plan.IsActive = input.IsActive;
        plan.IsVisible = input.IsVisible;
        plan.SortOrder = input.SortOrder;
        plan.BadgeColor = input.BadgeColor;
        plan.DailySoftCap = input.DailySoftCap;
        plan.TargetMarginPercent = input.TargetMarginPercent;
    }

    public static async Task<PlanSaveResult> LogAsync(IApplicationDbContext db, BillingPlan plan, PlanDiff.Shape before, int? userId, string? note, CancellationToken ct)
    {
        var changes = PlanDiff.Diff(before, PlanDiff.Of(plan));
        if (changes.Count > 0)
        {
            db.BillingPlanChangeLogs.Add(new BillingPlanChangeLog
            {
                BillingPlanId = plan.Id,
                ChangedByUserId = userId,
                ChangedAt = DateTime.UtcNow,
                Changes = BillingJson.Write(changes),
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
            });
            await db.SaveChanges(ct);
        }

        var subscribers = (await PlanSubscribers.ListAsync(db, [plan], ct)).Count;
        var featuresChanged = changes.Any(c => c.Field == "features");
        return new PlanSaveResult(PlanDtoMapper.Map(plan, subscribers), changes, featuresChanged ? subscribers : 0, PlanDiff.TouchesPricing(changes));
    }
}
