using Application.Common.Security;
using Cable.Core;
using Cable.Core.Constants;
using Cable.Core.Emuns;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Subscriptions.Queries;

// ---------------------------------------------------------------------------
// One subscription, with its payments
// ---------------------------------------------------------------------------

public record GetSubscriptionRequest(string EntityType, int EntityId) : IRequest<SubscriptionDto>;

public class GetSubscriptionRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    IUploadFileService files)
    : IRequestHandler<GetSubscriptionRequest, SubscriptionDto>
{
    public async Task<SubscriptionDto> Handle(GetSubscriptionRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var sub = await applicationDbContext.Subscriptions.AsNoTracking()
                      .Include(s => s.Payments).ThenInclude(p => p.Payer).ThenInclude(p => p.UserAccount)
                      .FirstOrDefaultAsync(s => s.EntityType == request.EntityType && s.EntityId == request.EntityId && !s.IsDeleted,
                          cancellationToken)
                  ?? throw new NotFoundException($"No subscription for {request.EntityType} #{request.EntityId}");

        return (await SubscriptionDtoMapper.MapAsync(applicationDbContext, files, [sub], cancellationToken))[0];
    }
}

// ---------------------------------------------------------------------------
// Renewals dashboard
// ---------------------------------------------------------------------------

public record MonthlyCollectionDto(
    int Year,
    int Month,
    decimal Total,
    int Payments,
    decimal CliQ,
    decimal Cash,
    Dictionary<int, decimal> ByPlanMonths);

public record RenewalsDashboardDto(
    int WithinDays,
    /// <summary>Still running, expiring within the window — chase these to renew.</summary>
    List<SubscriptionDto> ExpiringSoon,
    /// <summary>Past expiry but still on (manual grace / inside AfterDays) — decide: renewed or switch off.</summary>
    List<SubscriptionDto> LapsedStillOn,
    /// <summary>Past expiry and off. Candidates to win back.</summary>
    List<SubscriptionDto> Expired,
    /// <summary>Last 12 months of non-void payments, by paid date (UTC month).</summary>
    List<MonthlyCollectionDto> Monthly);

public record GetRenewalsDashboardRequest(int WithinDays = 30) : IRequest<RenewalsDashboardDto>;

public class GetRenewalsDashboardRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    IUploadFileService files)
    : IRequestHandler<GetRenewalsDashboardRequest, RenewalsDashboardDto>
{
    public async Task<RenewalsDashboardDto> Handle(GetRenewalsDashboardRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var days = request.WithinDays > 0 ? request.WithinDays : 30;
        var now = DateTime.UtcNow;
        var horizon = now.AddDays(days);

        // Everything not comfortably in the future — one query, then bucketed
        // with the same status rules the detail view uses.
        var candidates = await applicationDbContext.Subscriptions.AsNoTracking()
            .Include(s => s.Payments).ThenInclude(p => p.Payer).ThenInclude(p => p.UserAccount)
            .Where(s => !s.IsDeleted && s.ExpiresAt <= horizon)
            .OrderBy(s => s.ExpiresAt)
            .ToListAsync(cancellationToken);

        var mapped = await SubscriptionDtoMapper.MapAsync(applicationDbContext, files, candidates, cancellationToken);

        var expiringSoon = mapped.Where(s => s.Status is SubscriptionStatus.ExpiringSoon or SubscriptionStatus.Active).ToList();
        var lapsedOn = mapped.Where(s => s.Status == SubscriptionStatus.InGrace).ToList();
        var expired = mapped.Where(s => s.Status is SubscriptionStatus.Expired or SubscriptionStatus.SwitchedOff).ToList();

        var since = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-11);
        var payments = await applicationDbContext.Payments.AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsVoid && p.PaidDate >= since)
            .Select(p => new { p.PaidDate, p.Amount, p.Method, p.Subscription.PlanMonths })
            .ToListAsync(cancellationToken);

        var monthly = payments
            .GroupBy(p => (p.PaidDate.Year, p.PaidDate.Month))
            .OrderByDescending(g => g.Key.Year).ThenByDescending(g => g.Key.Month)
            .Select(g => new MonthlyCollectionDto(
                g.Key.Year, g.Key.Month,
                g.Sum(p => p.Amount), g.Count(),
                g.Where(p => p.Method == (int)PaymentMethod.CliQ).Sum(p => p.Amount),
                g.Where(p => p.Method == (int)PaymentMethod.Cash).Sum(p => p.Amount),
                g.GroupBy(p => p.PlanMonths ?? 0).ToDictionary(x => x.Key, x => x.Sum(p => p.Amount))))
            .ToList();

        return new RenewalsDashboardDto(days, expiringSoon, lapsedOn, expired, monthly);
    }
}

