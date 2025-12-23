using Application.Users.Queries.GetAllUsers;

namespace Application.Users.Queries.GetUserById;

public record GetUserByIdDto(
    int Id,
    string? Name,
    string? Phone,
    bool IsActive,
    string? Email,
    string? RegistrationProvider,
    string? FirebaseUId,
    string? Country,
    string? City,
    bool IsPhoneVerified,
    RoleSummary Role,
    List<UserCarTypeDto> UserCars
);

public record UserCarTypeDto(int CarTypeId, string CarTypeName, List<UserCarModelDto> CarModels);

public record UserCarModelDto(int UserCarId, int CarModelId, string CarModelName, UserPlugTypeDto PlugTypes);

public record UserPlugTypeDto(int Id, string? Name, string SerialNumber);