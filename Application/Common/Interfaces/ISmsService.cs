using Application.Common.Models;

namespace Application.Common.Interfaces;

public interface ISmsService
{
    Task<bool> SendSmsAsync(string phoneNumber, string message, CancellationToken cancellationToken);
    Task<SmsResult> SendSmsWithResultAsync(string phoneNumber, string message, CancellationToken cancellationToken);
}