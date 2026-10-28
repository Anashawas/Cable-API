using Application.Billing.Subscriptions;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Billing.Bundles;

public record AgreementBundlesDto(
    int AgreementId,
    string ProviderType,
    int ProviderId,
    string BillingModel,
    decimal? MinimumTransactionAmount,
    LoyaltyBundleDto? Current,
    List<PlanSubscriptionDto> History);

// ---------------------------------------------------------------------------
// Sell a bundle on a partner agreement (admin). Price debited from the wallet.
// ---------------------------------------------------------------------------

public record PurchaseBundleCommand(int AgreementId, int PlanId, int PriceOptionId, int? UpgradeRequestId = null, string? Note = null)
    : IRequest<PlanSubscriptionDto>;

public class PurchaseBundleCommandHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<PurchaseBundleCommand, PlanSubscriptionDto>
{
    public async Task<PlanSubscriptionDto> Handle(PurchaseBundleCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        var agreement = await BundleAdmin.LoadAgreementAsync(applicationDbContext, request.AgreementId, cancellationToken);
        var plan = await PlanCatalog.LoadAsync(applicationDbContext, request.PlanId, cancellationToken);
        var option = PlanCatalog.RequireOption(plan, request.PriceOptionId);

        var current = await LoyaltyBundleService.LatestAsync(applicationDbContext, agreement.ProviderType, agreement.ProviderId, cancellationToken);
        if (current is not null && current.BillingPlanId != plan.Id && LoyaltyBundleService.IsUsable(current, DateTime.UtcNow))
            PlanSubscriptionService.ThrowFor(BillingErrorCodes.ActiveSubscriptionExists);

        var record = await LoyaltyBundleService.PurchaseAsync(applicationDbContext, currentUserService.UserId, agreement.ProviderType, agreement.ProviderId,
            plan, option, false, null, 0, request.UpgradeRequestId, request.Note, cancellationToken);
        return (await PlanSubscriptionMapper.MapAsync(applicationDbContext, [record], cancellationToken))[0];
    }
}

public record GetAgreementBundlesRequest(int AgreementId) : IRequest<AgreementBundlesDto>;

public class GetAgreementBundlesRequestHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<GetAgreementBundlesRequest, AgreementBundlesDto>
{
    public async Task<AgreementBundlesDto> Handle(GetAgreementBundlesRequest request, CancellationToken cancellationToken)
    {
        var agreement = await BundleAdmin.LoadAgreementAsync(applicationDbContext, request.AgreementId, cancellationToken);
        // Admin, or the owner of the agreement's station / provider (their own bundle history).
        await BillingAccessGuard.EnsureOwnerOrAdminAsync(applicationDbContext, currentUserService, agreement.ProviderType, agreement.ProviderId, cancellationToken);

        var rows = await applicationDbContext.PlanSubscriptions.AsNoTracking()
            .Where(r => r.ProviderType == agreement.ProviderType && r.ProviderId == agreement.ProviderId
                        && r.PlanCategory == PlanCategories.Loyalty && !r.IsDeleted)
            .OrderByDescending(r => r.Id)
            .ToListAsync(cancellationToken);
        var history = await PlanSubscriptionMapper.MapAsync(applicationDbContext, rows, cancellationToken);
        var current = await LoyaltyBundleService.GetDtoAsync(applicationDbContext, agreement.ProviderType, agreement.ProviderId, cancellationToken);
        return new AgreementBundlesDto(agreement.Id, agreement.ProviderType, agreement.ProviderId, BillingModels.Normalize(agreement.BillingModel),
            agreement.MinimumTransactionAmount, current, history);
    }
}

// ---------------------------------------------------------------------------
// Switch the agreement between percentage and bundle (admin only).
// Decision 13: transactions already created keep the model they were created
// under (their commission is reserved on the row), so the open settlement closes
// on percentage and the bundle starts from this moment. Nothing switches back
// automatically when a bundle runs out.
// ---------------------------------------------------------------------------

public record SetBillingModelResult(int AgreementId, string BillingModel, decimal? MinimumTransactionAmount, int OpenPercentageCodes, LoyaltyBundleDto? Bundle);

public record SetBillingModelCommand(int AgreementId, string BillingModel, decimal? MinimumTransactionAmount = null) : IRequest<SetBillingModelResult>;

