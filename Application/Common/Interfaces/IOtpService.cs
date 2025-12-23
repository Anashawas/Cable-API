namespace Application.Common.Interfaces;

public interface IOtpService
{
    Task<string> GenerateOtpAsync(string phoneNumber, CancellationToken cancellationToken);
    Task<bool> SendOtpAsync(string phoneNumber, string otp, CancellationToken cancellationToken);
    Task<bool> VerifyOtpAsync(string phoneNumber, string otp, CancellationToken cancellationToken);
    Task<bool> IsRateLimitedAsync(string phoneNumber, CancellationToken cancellationToken);
}