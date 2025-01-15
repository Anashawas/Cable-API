using Cable.Security.Encryption.Interfaces;

namespace Application.Users.Commands.AddUser;

public record AddUserCommand(
    string Name,
    string Phone,
    int RoleId,
    string? Password,
    string? Username
)
    : IRequest<int>;

public class AddUserCommandHandler(IApplicationDbContext applicationDbContext, IPasswordHasher passwordHasher)
    : IRequestHandler<AddUserCommand, int>
{
    public async Task<int> Handle(AddUserCommand request, CancellationToken cancellationToken)
    {
        var user = new UserAccount()
        {
            Name = request.Name,
            Phone = request.Phone,
            RoleId = request.RoleId,
            UserName = request.Username,
            Password = string.IsNullOrEmpty(request.Password) ? null : passwordHasher.HashPassword(request.Password),
            IsDeleted = false,
            IsActive = true
        };

        applicationDbContext.UserAccounts.Add(user);
        await applicationDbContext.SaveChanges(cancellationToken);

        return user.Id;
    }
}