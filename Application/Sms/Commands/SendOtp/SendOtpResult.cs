namespace Application.Authentication.Commands.SendOtp;

public record SendOtpResult(bool Success, string Message, DateTime? ExpiresAt);