namespace Application.Common.Models.Results;

public record FirebaseTokenValidationResult(string FirebaseUId, string? RegistrationProvider, string? Email, string? Name);