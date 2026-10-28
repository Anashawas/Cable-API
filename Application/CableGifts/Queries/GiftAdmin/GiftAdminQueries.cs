using System.Globalization;
using System.Text;
using Application.Common.Security;
using Application.Common.Utilities;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.CableGifts.Queries.GiftAdmin;

/// <summary>A gift as the admin table / form sees it: both languages and the order figures.</summary>
public record AdminCableGiftDto(
    int Id,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string? HowToReceiveAr,
    string? HowToReceiveEn,
    GiftType GiftType,
    int PointsCost,
    string? ImageUrl,
    int? Stock,
    int OrdersCount,
    int? Remaining,
    int? PerUserLimit,
    DateTime? StartsAt,
    DateTime? EndsAt,
    List<string> Cities,
    int SortOrder,
    bool IsActive,
    int PendingOrders,
    int FulfilledOrders,
    int CancelledOrders,
    DateTime CreatedAt);

/// <summary>GET /api/admin/cable-gifts — every gift, inactive ones too. Paged when page/pageSize is sent.</summary>
public record GetAdminCableGiftsRequest(bool? IsActive, string? Search, int? Page, int? PageSize)
    : IRequest<PagedResult<AdminCableGiftDto>>;

public class GetAdminCableGiftsRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetAdminCableGiftsRequest, PagedResult<AdminCableGiftDto>>
{
    public async Task<PagedResult<AdminCableGiftDto>> Handle(GetAdminCableGiftsRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);

        var query = db.CableGifts.AsNoTracking().Where(g => !g.IsDeleted);
        if (request.IsActive.HasValue)
            query = query.Where(g => g.IsActive == request.IsActive.Value);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim();
            query = query.Where(g => g.NameAr.Contains(s) || g.NameEn.Contains(s));
        }

        var paged = await query.OrderBy(g => g.SortOrder).ThenByDescending(g => g.Id)
            .Select(GiftAdminProjection.Row)
            .ToOptionallyPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);
        return paged.As(paged.Items.Select(GiftAdminProjection.ToDto).ToList());
    }
}

/// <summary>GET /api/admin/cable-gifts/{id} — one gift for the edit form.</summary>
public record GetAdminCableGiftByIdRequest(int Id) : IRequest<AdminCableGiftDto>;

public class GetAdminCableGiftByIdRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetAdminCableGiftByIdRequest, AdminCableGiftDto>
{
    public async Task<AdminCableGiftDto> Handle(GetAdminCableGiftByIdRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var row = await db.CableGifts.AsNoTracking()
                      .Where(g => g.Id == request.Id && !g.IsDeleted)
                      .Select(GiftAdminProjection.Row)
                      .FirstOrDefaultAsync(cancellationToken)
                  ?? throw new NotFoundException($"Gift {request.Id} was not found");
        return GiftAdminProjection.ToDto(row);
    }
}

public record GiftAdminRow(CableGift Gift, int Pending, int Fulfilled, int Cancelled);

internal static class GiftAdminProjection
{
    public static readonly System.Linq.Expressions.Expression<Func<CableGift, GiftAdminRow>> Row = g => new GiftAdminRow(
        g,
        g.Orders.Count(o => !o.IsDeleted && o.Status == GiftOrderStatus.Pending),
        g.Orders.Count(o => !o.IsDeleted && o.Status == GiftOrderStatus.Fulfilled),
        g.Orders.Count(o => !o.IsDeleted && o.Status == GiftOrderStatus.Cancelled));

    public static AdminCableGiftDto ToDto(GiftAdminRow r)
    {
        var g = r.Gift;
        return new AdminCableGiftDto(g.Id, g.NameAr, g.NameEn, g.DescriptionAr, g.DescriptionEn, g.HowToReceiveAr,
            g.HowToReceiveEn, g.GiftType, g.PointsCost, g.ImageUrl, g.Stock, g.OrdersCount,
            GiftRules.Remaining(g.Stock, g.OrdersCount), g.PerUserLimit, g.StartsAt, g.EndsAt,
            CsvList.Strings(g.Cities), g.SortOrder, g.IsActive, r.Pending, r.Fulfilled, r.Cancelled, g.CreatedAt);
    }
}

/// <summary>One order in the admin "Gift orders" table, with the user's phone for the call.</summary>
public record AdminGiftOrderDto(
    int Id,
    string OrderCode,
    int GiftId,
    string GiftNameAr,
    string GiftNameEn,
    GiftType GiftType,
    int UserId,
    string? UserName,
    string? UserPhone,
    string? ContactPhone,
    string? Note,
    int PointsSpent,
    GiftOrderStatus Status,
    string? FulfillmentNote,
    DateTime? FulfilledAt,
    int? FulfilledByUserId,
    string? CancelReason,
    DateTime? CancelledAt,
    int? CancelledByUserId,
    DateTime CreatedAt);

