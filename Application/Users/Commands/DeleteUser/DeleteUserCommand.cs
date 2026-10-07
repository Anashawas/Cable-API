using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Users.Commands.DeleteUser;

public record DeleteUserCommand(int Id) : IRequest;

public class DeleteUserCommandHandler(IApplicationDbContext applicationDbContext) : IRequestHandler<DeleteUserCommand>
{
    public async Task Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        var user = await applicationDbContext.UserAccounts.FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken: cancellationToken);

        if (user == null)
        {
            throw new NotFoundException();
        }

        user.IsDeleted = true;

        // Drop the push registrations with the account. NotificationToken has no
        // IsDeleted of its own, so a row left behind keeps the device reachable
        // by anything that reads the table — filtering at each send site is a
        // guard, not a substitute for the account no longer being registered.
        //
        // Rows are removed rather than soft-deleted because a token is a device
        // handle, not history: it expires, rotates, and is re-registered from
        // scratch on the next sign-in. RestoreUsers therefore brings the account
        // back without its old devices, which is the correct outcome.
        var tokens = await applicationDbContext.NotificationTokens
            .Where(x => x.UserId == user.Id)
            .ToListAsync(cancellationToken);

        if (tokens.Count > 0)
            applicationDbContext.NotificationTokens.RemoveRange(tokens);

        // Ends every live session immediately. Without this the account keeps a
        // valid access token for its full lifetime; the account guard in the
        // request pipeline already rejects it, but rotating the stamps means a
        // token cannot be replayed even if that guard is ever bypassed.
        user.SecurityStamp = Guid.NewGuid().ToString();
        user.ProviderSecurityStamp = null;
        user.ProviderWebSecurityStamp = null;

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
