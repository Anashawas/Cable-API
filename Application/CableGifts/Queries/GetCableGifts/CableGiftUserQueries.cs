using Cable.Core;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.CableGifts.Queries.GetCableGifts;

/// <summary>
/// GET /api/cable-gifts — the active gifts for the user's city, with canOrder / cannotOrderReason / remaining /
/// myOrdersCount per card. Out-of-stock gifts stay in the list (the app greys them out).
/// </summary>
public record GetCableGiftsRequest : IRequest<List<CableGiftDto>>;

public class GetCableGiftsRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetCableGiftsRequest, List<CableGiftDto>>
{
    public async Task<List<CableGiftDto>> Handle(GetCableGiftsRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var now = DateTime.UtcNow;

        var gifts = await db.CableGifts.AsNoTracking()
            .Where(g => g.IsActive && !g.IsDeleted
                        && (g.StartsAt == null || g.StartsAt <= now)
                        && (g.EndsAt == null || g.EndsAt > now))
            .OrderBy(g => g.SortOrder).ThenBy(g => g.Id)
            .ToListAsync(cancellationToken);

        var user = await GiftViews.LoadUserStateAsync(db, userId, now, cancellationToken);
        gifts = gifts.Where(g => GiftRules.IsInCity(g.Cities, user.City)).ToList();

        var mine = await GiftViews.MyActiveOrderCountsAsync(db, userId, gifts.Select(g => g.Id).ToList(), cancellationToken);
        return gifts.Select(g => GiftViews.ToDto(g, user, mine.GetValueOrDefault(g.Id), now)).ToList();
    }
}

/// <summary>GET /api/cable-gifts/{id} — one gift with "your balance after buying".</summary>
public record GetCableGiftByIdRequest(int Id) : IRequest<CableGiftDto>;

public class GetCableGiftByIdRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetCableGiftByIdRequest, CableGiftDto>
{
    public async Task<CableGiftDto> Handle(GetCableGiftByIdRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var now = DateTime.UtcNow;

        var gift = await db.CableGifts.AsNoTracking()
                       .FirstOrDefaultAsync(g => g.Id == request.Id && !g.IsDeleted, cancellationToken)
                   ?? throw new NotFoundException(Lang.Pick("الهدية غير موجودة", $"Gift {request.Id} was not found"));

        var user = await GiftViews.LoadUserStateAsync(db, userId, now, cancellationToken);
        var mine = await GiftViews.MyActiveOrderCountsAsync(db, userId, [gift.Id], cancellationToken);
        return GiftViews.ToDto(gift, user, mine.GetValueOrDefault(gift.Id), now);
    }
}

/// <summary>GET /api/cable-gifts/my-orders — "My gifts", newest first, paged (default 20).</summary>
public record GetMyGiftOrdersRequest(GiftOrderStatus? Status, int? Page, int? PageSize) : IRequest<PagedResult<MyGiftOrderDto>>;

public class GetMyGiftOrdersRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetMyGiftOrdersRequest, PagedResult<MyGiftOrderDto>>
{
    public async Task<PagedResult<MyGiftOrderDto>> Handle(GetMyGiftOrdersRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");

        var query = db.GiftOrders.AsNoTracking().Where(o => o.UserId == userId && !o.IsDeleted);
        if (request.Status.HasValue)
            query = query.Where(o => o.Status == request.Status.Value);

        var paged = await query
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Select(o => new
            {
                o.Id, o.OrderCode, o.CableGiftId, o.Gift.NameAr, o.Gift.NameEn, o.Gift.ImageUrl, o.Gift.GiftType,
                o.PointsSpent, o.Status, o.ContactPhone, o.Note, o.FulfillmentNote, o.FulfilledAt,
                o.CancelReason, o.CancelledAt, o.CreatedAt
            })
            .ToPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);

        return paged.As(paged.Items.Select(o => new MyGiftOrderDto(
            o.Id, o.OrderCode, o.CableGiftId, Lang.Pick(o.NameAr, o.NameEn), o.ImageUrl, o.GiftType,
            o.PointsSpent, o.Status, o.ContactPhone, o.Note,
            o.Status == GiftOrderStatus.Fulfilled ? o.FulfillmentNote : null, o.FulfilledAt,
            o.CancelReason, o.CancelledAt,
            o.Status == GiftOrderStatus.Cancelled,
            o.CreatedAt)).ToList());
    }
}
