namespace Application.NotificationInbox.Queries.GetNotificationBatches;

public record GetNotificationBatchesDto(
    List<NotificationBatchDto> Batches,
    int TotalCount,
    int PageNumber,
    int PageSize
)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}

public record NotificationBatchDto(
    Guid BatchId,
    int NotificationTypeId,
    string NotificationTypeName,
    string Title,
    string Body,
    string? DeepLink,
    DateTime? SentAt,
    int TotalRecipients,
    int ReadCount,
    int UnreadCount,
    decimal ReadRate
);
