using Application.Users.Queries.GetAllUsers;
using Application.Users.Queries.GetUserById;
using Cable.Core.Exceptions;

namespace Application.Common.Extensions;

public static class UserAccountExtensions
{
    public static UserDetailsResult ToUserDetails(this UserAccount userAccount)
    {
        if (userAccount is null)
            throw new NotFoundException(nameof(userAccount), "UserAccount is null");

        return new UserDetailsResult(
            userAccount.Id,
            userAccount.Name,
            userAccount.Phone,
            userAccount.IsActive,
            userAccount.Email,
            userAccount.RegistrationProvider,
            userAccount.FirebaseUId,
            userAccount.Country,
            userAccount.City,
            userAccount.IsPhoneVerified,
            new RoleSummary(userAccount.RoleId, userAccount?.Role?.Name ?? string.Empty),
            userAccount?.UserCars?.GroupBy(uc => new { uc.CarModel.CarType.Id, uc.CarModel.CarType.Name })
                .Select(carTypeGroup => new UserCarTypeDto(
                    carTypeGroup.Key.Id,
                    carTypeGroup.Key.Name,
                    carTypeGroup.GroupBy(uc => new { uc.CarModel.Id, uc.CarModel.Name })
                        .Select(carModelGroup => new UserCarModelDto(
                            carModelGroup.First().Id,
                            carModelGroup.Key.Id,
                            carModelGroup.Key.Name,
                            new UserPlugTypeDto(
                                carModelGroup.First().PlugType.Id,
                                carModelGroup.First().PlugType.Name,
                                carModelGroup.First().PlugType.SerialNumber ?? string.Empty
                            )
                        )).ToList()
                )).ToList() ?? []
        );
      
    }
}