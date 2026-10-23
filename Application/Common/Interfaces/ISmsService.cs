using Application.Common.Models;

namespace Application.Common.Interfaces;

public interface ISmsService
{
    Task<bool> SendSmsAsync(string phoneNumber, string message, CancellationToken cancellationToken);
    Task<SmsResult> SendSmsWithResultAsync(string phoneNumber, string message, CancellationToken cancellationToken);

    /// <summary>Asks the provider whether the account works, the sender name is active and how much balance is left. Sends no SMS.</summary>
    Task<SmsAccountStatus> CheckAccountAsync(CancellationToken cancellationToken);
}