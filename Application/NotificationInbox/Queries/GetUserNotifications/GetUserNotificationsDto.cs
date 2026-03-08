namespace Application.NotificationInbox.Queries.GetUserNotifications;

public record GetUserNotificationsDto(
    List<NotificationDto> Notifications,
    int TotalCount,
    int PageNumber,
    int PageSize
)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}

public record NotificationDto(
    int Id,
    int NotificationTypeId,
    string NotificationTypeName,
    string Title,
    string Body,
    bool IsRead,
    string? DeepLink,
    string? Data,
    DateTime? CreatedAt
);
