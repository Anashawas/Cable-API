namespace Application.Users.Commands.RequestPasswordReset;

public record RequestPasswordResetDto(
    bool Success,
    string Message,
    DateTime? ExpiresAt
);
