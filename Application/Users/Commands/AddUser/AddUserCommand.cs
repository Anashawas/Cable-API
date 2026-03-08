using Application.Common.Extensions;
using Application.Common.Interfaces.Repositories;
using Cable.Core;
using Cable.Core.Utilities;
using Cable.Security.Encryption.Interfaces;
using Cable.Core.Exceptions;

namespace Application.Users.Commands.AddUser;

public record AddUserCommand(
    string? Name,
    int RoleId,
    string? Email,
    string? Password,
    string ? Country,
    string ? City
)
    : IRequest<UserDetailsResult>;

public class AddUserCommandHandler(IApplicationDbContext applicationDbContext,IUserAccountRepository userAccountRepository, IPasswordHasher passwordHasher)
    : IRequestHandler<AddUserCommand, UserDetailsResult>
{
    public async Task<UserDetailsResult> Handle(AddUserCommand request, CancellationToken cancellationToken)
    {
        var user = new UserAccount()
        {
            Name = request.Name,
            Phone = null,
            RoleId = request.RoleId,
            Password = string.IsNullOrEmpty(request.Password) ? null : passwordHasher.HashPassword(request.Password),
            IsDeleted = false,
            IsActive = true,
            Email = request.Email,
            Country = request.Country,
            City = request.City,
            IsPhoneVerified = false
        };

        applicationDbContext.UserAccounts.Add(user);
        await applicationDbContext.SaveChanges(cancellationToken);

        var userAccount = await userAccountRepository.GetUserDetailsByIdAsync(user.Id, cancellationToken);
        return userAccount.ToUserDetails();
    }
}