using Application.Common.Interfaces;
using Application.Common.Models;
using Application.Common.Security;
using Microsoft.EntityFrameworkCore;

namespace Application.Terms.Queries.GetAllTermsVersions;

public record TermsVersionListDto(
    int Id,
    string SystemVersion,
    int? RoleId,
    string? RoleName,
    bool IsActive,
    DateTime EffectiveFrom,
    DateTime CreatedAt,
    int AcceptanceCount);

/// <summary>Admin: all published terms versions (content excluded — use GetTermsVersionById).</summary>
public record GetAllTermsVersionsRequest(int? Page = null, int? PageSize = null)
    : IRequest<PagedResult<TermsVersionListDto>>;

public class GetAllTermsVersionsRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetAllTermsVersionsRequest, PagedResult<TermsVersionListDto>>
{
    public async Task<PagedResult<TermsVersionListDto>> Handle(GetAllTermsVersionsRequest request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        return await applicationDbContext.TermsVersions
            .AsNoTracking()
            .Where(t => !t.IsDeleted)
            .OrderByDescending(t => t.IsActive)
            .ThenByDescending(t => t.Id)
            .Select(t => new TermsVersionListDto(
                t.Id,
                t.SystemVersion,
                t.RoleId,
                t.Role != null ? t.Role.Name : null,
                t.IsActive,
                t.EffectiveFrom,
                t.CreatedAt,
                t.Acceptances.Count()))
            .ToOptionallyPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);
    }
}
