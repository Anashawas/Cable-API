using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Users.Commands.RestoreUsers;

public record RestoreUsersResult(int RestoredCount, List<int> RestoredIds, List<int> NotFoundIds);

public record RestoreUsersCommand(int[] Ids) : IRequest<RestoreUsersResult>;

public class RestoreUsersCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<RestoreUsersCommand, RestoreUsersResult>
{
    public async Task<RestoreUsersResult> Handle(RestoreUsersCommand request, CancellationToken cancellationToken)
    {
        _ = currentUserService.UserId
            ?? throw new NotAuthorizedAccessException("User not authenticated");

        if (request.Ids == null || request.Ids.Length == 0)
            throw new DataValidationException("Ids", "At least one user ID is required");

        var deletedUsers = await applicationDbContext.UserAccounts
            .Where(x => request.Ids.Contains(x.Id) && x.IsDeleted)
            .ToListAsync(cancellationToken);

        var restoredIds = deletedUsers.Select(x => x.Id).ToList();
        var notFoundIds = request.Ids.Except(restoredIds).ToList();

        foreach (var user in deletedUsers)
        {
            user.IsDeleted = false;
            user.ModifiedAt = DateTime.UtcNow;
            user.ModifiedBy = currentUserService.UserId;
        }

        if (deletedUsers.Count > 0)
            await applicationDbContext.SaveChanges(cancellationToken);

        return new RestoreUsersResult(deletedUsers.Count, restoredIds, notFoundIds);
    }
}
