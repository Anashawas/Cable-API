namespace Application.Users.Commands.VerifyPhone.VerifyUserPhone;

public record VerifyUserPhoneDto(
    bool Success,
    string Message,
    string PhoneNumber,
    DateTime VerifiedAt
);
