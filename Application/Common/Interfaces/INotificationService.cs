using Cable.Core.Enums;

namespace Application.Common.Interfaces;

public interface INotificationService
{
    /// <summary>
    /// Sends a push notification to a single device
    /// </summary>
    /// <param name="token">Device FCM token</param>
    /// <param name="title">Notification title</param>
    /// <param name="body">Notification body</param>
    /// <param name="appType">Firebase app type (UserApp or StationApp)</param>
    /// <returns>Message ID from Firebase</returns>
    Task<string> SendMessageAsync(
        string token,
        string title,
        string body,
        FirebaseAppType appType = FirebaseAppType.UserApp);

    /// <summary>
    /// Sends push notifications to multiple devices
    /// </summary>
    /// <param name="tokens">List of device FCM tokens</param>
    /// <param name="title">Notification title</param>
    /// <param name="body">Notification body</param>
    /// <param name="appType">Firebase app type (UserApp or StationApp)</param>
    /// <returns>Notification send result with success/failure counts</returns>
    Task<NotificationSendResult> SendMessagesAsync(
        IEnumerable<string> tokens,
        string title,
        string body,
        FirebaseAppType appType = FirebaseAppType.UserApp);
}

public class NotificationSendResult
{
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public List<string> SuccessfulTokens { get; set; } = new();
    public List<string> FailedTokens { get; set; } = new();
}