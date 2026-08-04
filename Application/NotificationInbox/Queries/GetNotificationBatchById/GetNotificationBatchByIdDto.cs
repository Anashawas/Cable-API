namespace Application.NotificationInbox.Queries.GetNotificationBatchById;

public record NotificationBatchDetailDto(
    Guid BatchId,
    int NotificationTypeId,
    string NotificationTypeName,
    string Title,
    string Body,
    string? DeepLink,
    string? Data,
    DateTime? SentAt,
    int TotalRecipients,
    int ReadCount,
    int UnreadCount,
    decimal ReadRate,
    List<NotificationBatchRecipientDto> Recipients
);

public record NotificationBatchRecipientDto(
    int UserId,
    string? UserName,
    string? Phone,
    string? Email,
    bool IsRead,
    DateTime? ReceivedAt
);
