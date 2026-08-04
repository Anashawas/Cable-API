using Application.Common.Security;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Queries.GetAllSeasons;

public record AdminSeasonDto(
    int Id,
    string Name,
    string? Description,
    DateTime StartDate,
    DateTime EndDate,
    bool IsActive,
    DateTime CreatedAt
);

/// <summary>Admin list of all loyalty seasons (the portal's missing GetAllSeasons).</summary>
public record GetAllSeasonsRequest() : IRequest<List<AdminSeasonDto>>;

public class GetAllSeasonsRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetAllSeasonsRequest, List<AdminSeasonDto>>
{
    public async Task<List<AdminSeasonDto>> Handle(GetAllSeasonsRequest request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        return await applicationDbContext.LoyaltySeasons
            .AsNoTracking()
            .Where(s => !s.IsDeleted)
            .OrderByDescending(s => s.StartDate)
            .Select(s => new AdminSeasonDto(
                s.Id, s.Name, s.Description, s.StartDate, s.EndDate, s.IsActive, s.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
