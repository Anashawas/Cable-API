using Application.CableGifts.Commands.ManageGifts;
using Application.Common.Security;
using Application.Common.Utilities;
using Cable.Core;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using FluentValidation;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Application.Promos.Commands.ManagePromos;

/// <summary>The admin campaign form (§2.3–2.6). Dates without an offset are Jordan time.</summary>
public record PromoCampaignInput(
    string TitleAr,
    string TitleEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string? ScopeLabelAr,
    string? ScopeLabelEn,
    decimal CableSharePercent,
    decimal? CableBudgetJod,
    PromoTargetScope StationsScope,
    List<int>? StationIds,
    PromoTargetScope ServiceProvidersScope,
    List<int>? ServiceProviderIds,
    PromoEffectType EffectType,
    decimal EffectValue,
    decimal? MaxDiscountJod,
    /// <summary>Version 1: ["PartnerScan"] (the default when empty).</summary>
    List<string>? AppliesTo,
    List<int>? ServiceCategoryIds,
    /// <summary>Provider cities (Arabic or English names); empty = every city.</summary>
    List<string>? Cities,
    decimal? MinAmountJod,
    DateTime? StartsAt,
    DateTime? EndsAt,
    int? ValidDaysAfterClaim,
    int? TotalLimit,
    int? PerUserLimit,
    bool NewUsersOnly,
    PromoCodeMode CodeMode,
    /// <summary>Public mode only: the code users type (CABLE20).</summary>
    string? PublicCode,
    bool IsActive = true);

public class PromoCampaignInputValidator : AbstractValidator<PromoCampaignInput>
{
    public PromoCampaignInputValidator()
    {
        RuleFor(x => x.TitleAr).NotEmpty().MaximumLength(150);
        RuleFor(x => x.TitleEn).NotEmpty().MaximumLength(150);
        RuleFor(x => x.DescriptionAr).MaximumLength(1000);
        RuleFor(x => x.DescriptionEn).MaximumLength(1000);
        RuleFor(x => x.ScopeLabelAr).MaximumLength(200);
        RuleFor(x => x.ScopeLabelEn).MaximumLength(200);
    }
}

