using Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.NotificationTypes.Queries.GetAllNotificationTypes;

public record GetAllNotificationTypesRequest : IRequest<List<NotificationTypeDto>>;

public class GetAllNotificationTypesQueryHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetAllNotificationTypesRequest, List<NotificationTypeDto>>
{
    public async Task<List<NotificationTypeDto>> Handle(
        GetAllNotificationTypesRequest request,
        CancellationToken cancellationToken)
    {
        var notificationTypes = await applicationDbContext.NotificationTypes
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new NotificationTypeDto(
                x.Id,
                x.Name,
                x.Description,
                x.DeepLinksTo,
                x.NameEn,
                x.NameAr
            ))
            .ToListAsync(cancellationToken);

        return notificationTypes;
    }
}

/// <summary>
/// DeepLinksTo (Part B R5) states where this type routes on tap:
/// "none" | "charging-point" | "service-provider" | "complaint" | "loyalty" |
/// "provider" (per-send: the attached deepLink decides charging-point vs
/// service-provider). The actual link always comes from the stored deepLink.
/// </summary>
public record NotificationTypeDto(
    int Id,
    string Name,
    string? Description,
    string? DeepLinksTo,
    string? NameEn,
    string? NameAr
);
