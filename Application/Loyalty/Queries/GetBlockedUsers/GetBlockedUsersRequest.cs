using Application.Common.Models;
using Application.Common.Security;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Queries.GetBlockedUsers;

public record BlockedUserDto(
    int UserId,
    string? UserName,
    string? Reason,
    DateTime? BlockedAt,
    DateTime? BlockedUntil,
    int? BlockedByUserId,
    string? BlockedByUserName
);

/// <summary>F1 — every user currently blocked from loyalty. Admin role required.</summary>
public record GetBlockedUsersRequest(int? Page = null, int? PageSize = null) : IRequest<PagedResult<BlockedUserDto>>;

public class GetBlockedUsersRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetBlockedUsersRequest, PagedResult<BlockedUserDto>>
{
    public async Task<PagedResult<BlockedUserDto>> Handle(GetBlockedUsersRequest request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var now = DateTime.UtcNow;

        return await applicationDbContext.UserLoyaltyAccounts
            .AsNoTracking()
            .Where(a => !a.IsDeleted && a.IsBlocked
                        && (a.BlockedUntil == null || a.BlockedUntil >= now))
            .OrderByDescending(a => a.BlockedAt)
            .Select(a => new BlockedUserDto(
                a.UserId,
                a.User.Name,
                a.BlockReason,
                a.BlockedAt,
                a.BlockedUntil,
                a.BlockedByUserId,
                a.BlockedByUser != null ? a.BlockedByUser.Name : null))
            .ToOptionallyPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);
    }
}