/// <summary>Validation (pure rules + the ids / code that need the database) and mapping, shared by create and update.</summary>
public static class PromoCampaignWriter
{
    public static async Task ValidateAndApplyAsync(IApplicationDbContext db, PromoCampaign campaign, PromoCampaignInput input,
        CancellationToken ct)
    {
        var stationIds = input.StationsScope == PromoTargetScope.Selected ? (input.StationIds ?? []).Distinct().ToList() : [];
        var providerIds = input.ServiceProvidersScope == PromoTargetScope.Selected ? (input.ServiceProviderIds ?? []).Distinct().ToList() : [];
        var categoryIds = (input.ServiceCategoryIds ?? []).Distinct().ToList();
        var appliesTo = input.AppliesTo is { Count: > 0 } ? input.AppliesTo : [PromoRules.PartnerScan];
        var normalizedCode = input.CodeMode == PromoCodeMode.Public ? SafeCode.Normalize(input.PublicCode) : null;

        var errors = PromoRules.Validate(input.EffectType, input.EffectValue, input.MaxDiscountJod,
            input.CableSharePercent, input.CableBudgetJod,
            input.StationsScope, stationIds, input.ServiceProvidersScope, providerIds,
            appliesTo, JordanTime.ToUtc(input.StartsAt), JordanTime.ToUtc(input.EndsAt),
            input.ValidDaysAfterClaim, input.TotalLimit, input.PerUserLimit, input.MinAmountJod,
            input.CodeMode, normalizedCode);

        if (stationIds.Count > 0)
        {
            var found = await db.ChargingPoints.AsNoTracking().CountAsync(x => stationIds.Contains(x.Id) && !x.IsDeleted, ct);
            if (found != stationIds.Count) errors["StationIds"] = ["Some stations were not found"];
        }
        if (providerIds.Count > 0)
        {
            var found = await db.ServiceProviders.AsNoTracking().CountAsync(x => providerIds.Contains(x.Id) && !x.IsDeleted, ct);
            if (found != providerIds.Count) errors["ServiceProviderIds"] = ["Some service providers were not found"];
        }
        if (categoryIds.Count > 0)
        {
            var found = await db.ServiceCategories.AsNoTracking().CountAsync(x => categoryIds.Contains(x.Id) && !x.IsDeleted, ct);
            if (found != categoryIds.Count) errors["ServiceCategoryIds"] = ["Some service categories were not found"];
        }
        if (!string.IsNullOrEmpty(normalizedCode) && !errors.ContainsKey("PublicCode")
            && await db.PromoCampaigns.AnyAsync(c => c.PublicCodeNormalized == normalizedCode && !c.IsDeleted && c.Id != campaign.Id, ct))
            errors["PublicCode"] = ["Another campaign already uses this code"];

        List<string> cities = [];
        try { cities = CableGiftInputMapper.ResolveCities(input.Cities); }
        catch (DataValidationException ex) { foreach (var e in ex.Errors) errors[e.Key] = e.Value; }

        if (errors.Count > 0)
            throw new DataValidationException(errors);

        campaign.TitleAr = input.TitleAr.Trim();
        campaign.TitleEn = input.TitleEn.Trim();
        campaign.DescriptionAr = Clean(input.DescriptionAr);
        campaign.DescriptionEn = Clean(input.DescriptionEn);
        campaign.ScopeLabelAr = Clean(input.ScopeLabelAr);
        campaign.ScopeLabelEn = Clean(input.ScopeLabelEn);
        campaign.CableSharePercent = input.CableSharePercent;
        campaign.CableBudgetJod = input.CableBudgetJod;
        campaign.StationsScope = input.StationsScope;
        campaign.StationIds = CsvList.Join(stationIds);
        campaign.ServiceProvidersScope = input.ServiceProvidersScope;
        campaign.ServiceProviderIds = CsvList.Join(providerIds);
        campaign.EffectType = input.EffectType;
        campaign.EffectValue = input.EffectValue;
        campaign.MaxDiscountJod = input.MaxDiscountJod;
        campaign.AppliesTo = PromoRules.PartnerScan;   // the only kind version 1 accepts (validated above)
        campaign.ServiceCategoryIds = CsvList.Join(categoryIds);
        campaign.Cities = CsvList.Join(cities);
        campaign.MinAmountJod = input.MinAmountJod;
        campaign.StartsAt = JordanTime.ToUtc(input.StartsAt);
        campaign.EndsAt = JordanTime.ToUtc(input.EndsAt);
        campaign.ValidDaysAfterClaim = input.ValidDaysAfterClaim;
        campaign.TotalLimit = input.TotalLimit;
        campaign.PerUserLimit = input.PerUserLimit ?? 1;
        campaign.NewUsersOnly = input.NewUsersOnly;
        campaign.CodeMode = input.CodeMode;
        campaign.PublicCode = input.CodeMode == PromoCodeMode.Public ? input.PublicCode?.Trim().ToUpperInvariant() : null;
        campaign.PublicCodeNormalized = string.IsNullOrEmpty(normalizedCode) ? null : normalizedCode;
        campaign.IsActive = input.IsActive;
        if (input.IsActive) campaign.DeactivatedReason = null;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>POST /api/admin/promos — returns the new campaign id.</summary>
public record CreatePromoCampaignCommand(PromoCampaignInput Input) : IRequest<int>;

public class CreatePromoCampaignCommandValidator : AbstractValidator<CreatePromoCampaignCommand>
{
    public CreatePromoCampaignCommandValidator() => RuleFor(x => x.Input).NotNull().SetValidator(new PromoCampaignInputValidator());
}

public class CreatePromoCampaignCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<CreatePromoCampaignCommand, int>
{
    public async Task<int> Handle(CreatePromoCampaignCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var campaign = new PromoCampaign();
        await PromoCampaignWriter.ValidateAndApplyAsync(db, campaign, request.Input, cancellationToken);
        db.PromoCampaigns.Add(campaign);
        await db.SaveChanges(cancellationToken);
        return campaign.Id;
    }
}

/// <summary>
/// PUT /api/admin/promos/{id}. After the first use the effect (type, value, cap, Cable's share) is locked; after the
/// first claim or generated code the code mode is locked; the budget can't go below what Cable already spent.
/// </summary>
public record UpdatePromoCampaignCommand(int Id, PromoCampaignInput Input) : IRequest;

public class UpdatePromoCampaignCommandValidator : AbstractValidator<UpdatePromoCampaignCommand>
{
    public UpdatePromoCampaignCommandValidator() => RuleFor(x => x.Input).NotNull().SetValidator(new PromoCampaignInputValidator());
}

public class UpdatePromoCampaignCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<UpdatePromoCampaignCommand>
{
    public async Task Handle(UpdatePromoCampaignCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var campaign = await db.PromoCampaigns.FirstOrDefaultAsync(c => c.Id == request.Id && !c.IsDeleted, cancellationToken)
                       ?? throw new NotFoundException($"Promo campaign {request.Id} was not found");
        var input = request.Input;

        if (campaign.UsesCount > 0 && (campaign.EffectType != input.EffectType || campaign.EffectValue != input.EffectValue
                                       || campaign.MaxDiscountJod != input.MaxDiscountJod
                                       || campaign.CableSharePercent != input.CableSharePercent))
            throw new DataValidationException("EffectType",
                "The effect (type, value, discount cap and Cable's share) can't change after the first use");

        var hasCodes = await db.PromoCodes.AnyAsync(c => c.PromoCampaignId == campaign.Id, cancellationToken);
        if ((campaign.ClaimsCount > 0 || hasCodes) && campaign.CodeMode != input.CodeMode)
            throw new DataValidationException("CodeMode", "The code mode can't change once codes were generated or claimed");

        if (input.CableBudgetJod.HasValue && input.CableBudgetJod.Value < campaign.CableSpentJod)
            throw new DataValidationException("CableBudgetJod",
                $"The budget can't be below the {campaign.CableSpentJod:F3} JOD Cable already paid");

        if (input.IsActive && PromoRules.IsBudgetExhausted(input.CableBudgetJod, campaign.CableSpentJod))
            throw new DataValidationException("CableBudgetJod", "The budget is used up — raise it before activating the campaign");

        await PromoCampaignWriter.ValidateAndApplyAsync(db, campaign, input, cancellationToken);
        await db.SaveChanges(cancellationToken);
    }
}

/// <summary>PATCH /api/admin/promos/{id}/active.</summary>
public record SetPromoCampaignActiveCommand(int Id, bool IsActive) : IRequest;

public class SetPromoCampaignActiveCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<SetPromoCampaignActiveCommand>
{
    public async Task Handle(SetPromoCampaignActiveCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var campaign = await db.PromoCampaigns.FirstOrDefaultAsync(c => c.Id == request.Id && !c.IsDeleted, cancellationToken)
                       ?? throw new NotFoundException($"Promo campaign {request.Id} was not found");

        if (request.IsActive && PromoRules.IsBudgetExhausted(campaign.CableBudgetJod, campaign.CableSpentJod))
            throw new DataValidationException("CableBudgetJod", "The budget is used up — raise it before activating the campaign");

        campaign.IsActive = request.IsActive;
        campaign.DeactivatedReason = null;
        await db.SaveChanges(cancellationToken);
    }
}

public record GeneratePromoCodesResult(int Generated, int TotalCodes, Guid BatchId);

/// <summary>POST /api/admin/promos/{id}/codes { "count": 100 } — unique PRM- codes (Unique mode), random and non-sequential.</summary>
public record GeneratePromoCodesCommand(int Id, int Count) : IRequest<GeneratePromoCodesResult>;

public class GeneratePromoCodesCommandValidator : AbstractValidator<GeneratePromoCodesCommand>
{
    public GeneratePromoCodesCommandValidator() => RuleFor(x => x.Count).InclusiveBetween(1, 5000);
}

public class GeneratePromoCodesCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GeneratePromoCodesCommand, GeneratePromoCodesResult>
{
    public async Task<GeneratePromoCodesResult> Handle(GeneratePromoCodesCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var campaign = await db.PromoCampaigns.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.Id && !c.IsDeleted, cancellationToken)
                       ?? throw new NotFoundException($"Promo campaign {request.Id} was not found");
        if (campaign.CodeMode != PromoCodeMode.Unique)
            throw new DataValidationException("CodeMode", "Unique codes are only for campaigns in Unique code mode");

        var batchId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var codes = new Dictionary<string, string>();   // normalized → display
        while (codes.Count < request.Count)
        {
            var missing = request.Count - codes.Count;
            var candidates = Enumerable.Range(0, missing).Select(_ => PromoRules.NewUniqueCode())
                .Select(c => (Display: c, Normalized: SafeCode.Normalize(c)))
                .Where(c => !codes.ContainsKey(c.Normalized))
                .DistinctBy(c => c.Normalized)
                .ToList();
            var normalized = candidates.Select(c => c.Normalized).ToList();
            var taken = await db.PromoCodes.AsNoTracking().Where(c => normalized.Contains(c.NormalizedCode))
                .Select(c => c.NormalizedCode).ToListAsync(cancellationToken);
            foreach (var c in candidates.Where(c => !taken.Contains(c.Normalized)))
                codes[c.Normalized] = c.Display;
        }

        db.PromoCodes.AddRange(codes.Select(c => new PromoCode
        {
            PromoCampaignId = campaign.Id,
            Code = c.Value,
            NormalizedCode = c.Key,
            CreatedAt = now,
            BatchId = batchId
        }));
        await db.SaveChanges(cancellationToken);

        var total = await db.PromoCodes.CountAsync(c => c.PromoCampaignId == campaign.Id, cancellationToken);
        return new GeneratePromoCodesResult(codes.Count, total, batchId);
    }
}

public record AssignPromoResult(int Assigned, int Skipped, Guid BatchId);

/// <summary>
/// POST /api/admin/promos/{id}/assign { "target": "All|City|Users", "cities": [], "userIds": [], "phones": [] } —
/// puts the promo straight into "My codes" (with a push). Skips users who already hold it available, reached the
/// per-user limit, or (newUsersOnly) are not new. Assignments don't count against totalLimit.
/// </summary>
public record AssignPromoCommand(int Id, PromoAssignTarget Target, List<string>? Cities, List<int>? UserIds, List<string>? Phones)
    : IRequest<AssignPromoResult>;

public class AssignPromoCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IBackgroundJobClient jobs)
    : IRequestHandler<AssignPromoCommand, AssignPromoResult>
{
    private const int ChunkSize = 1000;

    public async Task<AssignPromoResult> Handle(AssignPromoCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var now = DateTime.UtcNow;
        var campaign = await db.PromoCampaigns.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.Id && !c.IsDeleted, cancellationToken)
                       ?? throw new NotFoundException($"Promo campaign {request.Id} was not found");
        if (!campaign.IsActive || (campaign.EndsAt.HasValue && campaign.EndsAt <= now))
            throw new DataValidationException("Id", "Only an active, running campaign can be assigned");

        var users = db.UserAccounts.AsNoTracking()
            .Where(u => !u.IsDeleted && u.IsActive && u.RoleId != AdminRoleGuard.AdminRoleId);
        if (campaign.NewUsersOnly)
        {
            var since = now.AddDays(-PromoRules.NewUserWindowDays);
            users = users.Where(u => u.CreatedAt >= since);
        }

        List<int> candidates;
        switch (request.Target)
        {
            case PromoAssignTarget.All:
                candidates = await users.Select(u => u.Id).ToListAsync(cancellationToken);
                break;
            case PromoAssignTarget.City:
                var cities = CableGiftInputMapper.ResolveCities(request.Cities);
                if (cities.Count == 0) throw new DataValidationException("Cities", "Pick at least one city");
                var withCity = await users.Where(u => u.City != null).Select(u => new { u.Id, u.City }).ToListAsync(cancellationToken);
                candidates = withCity.Where(u => JordanCityResolver.Resolve(u.City) is { } c && cities.Contains(c)).Select(u => u.Id).ToList();
                break;
            case PromoAssignTarget.Users:
                var ids = (request.UserIds ?? []).Distinct().ToList();
                var phones = (request.Phones ?? []).Where(p => !string.IsNullOrWhiteSpace(p))
                    .SelectMany(p => new[] { p.Trim(), PhoneNumberUtility.NormalizePhoneNumber(p) })
                    .Where(p => p is not null).Select(p => p!).Distinct().ToList();
                if (ids.Count == 0 && phones.Count == 0)
                    throw new DataValidationException("UserIds", "Send userIds or phones");
                candidates = await users.Where(u => ids.Contains(u.Id) || (u.Phone != null && phones.Contains(u.Phone)))
                    .Select(u => u.Id).ToListAsync(cancellationToken);
                break;
            default:
                throw new DataValidationException("Target", "Target must be All, City or Users");
        }

        // Skip: an available one already, or the per-user limit reached.
        var held = await db.UserPromos.AsNoTracking()
            .Where(u => u.PromoCampaignId == campaign.Id && !u.IsDeleted)
            .GroupBy(u => u.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count(), HasAvailable = g.Any(x => x.Status == UserPromoStatus.Available) })
            .ToDictionaryAsync(x => x.UserId, cancellationToken);
        var targets = candidates.Distinct()
            .Where(id => !held.TryGetValue(id, out var h) || (!h.HasAvailable && h.Count < campaign.PerUserLimit))
            .ToList();

        var batchId = Guid.NewGuid();
        var expiresAt = PromoRules.ExpiryFor(campaign.EndsAt, campaign.ValidDaysAfterClaim, now);
        foreach (var chunk in targets.Chunk(ChunkSize))
        {
            db.UserPromos.AddRange(chunk.Select(userId => new UserPromo
            {
                UserId = userId,
                PromoCampaignId = campaign.Id,
                Source = UserPromoSource.Assigned,
                ClaimedAt = now,
                ExpiresAt = expiresAt,
                Status = UserPromoStatus.Available,
                AssignmentBatchId = batchId
            }));
            await db.SaveChanges(cancellationToken);
        }

        if (targets.Count > 0)
            jobs.Enqueue<IGiftsAndPromosJobs>(j => j.NotifyPromoAssignedAsync(batchId, CancellationToken.None));

        return new AssignPromoResult(targets.Count, candidates.Distinct().Count() - targets.Count, batchId);
    }
}

/// <summary>PATCH /api/admin/user-promos/{id}/revoke { "reason": "…" } — an available promo only.</summary>
public record RevokeUserPromoCommand(int Id, string? Reason) : IRequest;

public class RevokeUserPromoCommandValidator : AbstractValidator<RevokeUserPromoCommand>
{
    public RevokeUserPromoCommandValidator() => RuleFor(x => x.Reason).MaximumLength(500);
}

public class RevokeUserPromoCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<RevokeUserPromoCommand>
{
    public async Task Handle(RevokeUserPromoCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var userPromo = await db.UserPromos.FirstOrDefaultAsync(u => u.Id == request.Id && !u.IsDeleted, cancellationToken)
                        ?? throw new NotFoundException($"User promo {request.Id} was not found");
        if (userPromo.Status != UserPromoStatus.Available)
            throw new ConflictException("PROMO_NOT_AVAILABLE", $"Only an available promo can be revoked (this one is {userPromo.Status})");

        userPromo.Status = UserPromoStatus.Revoked;
        userPromo.RevokedAt = DateTime.UtcNow;
        userPromo.RevokedByUserId = currentUser.UserId;
        userPromo.RevokeReason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        await db.SaveChanges(cancellationToken);
    }
}
