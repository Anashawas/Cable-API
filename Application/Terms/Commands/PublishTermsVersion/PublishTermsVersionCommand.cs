using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Exceptions;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.Terms.Commands.PublishTermsVersion;

/// <summary>
/// Admin: publishes a new terms version for a role scope (RoleId null = all
/// roles). The previous active version in that scope is deactivated, which
/// automatically flips every affected user's hasAcceptedTerms to false —
/// no user rows are touched.
/// </summary>
public record PublishTermsVersionCommand(
    string SystemVersion,
    int? RoleId,
    string ContentEn,
    string ContentAr,
    DateTime? EffectiveFrom = null
) : IRequest<int>;

public class PublishTermsVersionCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<PublishTermsVersionCommand, int>
{
    public async Task<int> Handle(PublishTermsVersionCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        if (request.RoleId.HasValue)
        {
            var roleExists = await applicationDbContext.Roles
                .AnyAsync(r => r.Id == request.RoleId.Value, cancellationToken);
            if (!roleExists)
                throw new DataValidationException("RoleId", $"Role {request.RoleId} does not exist");
        }

        // Deactivate the current active version in the same scope.
        var current = await applicationDbContext.TermsVersions
            .Where(t => t.IsActive && !t.IsDeleted && t.RoleId == request.RoleId)
            .ToListAsync(cancellationToken);
        foreach (var t in current)
            t.IsActive = false;

        var version = new TermsVersion
        {
            SystemVersion = request.SystemVersion.Trim(),
            RoleId = request.RoleId,
            ContentEn = request.ContentEn,
            ContentAr = request.ContentAr,
            EffectiveFrom = request.EffectiveFrom ?? DateTime.UtcNow,
            IsActive = true,
            IsDeleted = false
        };
        applicationDbContext.TermsVersions.Add(version);

        await applicationDbContext.SaveChanges(cancellationToken);
        return version.Id;
    }
}
