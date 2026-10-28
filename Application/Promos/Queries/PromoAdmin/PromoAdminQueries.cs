using System.Globalization;
using System.Text;
using Application.Common.Security;
using Application.Common.Utilities;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Promos.Queries.PromoAdmin;

/// <summary>A campaign for the admin list and form: every field plus the counters.</summary>
public record AdminPromoCampaignDto(
    int Id,
    string TitleAr,
    string TitleEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string? ScopeLabelAr,
    string? ScopeLabelEn,
    decimal CableSharePercent,
    decimal? CableBudgetJod,
    decimal CableSpentJod,
    decimal? BudgetLeftJod,
    PromoTargetScope StationsScope,
    List<int> StationIds,
    PromoTargetScope ServiceProvidersScope,
    List<int> ServiceProviderIds,
    PromoEffectType EffectType,
    decimal EffectValue,
    decimal? MaxDiscountJod,
    List<string> AppliesTo,
    List<int> ServiceCategoryIds,
    List<string> Cities,
    decimal? MinAmountJod,
    DateTime? StartsAt,
    DateTime? EndsAt,
    int? ValidDaysAfterClaim,
    int? TotalLimit,
    int PerUserLimit,
    bool NewUsersOnly,
    PromoCodeMode CodeMode,
    string? PublicCode,
    int ClaimsCount,
    int UsesCount,
    int CodesCount,
    int CodesClaimed,
    bool IsActive,
    string? DeactivatedReason,
    /// <summary>True after the first use: effect type / value / cap / Cable's share can't be edited.</summary>
    bool EffectLocked,
    int? CreatedByUserId,
    DateTime CreatedAt);

public record PromoAdminRow(PromoCampaign Campaign, int CodesCount, int CodesClaimed);

internal static class PromoAdminProjection
{
    public static readonly System.Linq.Expressions.Expression<Func<PromoCampaign, PromoAdminRow>> Row = c => new PromoAdminRow(
        c, c.Codes.Count(), c.Codes.Count(x => x.ClaimedByUserId != null));

    public static AdminPromoCampaignDto ToDto(PromoAdminRow r)
    {
        var c = r.Campaign;
        return new AdminPromoCampaignDto(c.Id, c.TitleAr, c.TitleEn, c.DescriptionAr, c.DescriptionEn, c.ScopeLabelAr,
            c.ScopeLabelEn, c.CableSharePercent, c.CableBudgetJod, c.CableSpentJod,
            c.CableBudgetJod.HasValue ? Math.Max(0, c.CableBudgetJod.Value - c.CableSpentJod) : null,
            c.StationsScope, CsvList.Ints(c.StationIds), c.ServiceProvidersScope, CsvList.Ints(c.ServiceProviderIds),
            c.EffectType, c.EffectValue, c.MaxDiscountJod, CsvList.Strings(c.AppliesTo), CsvList.Ints(c.ServiceCategoryIds),
            CsvList.Strings(c.Cities), c.MinAmountJod, c.StartsAt, c.EndsAt, c.ValidDaysAfterClaim, c.TotalLimit,
            c.PerUserLimit, c.NewUsersOnly, c.CodeMode, c.PublicCode, c.ClaimsCount, c.UsesCount, r.CodesCount,
            r.CodesClaimed, c.IsActive, c.DeactivatedReason, c.UsesCount > 0, c.CreatedBy, c.CreatedAt);
    }
}

/// <summary>GET /api/admin/promos?isActive=&amp;search=&amp;page=&amp;pageSize= — paged when page/pageSize is sent.</summary>
public record GetPromoCampaignsRequest(bool? IsActive, string? Search, int? Page, int? PageSize)
    : IRequest<PagedResult<AdminPromoCampaignDto>>;

public class GetPromoCampaignsRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetPromoCampaignsRequest, PagedResult<AdminPromoCampaignDto>>
{
    public async Task<PagedResult<AdminPromoCampaignDto>> Handle(GetPromoCampaignsRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var query = db.PromoCampaigns.AsNoTracking().Where(c => !c.IsDeleted);
        if (request.IsActive.HasValue) query = query.Where(c => c.IsActive == request.IsActive.Value);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim();
            var code = SafeCode.Normalize(s);
            query = query.Where(c => c.TitleAr.Contains(s) || c.TitleEn.Contains(s) || c.PublicCodeNormalized == code);
        }

        var paged = await query.OrderByDescending(c => c.Id).Select(PromoAdminProjection.Row)
            .ToOptionallyPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);
        return paged.As(paged.Items.Select(PromoAdminProjection.ToDto).ToList());
    }
}

/// <summary>GET /api/admin/promos/{id}.</summary>
public record GetPromoCampaignByIdRequest(int Id) : IRequest<AdminPromoCampaignDto>;

public class GetPromoCampaignByIdRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetPromoCampaignByIdRequest, AdminPromoCampaignDto>
{
    public async Task<AdminPromoCampaignDto> Handle(GetPromoCampaignByIdRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var row = await db.PromoCampaigns.AsNoTracking().Where(c => c.Id == request.Id && !c.IsDeleted)
                      .Select(PromoAdminProjection.Row).FirstOrDefaultAsync(cancellationToken)
                  ?? throw new NotFoundException($"Promo campaign {request.Id} was not found");
        return PromoAdminProjection.ToDto(row);
    }
}

public record PromoReportProviderDto(
    string ProviderType, int ProviderId, string? ProviderName, int Uses,
    decimal OriginalAmountJod, decimal DiscountJod, decimal CableShareJod, decimal ProviderShareJod, int BonusPoints);

/// <summary>Campaign report: claims, uses, the discount split Cable / partners, the budget left, per partner.</summary>
public record PromoReportDto(
    int CampaignId,
    string Title,
    int Claims,
    int Assigned,
    int Available,
    int Used,
    int Expired,
    int Revoked,
    int Uses,
    decimal OriginalAmountJod,
    decimal TotalDiscountJod,
    decimal CableShareJod,
    decimal ProvidersShareJod,
    int BonusPointsGiven,
    decimal? BudgetJod,
    decimal CableSpentJod,
    decimal? BudgetLeftJod,
    List<PromoReportProviderDto> ByProvider);

/// <summary>GET /api/admin/promos/{id}/report.</summary>
public record GetPromoReportRequest(int Id) : IRequest<PromoReportDto>;

