using Application.Common.Extensions;
using Application.Common.Interfaces.Repositories;
using Application.Users;
using Cable.Core;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Users.Commands.UpdateUser;

public record UpdateUserCommand(
    int Id,
    string? Name,
    int RoleId,
    string? Email,
    bool IsActive,
    string? Country,
    string? City
    ) : IRequest<UserDetailsResult>;

public class UpdateUserCommandHandler(IApplicationDbContext applicationDbContext, IUserAccountRepository userAccountRepository) : IRequestHandler<UpdateUserCommand,UserDetailsResult>
{
    public async Task<UserDetailsResult> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        var user = await applicationDbContext.UserAccounts.FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted,
            cancellationToken) ?? throw new NotFoundException($"cannot find user with id: {request.Id}");

        user.Name = request.Name;
        user.RoleId = request.RoleId;
        user.IsActive = request.IsActive;
        user.Email = request.Email;
        user.Country = request.Country;
        user.City = request.City;

        // Promoting to Provider/Worker must strip any Google/Apple identity, or
        // the partner app refuses the account. Shared with ChangePassword so the
        // conversion happens whichever of role/password is set last.
        await ProviderAccountConverter.EnsureConvertedAsync(applicationDbContext, user, request.RoleId, cancellationToken);

        await applicationDbContext.SaveChanges(cancellationToken);
        var userAccount = await userAccountRepository.GetUserDetailsByIdAsync(user.Id, cancellationToken);
        return userAccount.ToUserDetails();
    }
}
