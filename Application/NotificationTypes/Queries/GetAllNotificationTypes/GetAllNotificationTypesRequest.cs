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
                x.Description
            ))
            .ToListAsync(cancellationToken);

        return notificationTypes;
    }
}

public record NotificationTypeDto(
    int Id,
    string Name,
    string? Description
);
