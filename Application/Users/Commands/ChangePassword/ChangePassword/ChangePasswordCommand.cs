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

        if (user.Id != currentUserService.UserId.Value)
        {
            if(!await identityService.HasPrivilege(currentUserService.UserId.Value, "ManageUsers", cancellationToken))
            {
                throw new ForbiddenAccessException();

            }
        }

        user.Password = string.IsNullOrEmpty(request.Password) ? null : passwordHasher.HashPassword(request.Password);
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}