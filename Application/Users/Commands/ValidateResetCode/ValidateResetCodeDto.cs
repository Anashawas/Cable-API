namespace Application.Users.Commands.ValidateResetCode;

public record ValidateResetCodeDto(
    bool Success,
    string Message,
    DateTime ExpiresAt
);