public class GetPromoReportRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetPromoReportRequest, PromoReportDto>
{
    public async Task<PromoReportDto> Handle(GetPromoReportRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var c = await db.PromoCampaigns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                ?? throw new NotFoundException($"Promo campaign {request.Id} was not found");
        var now = DateTime.UtcNow;

        var promos = await db.UserPromos.AsNoTracking()
            .Where(u => u.PromoCampaignId == c.Id && !u.IsDeleted)
            .Select(u => new { u.Source, u.Status, u.ExpiresAt })
            .ToListAsync(cancellationToken);
        var statuses = promos.Select(p => PromoRules.EffectiveStatus(p.Status, p.ExpiresAt, now)).ToList();

        var byProvider = await db.PromoUsages.AsNoTracking()
            .Where(u => u.PromoCampaignId == c.Id)
            .GroupBy(u => new { u.ProviderType, u.ProviderId })
            .Select(g => new
            {
                g.Key.ProviderType, g.Key.ProviderId, Uses = g.Count(),
                Original = g.Sum(x => x.OriginalAmountJod), Discount = g.Sum(x => x.DiscountJod),
                Cable = g.Sum(x => x.CableShareJod), Provider = g.Sum(x => x.ProviderShareJod), Bonus = g.Sum(x => x.BonusPoints)
            })
            .ToListAsync(cancellationToken);

        var cpIds = byProvider.Where(p => p.ProviderType == PromoRules.ChargingPoint).Select(p => p.ProviderId).ToList();
        var spIds = byProvider.Where(p => p.ProviderType == PromoRules.ServiceProvider).Select(p => p.ProviderId).ToList();
        var cpNames = await db.ChargingPoints.AsNoTracking().Where(x => cpIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var spNames = await db.ServiceProviders.AsNoTracking().Where(x => spIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var rows = byProvider.Select(p => new PromoReportProviderDto(p.ProviderType, p.ProviderId,
                p.ProviderType == PromoRules.ChargingPoint ? cpNames.GetValueOrDefault(p.ProviderId) : spNames.GetValueOrDefault(p.ProviderId),
                p.Uses, p.Original, p.Discount, p.Cable, p.Provider, p.Bonus))
            .OrderByDescending(p => p.Uses).ToList();

        return new PromoReportDto(c.Id, Lang.Pick(c.TitleAr, c.TitleEn),
            promos.Count(p => p.Source == UserPromoSource.Code),
            promos.Count(p => p.Source == UserPromoSource.Assigned),
            statuses.Count(s => s == UserPromoStatus.Available),
            statuses.Count(s => s == UserPromoStatus.Used),
            statuses.Count(s => s == UserPromoStatus.Expired),
            statuses.Count(s => s == UserPromoStatus.Revoked),
            rows.Sum(r => r.Uses), rows.Sum(r => r.OriginalAmountJod), rows.Sum(r => r.DiscountJod),
            rows.Sum(r => r.CableShareJod), rows.Sum(r => r.ProviderShareJod), rows.Sum(r => r.BonusPoints),
            c.CableBudgetJod, c.CableSpentJod,
            c.CableBudgetJod.HasValue ? Math.Max(0, c.CableBudgetJod.Value - c.CableSpentJod) : null,
            rows);
    }
}

/// <summary>GET /api/admin/promos/{id}/codes/export — the unique codes as CSV (UTF-8 with BOM).</summary>
public record ExportPromoCodesCsvRequest(int Id) : IRequest<byte[]>;

public class ExportPromoCodesCsvRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ExportPromoCodesCsvRequest, byte[]>
{
    public async Task<byte[]> Handle(ExportPromoCodesCsvRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        if (!await db.PromoCampaigns.AnyAsync(c => c.Id == request.Id && !c.IsDeleted, cancellationToken))
            throw new NotFoundException($"Promo campaign {request.Id} was not found");

        var codes = await db.PromoCodes.AsNoTracking()
            .Where(c => c.PromoCampaignId == request.Id)
            .OrderBy(c => c.Id)
            .Select(c => new
            {
                c.Code, c.ClaimedByUserId, c.ClaimedAt, c.CreatedAt, c.BatchId,
                UserName = db.UserAccounts.Where(u => u.Id == c.ClaimedByUserId).Select(u => u.Name).FirstOrDefault(),
                UserPhone = db.UserAccounts.Where(u => u.Id == c.ClaimedByUserId).Select(u => u.Phone).FirstOrDefault(),
                PromoStatus = db.UserPromos.Where(u => u.PromoCodeId == c.Id && !u.IsDeleted).Select(u => (UserPromoStatus?)u.Status).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("Code,Claimed,ClaimedByUserId,ClaimedByName,ClaimedByPhone,ClaimedAt,PromoStatus,BatchId,CreatedAt");
        foreach (var c in codes)
            sb.AppendLine(string.Join(",",
                c.Code, c.ClaimedByUserId.HasValue ? "Yes" : "No", c.ClaimedByUserId?.ToString(CultureInfo.InvariantCulture) ?? "",
                CsvList.Cell(c.UserName), CsvList.Cell(c.UserPhone), Jordan(c.ClaimedAt), c.PromoStatus?.ToString() ?? "",
                c.BatchId?.ToString() ?? "", Jordan(c.CreatedAt)));

        return CsvList.Utf8WithBom(sb);
    }

    private static string Jordan(DateTime? utc) =>
        utc.HasValue ? JordanTime.FromUtc(utc.Value).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "";
}

/// <summary>A user's promo as the admin sees it (user profile → codes, with revoke).</summary>
public record AdminUserPromoDto(
    int Id, int UserId, string? UserName, string? UserPhone, int CampaignId, string CampaignTitle, string? Code,
    UserPromoSource Source, UserPromoStatus Status, DateTime ClaimedAt, DateTime? ExpiresAt, DateTime? UsedAt,
    DateTime? RevokedAt, string? RevokeReason);

/// <summary>GET /api/admin/user-promos?userId=&amp;campaignId=&amp;status=&amp;page=&amp;pageSize=.</summary>
public record GetAdminUserPromosRequest(int? UserId, int? CampaignId, UserPromoStatus? Status, int? Page, int? PageSize)
    : IRequest<PagedResult<AdminUserPromoDto>>;

public class GetAdminUserPromosRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetAdminUserPromosRequest, PagedResult<AdminUserPromoDto>>
{
    public async Task<PagedResult<AdminUserPromoDto>> Handle(GetAdminUserPromosRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var now = DateTime.UtcNow;
        var query = db.UserPromos.AsNoTracking().Where(u => !u.IsDeleted);
        if (request.UserId.HasValue) query = query.Where(u => u.UserId == request.UserId.Value);
        if (request.CampaignId.HasValue) query = query.Where(u => u.PromoCampaignId == request.CampaignId.Value);
        if (request.Status.HasValue) query = query.Where(u => u.Status == request.Status.Value);

        var paged = await query.OrderByDescending(u => u.Id)
            .Select(u => new
            {
                u.Id, u.UserId, u.User.Name, u.User.Phone, u.PromoCampaignId, u.Campaign.TitleAr, u.Campaign.TitleEn,
                Code = u.PromoCode != null ? u.PromoCode.Code : u.Campaign.PublicCode,
                u.Source, u.Status, u.ClaimedAt, u.ExpiresAt, u.UsedAt, u.RevokedAt, u.RevokeReason
            })
            .ToPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);

        return paged.As(paged.Items.Select(u => new AdminUserPromoDto(u.Id, u.UserId, u.Name, u.Phone, u.PromoCampaignId,
            Lang.Pick(u.TitleAr, u.TitleEn), u.Code, u.Source, PromoRules.EffectiveStatus(u.Status, u.ExpiresAt, now),
            u.ClaimedAt, u.ExpiresAt, u.UsedAt, u.RevokedAt, u.RevokeReason)).ToList());
    }
}
