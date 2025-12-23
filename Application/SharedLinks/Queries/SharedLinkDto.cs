namespace Application.SharedLinks.Queries;

public record SharedLinkDto(
    int Id,
    string LinkToken,
    string LinkType,
    int? TargetId,
    string? Parameters,
    DateTime? ExpiresAt,
    int MaxUsage,
    int CurrentUsage,
    bool IsActive,
    string? BaseUrl
);

public record SharedLinkTypeDto(
    int Id,
    string TypeName,
    string? Description,
    string BaseUrl,
    bool IsActive
);

public record SharedLinkUsageDto(
    int Id,
    int SharedLinkId,
    int? UserId,
    string? DeviceInfo,
    string? IpAddress,
    DateTime UsedAt,
    bool IsSuccessful,
    string? ErrorMessage
);