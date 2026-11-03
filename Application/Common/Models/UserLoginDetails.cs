using Application.Users.Queries.GetAllUsers;
using Application.Users.Queries.GetUserById;
using AutoMapper.Configuration.Annotations;
using Newtonsoft.Json;

namespace Application.Common.Models;

/// <summary>
/// The user login result
/// </summary>
/// <param name="Id">The id of the user</param>
/// <param name="Name">The display name</param>
/// <param name="AccessToken">The access token</param>
/// <param name="RefreshToken">The refresh token</param>
/// <param name="Privileges">The user privleges</param>
public record UserLoginResult(
    UserDetailsResult  UserDetails,
    string AccessToken,
    string RefreshToken,
    bool IsCompletedData,
    IReadOnlyCollection<string> Privileges,
    /// <summary>Worker login: the partner password is still the temporary one — the app must show the change screen before anything else.</summary>
    bool MustChangePassword = false,
    /// <summary>Worker login after an OTP: keep it on the device and send it with the next worker-login to skip the OTP.</summary>
    string? DeviceToken = null
);

public record UserDetailsResult(
    int Id,
    string? Name,
    string? Phone,
    bool? IsActive,
    string? Email,
    string? RegistrationProvider,
    string? FirebaseUId,
    string? Country,
    string? City,
    bool? IsPhoneVerified,
    bool HasReadUpdateNotes,
    RoleSummary? Role,
    List<UserCarTypeDto>? UserCars
);


/// <summary>
/// The user login result
/// </summary>
/// <param name="Id">The id of the user</param>
/// <param name="Name">The display name</param>
/// <param name="Privileges">The user privleges</param>

public record UserLoginByTokenResult(
    int Id,
    string? Name,
    IReadOnlyCollection<string> Privileges
);