using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core.Exceptions;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.Terms.Queries.GetTermsVersionById;

public record TermsVersionDetailDto(
    int Id,
    string SystemVersion,
    int? RoleId,
    string? RoleName,
    bool IsActive,
    DateTime EffectiveFrom,
    DateTime CreatedAt,
    string ContentEn,
    string ContentAr,
    int AcceptanceCount);

/// <summary>Admin: one terms version with its full content.</summary>
public record GetTermsVersionByIdRequest(int Id) : IRequest<TermsVersionDetailDto>;

public class GetTermsVersionByIdRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetTermsVersionByIdRequest, TermsVersionDetailDto>
{
    public async Task<TermsVersionDetailDto> Handle(GetTermsVersionByIdRequest request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        return await applicationDbContext.TermsVersions
                   .AsNoTracking()
                   .Where(t => t.Id == request.Id && !t.IsDeleted)
                   .Select(t => new TermsVersionDetailDto(
                       t.Id,
                       t.SystemVersion,
                       t.RoleId,
                       t.Role != null ? t.Role.Name : null,
                       t.IsActive,
                       t.EffectiveFrom,
                       t.CreatedAt,
                       t.ContentEn,
                       t.ContentAr,
                       t.Acceptances.Count()))
                   .FirstOrDefaultAsync(cancellationToken)
               ?? throw new NotFoundException(nameof(TermsVersion), request.Id);
    }
}
