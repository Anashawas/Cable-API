using Cable.Security.Encryption.Interfaces;

namespace Application.Users.Commands.AddUser;

public record AddUserCommand(
    string Name,
    string Phone,
    int RoleId,
    string? Password,
    string? Username,
    string ? Email,
    string ? Country, 
    string ? City 
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
            Password = string.IsNullOrEmpty(request.Password) ? null : passwordHasher.HashPassword(request.Password),
            IsDeleted = false,
            IsActive = true,
            Email = request.Email,
            Country = request.Country,
            City = request.City
        };

        applicationDbContext.UserAccounts.Add(user);
        await applicationDbContext.SaveChanges(cancellationToken);

        return user.Id;
    }
}