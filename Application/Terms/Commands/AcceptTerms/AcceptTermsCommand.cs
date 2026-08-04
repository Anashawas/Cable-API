using Application.Common.Interfaces;
using Cable.Core;
using Cable.Core.Exceptions;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.Terms.Commands.AcceptTerms;

public record AcceptTermsResult(int TermsVersionId, string SystemVersion, DateTime AcceptedAt);

/// <summary>
/// Records the caller's acceptance of the terms version that applies to their
/// role. Idempotent: accepting an already-accepted version returns the
/// existing acceptance instead of a second row.
/// </summary>
public record AcceptTermsCommand : IRequest<AcceptTermsResult>;

public class AcceptTermsCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<AcceptTermsCommand, AcceptTermsResult>
{
    public async Task<AcceptTermsResult> Handle(AcceptTermsCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.UserId is null)
            throw new NotAuthorizedAccessException("User not authenticated");

        var user = await applicationDbContext.UserAccounts
                       .FirstOrDefaultAsync(x => x.Id == currentUserService.UserId.Value && !x.IsDeleted, cancellationToken)
                   ?? throw new NotFoundException($"can not find user with id {currentUserService.UserId}");

        var terms = await TermsResolver.GetActiveForRoleAsync(applicationDbContext, user.RoleId, cancellationToken)
                    ?? throw new DataValidationException("Terms", "There is no active terms version to accept");

        var existing = await applicationDbContext.UserTermsAcceptances
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == user.Id && x.TermsVersionId == terms.Id, cancellationToken);

        var acceptedAt = existing?.AcceptedAt ?? DateTime.UtcNow;

        if (existing == null)
        {
            applicationDbContext.UserTermsAcceptances.Add(new UserTermsAcceptance
            {
                UserId = user.Id,
                TermsVersionId = terms.Id,
                AcceptedAt = acceptedAt
            });
        }

        // Denormalized current state — what the hasAcceptedTerms flag reads.
        user.AcceptedTermsVersionId = terms.Id;
        user.TermsAcceptedAt = acceptedAt;

        await applicationDbContext.SaveChanges(cancellationToken);

        return new AcceptTermsResult(terms.Id, terms.SystemVersion, acceptedAt);
    }
}
