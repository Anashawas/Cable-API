using Application.Users;
using Cable.Core;
using Cable.Core.Exceptions;
using Cable.Security.Encryption.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Application.Users.Commands.ChangePassword;

public record ChangePasswordCommand(int Id, string Password) : IRequest;



public class ChangePasswordCommandHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService, IIdentityService identityService, IPasswordHasher passwordHasher): IRequestHandler<ChangePasswordCommand>
{
    public async Task Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await applicationDbContext.UserAccounts.FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken);

        if (user == null)
        {
            throw new NotFoundException();
        }

        // if (user.Id != currentUserService.UserId.Value)
        // {
        //     if(!await identityService.HasPrivilege(currentUserService.UserId.Value, "ManageUsers", cancellationToken))
        //     {
        //         throw new ForbiddenAccessException();
        //
        //     }
        // }

        user.Password = string.IsNullOrEmpty(request.Password) ? null : passwordHasher.HashPassword(request.Password);

        // Setting the password can be the step that completes a social account's
        // conversion to a partner login: if this user was already promoted to
        // Provider/Worker but kept its Google/Apple identity (because the password
        // did not exist at promotion time), clear it now that one exists.
        // Without this, an admin who promotes THEN sets the password leaves the
        // account permanently refused by the partner app.
        await ProviderAccountConverter.EnsureConvertedAsync(applicationDbContext, user, user.RoleId, cancellationToken);

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}