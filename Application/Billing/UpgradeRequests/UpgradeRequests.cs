using Application.Billing.Jobs;
using Application.Billing.Plans;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Billing.UpgradeRequests;

public record UpgradeRequestDto(
    int Id,
    string ProviderType,
    int ProviderId,
    string? ProviderName,
    int PlanId,
    string Tier,
    string PlanName,
    string Kind,
    int? Months,
    int? PriceOptionId,
    string? Note,
    string Status,
    int RequestedByUserId,
    string? RequestedByName,
    string? RequestedByPhone,
    DateTime CreatedAt,
    int? HandledByUserId,
    DateTime? HandledAt,
    string? RejectReason,
    int? FulfilledPaymentId,
    int? FulfilledPlanSubscriptionId);

public record CreateUpgradeRequestResult(int Id, string Status);

// ---------------------------------------------------------------------------
// Partner: "upgrade / renew / buy a bundle" (§2.5). 409 while one is pending.
// ---------------------------------------------------------------------------

/// <param name="Tier">Plan key (premium, connect, loyalty_bundle_400…). Or pass PlanId.</param>
public record CreateUpgradeRequestCommand(
    string ProviderType, int ProviderId, string? Tier = null, int? PlanId = null, int? Months = null, int? PriceOptionId = null, string? Note = null)
    : IRequest<CreateUpgradeRequestResult>;

