using Application.Users.Queries.GetAllUsers;

namespace Application.Users.Queries.GetUserById;

public record GetUserByIdDto(
    int Id,
    string Name,
    string? Phone,
    bool IsActive,
    string? Email,
    string? RegistrationProvider,
    string? FirebaseUId,
    string? Country,
    string? City,
    RoleSummary Role
);