// ---------------------------------------------------------------------------
// Payer search (for the "pick a previous payer" list)
// ---------------------------------------------------------------------------

public record SearchPayersRequest(string? Q = null, int? Page = null, int? PageSize = null) : IRequest<PagedResult<PayerDto>>;

public class SearchPayersRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<SearchPayersRequest, PagedResult<PayerDto>>
{
    public async Task<PagedResult<PayerDto>> Handle(SearchPayersRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var query = applicationDbContext.Payers.AsNoTracking()
            .Include(p => p.UserAccount)
            .Include(p => p.Payments)
            .Where(p => !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var q = request.Q.Trim();
            var digits = PhoneNumberUtility.NormalizePhoneNumber(q) ?? q;
            query = query.Where(p =>
                (p.Name != null && p.Name.Contains(q)) ||
                (p.Phone != null && p.Phone.Contains(digits)) ||
                (p.UserAccount != null && ((p.UserAccount.Name != null && p.UserAccount.Name.Contains(q)) ||
                                           (p.UserAccount.Phone != null && p.UserAccount.Phone.Contains(digits)))));
        }

        var payers = await query.OrderByDescending(p => p.ModifiedAt ?? p.CreatedAt).ToListAsync(cancellationToken);
        return payers.Select(SubscriptionDtoMapper.MapPayer).ToList().ToOptionallyPaginated(request.Page, request.PageSize);
    }
}

// ---------------------------------------------------------------------------
// Receipt download — admin, or the owner of the thing that was paid for
// ---------------------------------------------------------------------------

public record ReceiptFile(byte[] Content, string FileName);

public record GetPaymentReceiptRequest(int PaymentId) : IRequest<ReceiptFile>;

public class GetPaymentReceiptRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    IUploadFileService files)
    : IRequestHandler<GetPaymentReceiptRequest, ReceiptFile>
{
    public async Task<ReceiptFile> Handle(GetPaymentReceiptRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");

        var payment = await applicationDbContext.Payments.AsNoTracking()
                          .Include(p => p.Subscription)
                          .FirstOrDefaultAsync(p => p.Id == request.PaymentId && !p.IsDeleted, cancellationToken)
                      ?? throw new NotFoundException($"Payment with id {request.PaymentId} not found");

        if (!await AdminRoleGuard.IsAdminAsync(applicationDbContext, currentUserService, cancellationToken)
            && !await IsOwnerAsync(payment.Subscription, userId, cancellationToken))
            throw new ForbiddenAccessException("Only an admin or the owner of the paid-for item can download this receipt.");

        if (string.IsNullOrEmpty(payment.GeneratedReceiptFileName))
            throw new NotFoundException("No receipt has been generated for this payment");

        var bytes = await files.GetFileAsync(UploadFileFolders.CableReceipts, payment.GeneratedReceiptFileName, cancellationToken);
        return new ReceiptFile(bytes, $"{payment.ReferenceNo}.pdf");
    }

    private async Task<bool> IsOwnerAsync(Subscription s, int userId, CancellationToken ct) => s.EntityType switch
    {
        SubscriptionEntityTypes.StationPremium => await applicationDbContext.ChargingPoints.AsNoTracking()
            .AnyAsync(c => c.Id == s.EntityId && c.OwnerId == userId, ct),
        SubscriptionEntityTypes.ServiceProviderPremium => await applicationDbContext.ServiceProviders.AsNoTracking()
            .AnyAsync(p => p.Id == s.EntityId && p.OwnerId == userId, ct),
        _ => false   // banners have no owner account
    };
}