public class CreateUpgradeRequestCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    INotificationService notificationService,
    ILogger<CreateUpgradeRequestCommandHandler> logger)
    : IRequestHandler<CreateUpgradeRequestCommand, CreateUpgradeRequestResult>
{
    public async Task<CreateUpgradeRequestResult> Handle(CreateUpgradeRequestCommand request, CancellationToken cancellationToken)
    {
        await BillingAccessGuard.EnsureOwnerOrAdminAsync(applicationDbContext, currentUserService, request.ProviderType, request.ProviderId, cancellationToken);
        var userId = currentUserService.UserId!.Value;
        if ((request.Note?.Length ?? 0) > 1000) throw new DataValidationException("Note", Lang.Pick("الملاحظة حتى 1000 حرف", "Note is up to 1000 characters"));
        if (request.Months is < 1 or > 24) throw new DataValidationException("Months", Lang.Pick("المدة بين 1 و 24 شهر", "months must be between 1 and 24"));

        var plans = await PlanCatalog.LoadAllAsync(applicationDbContext, cancellationToken);
        var key = request.Tier?.Trim().ToLowerInvariant();
        var plan = (request.PlanId is int pid ? plans.FirstOrDefault(p => p.Id == pid) : plans.FirstOrDefault(p => p.Key == key))
                   ?? throw new NotFoundException(Lang.Pick("الباقة غير موجودة", "Plan not found"));
        if (plan.Key == PlanKeys.Free)
            throw new DataValidationException("Tier", Lang.Pick("الباقة المجانية لا تحتاج طلب", "The free plan needs no request"));
        PlanCatalog.EnsureSellable(plan, request.ProviderType);
        if (request.PriceOptionId is not null) PlanCatalog.RequireOption(plan, request.PriceOptionId);

        var pending = await applicationDbContext.PlanUpgradeRequests.AsNoTracking()
            .AnyAsync(r => r.ProviderType == request.ProviderType && r.ProviderId == request.ProviderId
                           && r.Status == UpgradeRequestStatuses.Pending && !r.IsDeleted, cancellationToken);
        if (pending)
            throw new ConflictException(BillingErrorCodes.UpgradeRequestPending,
                Lang.Pick("طلبك قيد المتابعة — فريق كيبل رح يتواصل معك", "Your request is already being followed up — Cable's team will contact you"));

        var snapshot = await ProviderPlanResolver.LoadAsync(applicationDbContext, request.ProviderType, request.ProviderId, cancellationToken);
        var kind = plan.Category == PlanCategories.Loyalty ? UpgradeRequestKinds.Bundle
            : snapshot.OnSubscriptionPlans().Any(p => p.Id == plan.Id) ? UpgradeRequestKinds.Renew
            : UpgradeRequestKinds.Upgrade;

        var row = new PlanUpgradeRequest
        {
            ProviderType = request.ProviderType,
            ProviderId = request.ProviderId,
            BillingPlanId = plan.Id,
            PlanKey = plan.Key,
            PriceOptionId = request.PriceOptionId,
            Months = request.Months ?? plan.PriceOptions.FirstOrDefault(o => o.Id == request.PriceOptionId)?.Months,
            Kind = kind,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            Status = UpgradeRequestStatuses.Pending,
            RequestedByUserId = userId
        };
        applicationDbContext.PlanUpgradeRequests.Add(row);
        await applicationDbContext.SaveChanges(cancellationToken);

        // Tell the admins; a failed push must not lose the request (it is already in their list).
        try
        {
            var owner = await BillingAccessGuard.OwnerOfAsync(applicationDbContext, request.ProviderType, request.ProviderId, cancellationToken);
            var notifier = new BillingNotifier(applicationDbContext, notificationService, logger);
            await notifier.NotifyAdminsAsync($"upgradeRequest:{row.Id}", "upgradeRequest", request.ProviderType, request.ProviderId, null,
                lang => (Lang.Pick(lang, "طلب ترقية جديد", "New upgrade request"),
                         Lang.Pick(lang, $"{owner.Name}: {plan.NameAr}{(row.Months is int m ? $" — {m} شهر" : "")}", $"{owner.Name}: {plan.NameEn}{(row.Months is int m2 ? $" — {m2} months" : "")}")),
                new Dictionary<string, string> { ["upgradeRequestId"] = row.Id.ToString() }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Upgrade request {Id}: admin notification failed", row.Id);
        }

        return new CreateUpgradeRequestResult(row.Id, row.Status);
    }
}

// ---------------------------------------------------------------------------
// Admin: list, reject with a reason, mark fulfilled
// ---------------------------------------------------------------------------

public record GetUpgradeRequestsRequest(string? Status = null, string? ProviderType = null, int? ProviderId = null, int? Page = null, int? PageSize = null)
    : IRequest<PagedResult<UpgradeRequestDto>>;

public class GetUpgradeRequestsRequestHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<GetUpgradeRequestsRequest, PagedResult<UpgradeRequestDto>>
{
    public async Task<PagedResult<UpgradeRequestDto>> Handle(GetUpgradeRequestsRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        var q = applicationDbContext.PlanUpgradeRequests.AsNoTracking().Where(r => !r.IsDeleted);
        if (!string.IsNullOrWhiteSpace(request.Status)) q = q.Where(r => r.Status == request.Status);
        if (!string.IsNullOrWhiteSpace(request.ProviderType)) q = q.Where(r => r.ProviderType == request.ProviderType);
        if (request.ProviderId is int pid) q = q.Where(r => r.ProviderId == pid);
        var rows = await q.OrderBy(r => r.Status == UpgradeRequestStatuses.Pending ? 0 : 1).ThenByDescending(r => r.CreatedAt).ToListAsync(cancellationToken);
        var mapped = await UpgradeRequestMapper.MapAsync(applicationDbContext, rows, cancellationToken);
        return mapped.ToOptionallyPaginated(request.Page, request.PageSize);
    }
}

public record RejectUpgradeRequestCommand(int Id, string Reason) : IRequest<UpgradeRequestDto>;

public class RejectUpgradeRequestCommandHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<RejectUpgradeRequestCommand, UpgradeRequestDto>
{
    public async Task<UpgradeRequestDto> Handle(RejectUpgradeRequestCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new DataValidationException("Reason", Lang.Pick("سبب الرفض مطلوب", "A reason is required"));
        var row = await UpgradeRequestMapper.LoadPendingAsync(applicationDbContext, request.Id, cancellationToken);
        row.Status = UpgradeRequestStatuses.Rejected;
        row.RejectReason = request.Reason.Trim();
        row.HandledAt = DateTime.UtcNow;
        row.HandledByUserId = currentUserService.UserId;
        await applicationDbContext.SaveChanges(cancellationToken);
        return (await UpgradeRequestMapper.MapAsync(applicationDbContext, [row], cancellationToken))[0];
    }
}

/// <summary>Manual "mark fulfilled" — normally recording the payment does it automatically.</summary>
public record FulfilUpgradeRequestCommand(int Id, int? PaymentId = null, int? PlanSubscriptionId = null) : IRequest<UpgradeRequestDto>;

public class FulfilUpgradeRequestCommandHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<FulfilUpgradeRequestCommand, UpgradeRequestDto>
{
    public async Task<UpgradeRequestDto> Handle(FulfilUpgradeRequestCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        var row = await UpgradeRequestMapper.LoadPendingAsync(applicationDbContext, request.Id, cancellationToken);
        row.Status = UpgradeRequestStatuses.Fulfilled;
        row.HandledAt = DateTime.UtcNow;
        row.HandledByUserId = currentUserService.UserId;
        row.FulfilledPaymentId = request.PaymentId;
        row.FulfilledPlanSubscriptionId = request.PlanSubscriptionId;
        await applicationDbContext.SaveChanges(cancellationToken);
        return (await UpgradeRequestMapper.MapAsync(applicationDbContext, [row], cancellationToken))[0];
    }
}

internal static class UpgradeRequestMapper
{
    public static async Task<PlanUpgradeRequest> LoadPendingAsync(IApplicationDbContext db, int id, CancellationToken ct)
    {
        var row = await db.PlanUpgradeRequests.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, ct)
                  ?? throw new NotFoundException(Lang.Pick($"لم يتم العثور على الطلب رقم {id}", $"Upgrade request {id} not found"));
        if (row.Status != UpgradeRequestStatuses.Pending)
            throw new UnprocessableException(BillingErrorCodes.SubscriptionNotActive, Lang.Pick("هذا الطلب تمت معالجته من قبل", "This request was already handled"));
        return row;
    }

    public static async Task<List<UpgradeRequestDto>> MapAsync(IApplicationDbContext db, IReadOnlyCollection<PlanUpgradeRequest> rows, CancellationToken ct)
    {
        var names = await ProviderNames.ResolveAsync(db, rows.Select(r => (r.ProviderType, r.ProviderId)), ct);
        var planIds = rows.Select(r => r.BillingPlanId).Distinct().ToList();
        var plans = await db.BillingPlans.AsNoTracking().Where(p => planIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var userIds = rows.Select(r => r.RequestedByUserId).Distinct().ToList();
        var users = await db.UserAccounts.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Name, u.Phone }).ToDictionaryAsync(u => u.Id, ct);

        return rows.Select(r => new UpgradeRequestDto(
            r.Id, r.ProviderType, r.ProviderId, names.GetValueOrDefault((r.ProviderType, r.ProviderId)),
            r.BillingPlanId, r.PlanKey, plans.TryGetValue(r.BillingPlanId, out var p) ? PlanCatalog.Name(p) : r.PlanKey,
            r.Kind, r.Months, r.PriceOptionId, r.Note, r.Status, r.RequestedByUserId,
            users.TryGetValue(r.RequestedByUserId, out var u) ? u.Name : null,
            users.TryGetValue(r.RequestedByUserId, out var u2) ? PhoneNumberUtility.ToE164OrOriginal(u2.Phone) : null,
            r.CreatedAt, r.HandledByUserId, r.HandledAt, r.RejectReason, r.FulfilledPaymentId, r.FulfilledPlanSubscriptionId)).ToList();
    }
}
