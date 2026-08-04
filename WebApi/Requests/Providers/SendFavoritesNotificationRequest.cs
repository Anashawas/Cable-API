namespace Cable.Requests.Providers;

/// <summary>
/// F5: send { notificationTypeId, body } and the title is built server-side as
/// "{typeName} From {stationName}". A raw title is still accepted (legacy).
/// </summary>
public record SendFavoritesNotificationRequest(string? Title, string Body, int? NotificationTypeId = null);

/// <summary>F4 toggle payload.</summary>
public record SetAutoApproveRequest(bool Enabled);