/// <summary>GET /api/admin/cable-gifts/orders?status=&amp;giftId=&amp;from=&amp;to=&amp;search=&amp;page= (dates are Jordan days, to inclusive).</summary>
public record GetAdminGiftOrdersRequest(
    GiftOrderStatus? Status, int? GiftId, DateTime? From, DateTime? To, string? Search, int? Page, int? PageSize)
    : IRequest<PagedResult<AdminGiftOrderDto>>;

public class GetAdminGiftOrdersRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetAdminGiftOrdersRequest, PagedResult<AdminGiftOrderDto>>
{
    public async Task<PagedResult<AdminGiftOrderDto>> Handle(GetAdminGiftOrdersRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        return await GiftOrderFilters.Apply(db, request.Status, request.GiftId, request.From, request.To, request.Search)
            .ToPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);
    }
}

/// <summary>GET /api/admin/cable-gifts/orders/export — same filters, CSV in UTF-8 with BOM (Arabic opens right in Excel).</summary>
public record ExportGiftOrdersCsvRequest(GiftOrderStatus? Status, int? GiftId, DateTime? From, DateTime? To, string? Search)
    : IRequest<byte[]>;

public class ExportGiftOrdersCsvRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ExportGiftOrdersCsvRequest, byte[]>
{
    public async Task<byte[]> Handle(ExportGiftOrdersCsvRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var rows = await GiftOrderFilters.Apply(db, request.Status, request.GiftId, request.From, request.To, request.Search)
            .Take(50_000)
            .ToListAsync(cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("OrderCode,GiftAr,GiftEn,GiftType,UserId,UserName,UserPhone,ContactPhone,PointsSpent,Status," +
                      "Note,FulfillmentNote,CreatedAt,FulfilledAt,CancelledAt,CancelReason");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",",
                CsvList.Cell(r.OrderCode), CsvList.Cell(r.GiftNameAr), CsvList.Cell(r.GiftNameEn), r.GiftType,
                r.UserId, CsvList.Cell(r.UserName), CsvList.Cell(r.UserPhone), CsvList.Cell(r.ContactPhone),
                r.PointsSpent, r.Status, CsvList.Cell(r.Note), CsvList.Cell(r.FulfillmentNote),
                Jordan(r.CreatedAt), Jordan(r.FulfilledAt), Jordan(r.CancelledAt), CsvList.Cell(r.CancelReason)));

        return CsvList.Utf8WithBom(sb);
    }

    private static string Jordan(DateTime? utc) =>
        utc.HasValue ? JordanTime.FromUtc(utc.Value).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "";
}

public record PendingGiftOrdersCountDto(int PendingCount);

/// <summary>GET /api/admin/cable-gifts/orders/pending-count — the dashboard badge.</summary>
public record GetPendingGiftOrdersCountRequest : IRequest<PendingGiftOrdersCountDto>;

public class GetPendingGiftOrdersCountRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetPendingGiftOrdersCountRequest, PendingGiftOrdersCountDto>
{
    public async Task<PendingGiftOrdersCountDto> Handle(GetPendingGiftOrdersCountRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        return new PendingGiftOrdersCountDto(await db.GiftOrders.CountAsync(
            o => o.Status == GiftOrderStatus.Pending && !o.IsDeleted, cancellationToken));
    }
}

internal static class GiftOrderFilters
{
    public static IQueryable<AdminGiftOrderDto> Apply(IApplicationDbContext db, GiftOrderStatus? status, int? giftId,
        DateTime? from, DateTime? to, string? search)
    {
        var query = db.GiftOrders.AsNoTracking().Where(o => !o.IsDeleted);
        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        if (giftId.HasValue) query = query.Where(o => o.CableGiftId == giftId.Value);
        if (from.HasValue)
        {
            var fromUtc = JordanTime.ToUtc(from.Value);
            query = query.Where(o => o.CreatedAt >= fromUtc);
        }
        if (to.HasValue)
        {
            // A bare date means the whole day.
            var toUtc = JordanTime.ToUtc(to.Value.TimeOfDay == TimeSpan.Zero ? to.Value.AddDays(1) : to.Value);
            query = query.Where(o => o.CreatedAt < toUtc);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(o => o.OrderCode.Contains(s)
                                     || (o.ContactPhone != null && o.ContactPhone.Contains(s))
                                     || (o.User.Phone != null && o.User.Phone.Contains(s))
                                     || (o.User.Name != null && o.User.Name.Contains(s)));
        }

        return query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Select(o => new AdminGiftOrderDto(
                o.Id, o.OrderCode, o.CableGiftId, o.Gift.NameAr, o.Gift.NameEn, o.Gift.GiftType,
                o.UserId, o.User.Name, o.User.Phone, o.ContactPhone, o.Note, o.PointsSpent, o.Status,
                o.FulfillmentNote, o.FulfilledAt, o.FulfilledByUserId, o.CancelReason, o.CancelledAt,
                o.CancelledByUserId, o.CreatedAt));
    }
}
