using Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Application.Terms.Queries.GetCurrentTerms;

public record GetCurrentTermsDto(
    int Id,
    string SystemVersion,
    int? RoleId,
    DateTime EffectiveFrom,
    string ContentEn,
    string ContentAr,
    bool HasAccepted,
    DateTime? AcceptedAt);

/// <summary>
/// Returns the terms version that applies to the caller (role-specific first,
/// general fallback). Works anonymously too (registration screen): no token →
/// the general policy with hasAccepted = false. Null when nothing is published.
/// </summary>
public record GetCurrentTermsRequest : IRequest<GetCurrentTermsDto?>;

public class GetCurrentTermsRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCurrentTermsRequest, GetCurrentTermsDto?>
{
    public async Task<GetCurrentTermsDto?> Handle(GetCurrentTermsRequest request, CancellationToken cancellationToken)
    {
        int? roleId = null;
        int? acceptedVersionId = null;
        DateTime? acceptedAt = null;

        if (currentUserService.UserId.HasValue)
        {
            var user = await applicationDbContext.UserAccounts.AsNoTracking()
                .Where(x => x.Id == currentUserService.UserId.Value && !x.IsDeleted)
                .Select(x => new { x.RoleId, x.AcceptedTermsVersionId, x.TermsAcceptedAt })
                .FirstOrDefaultAsync(cancellationToken);

            roleId = user?.RoleId;
            acceptedVersionId = user?.AcceptedTermsVersionId;
            acceptedAt = user?.TermsAcceptedAt;
        }

        var terms = await TermsResolver.GetActiveForRoleAsync(applicationDbContext, roleId, cancellationToken);
        if (terms == null)
            return null;

        var hasAccepted = acceptedVersionId == terms.Id;
        return new GetCurrentTermsDto(
            terms.Id,
            terms.SystemVersion,
            terms.RoleId,
            terms.EffectiveFrom,
            terms.ContentEn,
            terms.ContentAr,
            hasAccepted,
            hasAccepted ? acceptedAt : null);
    }
}