public class SetBillingModelCommandHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<SetBillingModelCommand, SetBillingModelResult>
{
    public async Task<SetBillingModelResult> Handle(SetBillingModelCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        if (!BillingModels.IsValid(request.BillingModel?.Trim().ToLowerInvariant()))
            throw new DataValidationException("BillingModel", Lang.Pick("الموديل percentage أو bundle", "billingModel must be percentage or bundle"));
        var model = BillingModels.Normalize(request.BillingModel);
        if (request.MinimumTransactionAmount is <= 0)
            throw new DataValidationException("MinimumTransactionAmount", Lang.Pick("أقل مبلغ يجب أن يكون أكبر من صفر", "minimumTransactionAmount must be greater than 0"));

        var agreement = await applicationDbContext.PartnerAgreements.FirstOrDefaultAsync(a => a.Id == request.AgreementId && !a.IsDeleted, cancellationToken)
                        ?? throw new NotFoundException(Lang.Pick($"لم يتم العثور على الاتفاقية رقم {request.AgreementId}", $"Partner agreement {request.AgreementId} not found"));

        if (request.MinimumTransactionAmount is decimal min) agreement.MinimumTransactionAmount = min;

        // Decision 12: a bundle needs a minimum amount (a 1-JOD scan costs the bundle as much as a 50-JOD one).
        if (model == BillingModels.Bundle && agreement.MinimumTransactionAmount is not > 0)
            agreement.MinimumTransactionAmount = await BillingSettingsProvider.GetBundleMinimumAmountAsync(applicationDbContext, cancellationToken);

        if (BillingModels.Normalize(agreement.BillingModel) != model)
        {
            agreement.BillingModel = model;
            agreement.BillingModelChangedAt = DateTime.UtcNow;
        }
        agreement.ModifiedAt = DateTime.UtcNow;
        agreement.ModifiedBy = currentUserService.UserId;
        await applicationDbContext.SaveChanges(cancellationToken);

        var now = DateTime.UtcNow;
        var open = await applicationDbContext.PartnerTransactions.AsNoTracking()
            .CountAsync(t => t.PartnerAgreementId == agreement.Id && t.Status == (int)PartnerTransactionStatus.Initiated
                             && t.CodeExpiresAt > now && !t.IsDeleted
                             && !applicationDbContext.PartnerTransactionBundleScans.Any(m => m.PartnerTransactionId == t.Id), cancellationToken);
        var bundle = await LoyaltyBundleService.GetDtoAsync(applicationDbContext, agreement.ProviderType, agreement.ProviderId, cancellationToken);
        return new SetBillingModelResult(agreement.Id, model, agreement.MinimumTransactionAmount, open, bundle);
    }
}

// ---------------------------------------------------------------------------
// Admin billing alerts (soft cap exceeded, break-even passed, upgrade requests)
// ---------------------------------------------------------------------------

public record BillingAlertDto(int Id, string Kind, string? ProviderType, int? ProviderId, string? ProviderName, int? PlanSubscriptionId,
    string? Title, string? Body, DateTime CreatedAt, DateTime? AcknowledgedAt);

public record GetBillingAlertsRequest(bool UnacknowledgedOnly = false, int? Page = null, int? PageSize = null) : IRequest<PagedResult<BillingAlertDto>>;

public class GetBillingAlertsRequestHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<GetBillingAlertsRequest, PagedResult<BillingAlertDto>>
{
    public async Task<PagedResult<BillingAlertDto>> Handle(GetBillingAlertsRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        var q = applicationDbContext.BillingEvents.AsNoTracking().Where(e => e.Audience == "admin");
        if (request.UnacknowledgedOnly) q = q.Where(e => e.AcknowledgedAt == null);
        var rows = await q.OrderByDescending(e => e.CreatedAt).Take(1000).ToListAsync(cancellationToken);
        var names = await Plans.ProviderNames.ResolveAsync(applicationDbContext,
            rows.Where(r => r.ProviderType != null && r.ProviderId != null).Select(r => (r.ProviderType!, r.ProviderId!.Value)), cancellationToken);
        return rows.Select(r => new BillingAlertDto(r.Id, r.Kind, r.ProviderType, r.ProviderId,
                r.ProviderType != null && r.ProviderId != null ? names.GetValueOrDefault((r.ProviderType, r.ProviderId.Value)) : null,
                r.PlanSubscriptionId, r.Title, r.Body, r.CreatedAt, r.AcknowledgedAt))
            .ToList().ToOptionallyPaginated(request.Page, request.PageSize);
    }
}

public record AcknowledgeBillingAlertCommand(int Id) : IRequest;

public class AcknowledgeBillingAlertCommandHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<AcknowledgeBillingAlertCommand>
{
    public async Task Handle(AcknowledgeBillingAlertCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        var row = await applicationDbContext.BillingEvents.FirstOrDefaultAsync(e => e.Id == request.Id && e.Audience == "admin", cancellationToken)
                  ?? throw new NotFoundException(Lang.Pick("التنبيه غير موجود", "Alert not found"));
        row.AcknowledgedAt = DateTime.UtcNow;
        row.AcknowledgedByUserId = currentUserService.UserId;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}

internal static class BundleAdmin
{
    public static async Task<PartnerAgreement> LoadAgreementAsync(IApplicationDbContext db, int id, CancellationToken ct) =>
        await db.PartnerAgreements.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted, ct)
        ?? throw new NotFoundException(Lang.Pick($"لم يتم العثور على الاتفاقية رقم {id}", $"Partner agreement {id} not found"));
}
