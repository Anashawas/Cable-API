using Application.Users.Queries.GetAllUsers;
using Application.Users.Queries.GetUserById;
using Domain.Enitites;

namespace Application.Common.Models;

/// <summary>
/// SQL projection class for raw SQL query results when getting complete user details
/// </summary>
public class GetUserDetailsResult
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; }
    public string? Email { get; set; }
    public string? RegistrationProvider { get; set; }
    public string? FirebaseUId { get; set; }
    public string? Country { get; set; }
    public string? City { get; set; }
    public int RoleId { get; set; }
    public string? Password { get; set; }
    public bool IsDeleted { get; set; }
    public string? RoleName { get; set; }
    public int? UserCarId { get; set; }
    public int? CarModelId { get; set; }
    public string? CarModelName { get; set; }
    public int? CarTypeId { get; set; }
    public string? CarTypeName { get; set; }
    public int? PlugTypeId { get; set; }
    public string? PlugTypeName { get; set; }

    /// <summary>
    /// Converts the SQL projection results to a complete UserAccount object
    /// </summary>
    public static UserAccount ToUserAccount(List<GetUserDetailsResult> results)
    {
        if (!results.Any())
            throw new ArgumentException("Results cannot be empty", nameof(results));

        var firstResult = results.First();
        
        var userAccount = new UserAccount
        {
            Id = firstResult.Id,
            Name = firstResult.Name,
            Phone = firstResult.Phone,
            IsActive = firstResult.IsActive,
            Email = firstResult.Email,
            RegistrationProvider = firstResult.RegistrationProvider,
            FirebaseUId = firstResult.FirebaseUId,
            Country = firstResult.Country,
            City = firstResult.City,
            RoleId = firstResult.RoleId,
            Password = firstResult.Password,
            IsDeleted = firstResult.IsDeleted,
            Role = new Role 
            { 
                Id = firstResult.RoleId, 
                Name = firstResult.RoleName ?? string.Empty 
            }
        };

        // Load user cars
        var userCars = results
            .Where(r => r.UserCarId.HasValue)
            .Select(r => new UserCar
            {
                Id = r.UserCarId!.Value,
                UserId = r.Id,
                CarModelId = r.CarModelId ?? 0,
                PlugTypeId = r.PlugTypeId ?? 0,
                CarModel = new CarModel
                {
                    Id = r.CarModelId ?? 0,
                    Name = r.CarModelName ?? string.Empty,
                    CarTypeId = r.CarTypeId ?? 0,
                    CarType = new CarType
                    {
                        Id = r.CarTypeId ?? 0,
                        Name = r.CarTypeName ?? string.Empty
                    }
                },
                PlugType = new PlugType
                {
                    Id = r.PlugTypeId ?? 0,
                    Name = r.PlugTypeName ?? string.Empty
                }
            })
            .DistinctBy(x => x.Id) 
            .ToList();

        userAccount.UserCars = userCars;
        
        return userAccount;
    }
}