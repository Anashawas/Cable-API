using Application.Common.Models;
using Application.Common.Security;
using Application.Users.Queries.GetAllUsers;
using Microsoft.EntityFrameworkCore;

namespace Application.Users.Queries.GetUsersPaged;

/// <summary>
/// A2a — server-side paged + filtered users list for the admin portal.
/// `search` matches name, email, phone or id. Paging is opt-in: when
/// page/pageSize are omitted, ALL matching rows are returned (still in the
/// paged envelope). Admin role required.
/// </summary>
public record GetUsersPagedRequest(
    string? Search = null,
    int? RoleId = null,
    string? City = null,
    bool? IsDeleted = null,
    string? Sort = null,   // createdAt_asc|createdAt_desc|name_asc|name_desc
    int? Page = null,
    int? PageSize = null
) : IRequest<PagedResult<GetAllUsersDto>>;

public class GetUsersPagedRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetUsersPagedRequest, PagedResult<GetAllUsersDto>>
{
    public async Task<PagedResult<GetAllUsersDto>> Handle(GetUsersPagedRequest request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var query = applicationDbContext.UserAccounts.AsNoTracking().AsQueryable();

        // isDeleted defaults to false (live users), matching the admin's main view.
        var isDeleted = request.IsDeleted ?? false;
        query = query.Where(x => x.IsDeleted == isDeleted);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim();
            var searchId = int.TryParse(s, out var id) ? id : (int?)null;
            query = query.Where(x =>
                (x.Name != null && x.Name.Contains(s)) ||
                (x.Email != null && x.Email.Contains(s)) ||
                (x.Phone != null && x.Phone.Contains(s)) ||
                (searchId != null && x.Id == searchId.Value));
        }

        if (request.RoleId.HasValue)
            query = query.Where(x => x.RoleId == request.RoleId.Value);

        if (!string.IsNullOrWhiteSpace(request.City))
            query = query.Where(x => x.City != null && x.City.ToLower() == request.City.ToLower());

        query = request.Sort?.ToLowerInvariant() switch
        {
            "createdat_asc" => query.OrderBy(x => x.CreatedAt),
            "name_asc" => query.OrderBy(x => x.Name),
            "name_desc" => query.OrderByDescending(x => x.Name),
            _ => query.OrderByDescending(x => x.CreatedAt) // default: newest first
        };

        // Paging is opt-in — no page params means the full (filtered) set.
        return await query
            .Select(x => new GetAllUsersDto(
                x.Id, x.Name, x.Phone, x.Name, x.Email,
                x.City,
                x.IsPhoneVerified,
                x.HasReadUpdateNotes,
                x.IsDeleted,
                x.CreatedAt,
                new RoleSummary(x.Role.Id, x.Role.Name),
                x.UserCars.Select(uc => new UserCarSummaryDto(
                    uc.Id,
                    uc.CarModel.CarType.Name,
                    uc.CarModel.Name,
                    uc.PlugType.Name,
                    uc.CreatedAt
                )).ToList()
            ))
            .ToOptionallyPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);
    }
}
