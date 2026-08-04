using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Exceptions;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.NotificationTemplates;

// ============================================================================
// Part B F5 — admin-managed body suggestions per notification type. The
// partner compose sheet fetches them as chips; admins CRUD them.
// ============================================================================

public record NotificationTemplateDto(int Id, int NotificationTypeId, string NotificationTypeName, string Body);

/// <summary>Any authenticated caller (the partner app needs these). Optional type filter.</summary>
public record GetNotificationTemplatesRequest(int? NotificationTypeId = null)
    : IRequest<List<NotificationTemplateDto>>;

public class GetNotificationTemplatesRequestHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetNotificationTemplatesRequest, List<NotificationTemplateDto>>
{
    public async Task<List<NotificationTemplateDto>> Handle(GetNotificationTemplatesRequest request,
        CancellationToken cancellationToken)
    {
        var query = applicationDbContext.NotificationTemplates.AsNoTracking()
            .Where(x => !x.IsDeleted);
        if (request.NotificationTypeId.HasValue)
            query = query.Where(x => x.NotificationTypeId == request.NotificationTypeId.Value);

        return await query
            .OrderBy(x => x.NotificationTypeId).ThenBy(x => x.Id)
            .Select(x => new NotificationTemplateDto(x.Id, x.NotificationTypeId, x.NotificationType.Name, x.Body))
            .ToListAsync(cancellationToken);
    }
}

public record CreateNotificationTemplateCommand(int NotificationTypeId, string Body) : IRequest<int>;

public class CreateNotificationTemplateCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<CreateNotificationTemplateCommand, int>
{
    public async Task<int> Handle(CreateNotificationTemplateCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > 1000)
            throw new DataValidationException("Body", "Body is required (max 1000 characters).");
        if (!await applicationDbContext.NotificationTypes
                .AnyAsync(t => t.Id == request.NotificationTypeId, cancellationToken))
            throw new NotFoundException($"can not find notification type with id {request.NotificationTypeId}");

        var template = new NotificationTemplate
        {
            NotificationTypeId = request.NotificationTypeId,
            Body = request.Body.Trim()
        };
        applicationDbContext.NotificationTemplates.Add(template);
        await applicationDbContext.SaveChanges(cancellationToken);
        return template.Id;
    }
}

public record UpdateNotificationTemplateCommand(int Id, int NotificationTypeId, string Body) : IRequest;

public class UpdateNotificationTemplateCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateNotificationTemplateCommand>
{
    public async Task Handle(UpdateNotificationTemplateCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var template = await applicationDbContext.NotificationTemplates
                           .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                       ?? throw new NotFoundException($"can not find notification template with id {request.Id}");

        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > 1000)
            throw new DataValidationException("Body", "Body is required (max 1000 characters).");
        if (!await applicationDbContext.NotificationTypes
                .AnyAsync(t => t.Id == request.NotificationTypeId, cancellationToken))
            throw new NotFoundException($"can not find notification type with id {request.NotificationTypeId}");

        template.NotificationTypeId = request.NotificationTypeId;
        template.Body = request.Body.Trim();
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}

public record DeleteNotificationTemplateCommand(int Id) : IRequest;

public class DeleteNotificationTemplateCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteNotificationTemplateCommand>
{
    public async Task Handle(DeleteNotificationTemplateCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var template = await applicationDbContext.NotificationTemplates
                           .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                       ?? throw new NotFoundException($"can not find notification template with id {request.Id}");

        template.IsDeleted = true;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
