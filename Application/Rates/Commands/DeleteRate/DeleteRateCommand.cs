using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Rates.Commands.DeleteRate;

/// <summary>F2: a user removes their OWN review (admins can remove any).</summary>
public record DeleteRateCommand(int Id) : IRequest;

public class DeleteRateCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteRateCommand>
{
    public async Task Handle(DeleteRateCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var rate = await applicationDbContext.Rates
                       .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                   ?? throw new NotFoundException($"can not find rate with id {request.Id}");

        if (rate.UserId != userId
            && !await AdminRoleGuard.IsAdminAsync(applicationDbContext, currentUserService, cancellationToken))
            throw new ForbiddenAccessException("You can only delete your own review.");

        // Soft delete — averages are computed live over IsDeleted = 0, so they self-correct.
        rate.IsDeleted = true;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
