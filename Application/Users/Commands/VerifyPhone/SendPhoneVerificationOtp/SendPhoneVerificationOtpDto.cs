namespace Application.Users.Commands.VerifyPhone.SendPhoneVerificationOtp;

public record SendPhoneVerificationOtpDto(
    bool Success,
    string Message,
    DateTime? ExpiresAt
);
