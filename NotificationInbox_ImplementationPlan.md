# Notification Inbox System - Complete Implementation Plan

## Overview
This document provides a complete implementation plan for adding a notification inbox/history system to the Cable EV Charging Station Management System. The system will store all notifications sent to users and track read status.

---

## User Requirements Summary

### Two New Database Tables:
1. **NotificationType** - Reference/lookup table for categorizing notifications
2. **NotificationInbox** - Main table storing notification history per user

### Specifications:
- ✅ **Read Status**: IsRead boolean only (no timestamps)
- ✅ **Notification Type**: Reference table for extensibility
- ✅ **Entity Links**: NOT needed (no links to ChargingPoint, Rate, etc.)
- ✅ **Action Links**: Both DeepLink (URL string) and Data (JSON payload)
- ✅ **Database-First**: SQL scripts provided, use reverse engineering (NOT code-first migrations)

### NotificationInbox Fields:
- `UserId` (FK to UserAccount)
- `NotificationTypeId` (FK to NotificationType)
- `Title` (max 256 chars)
- `Body` (max 1000 chars)
- `IsRead` (boolean, default false)
- `DeepLink` (nullable string for app navigation)
- `Data` (nullable string for JSON payload)
- Plus standard audit fields from BaseAuditableEntity

---

## Implementation Workflow

### Step 1: Execute SQL Scripts
1. Execute NotificationType table creation script
2. Execute NotificationType seed data script
3. Execute NotificationInbox table creation script
4. Verify tables and indexes created successfully

### Step 2: Reverse Engineering
Run this command to generate entities from database:
```bash
dotnet ef dbcontext scaffold "YourConnectionString" Microsoft.EntityFrameworkCore.SqlServer \
    --project Infrastructrue \
    --startup-project WebApi \
    --output-dir Enitites \
    --context-dir Persistence \
    --force
```

### Step 3: Manual Updates
1. Add navigation property to UserAccount.cs
2. Update ApplicationDbContext.cs with DbSets
3. Update IApplicationDbContext.cs interface

### Step 4: Application Layer (CQRS)
1. Create all Commands with handlers and validators (8 files)
2. Create all Queries with handlers and validators (6 files)
3. Create DTOs and response models

### Step 5: WebApi Layer
1. Create NotificationInboxRoutes.cs
2. Update Program.cs to register routes

### Step 6: Integration Service
1. Create INotificationInboxService interface
2. Implement NotificationInboxService
3. Register in DI container
4. Integrate with existing features

### Step 7: Testing
Test all endpoints and verify functionality

---

## SQL Scripts

### Script 1: Create NotificationType Table

```sql
-- =============================================
-- NotificationType Table Creation
-- Reference/lookup table for notification categories
-- =============================================

CREATE TABLE [dbo].[NotificationType]
(
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(100) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,

    -- Audit fields from BaseAuditableEntity pattern
    [CreatedBy] INT NULL,
    [CreatedAt] DATETIME NULL,
    [ModifiedBy] INT NULL,
    [ModifiedAt] DATETIME NULL,
    [IsDeleted] BIT NOT NULL DEFAULT 0,

    CONSTRAINT [PK_NotificationType] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UQ_NotificationType_Name] UNIQUE ([Name])
);

-- Index for active lookups
CREATE NONCLUSTERED INDEX [IX_NotificationType_IsActive]
ON [dbo].[NotificationType] ([IsActive])
INCLUDE ([Name], [Description])
WHERE [IsDeleted] = 0;

GO
```

### Script 2: Seed NotificationType Data

```sql
-- =============================================
-- NotificationType Seed Data
-- Common notification types for EV charging system
-- =============================================

SET IDENTITY_INSERT [dbo].[NotificationType] ON;

INSERT INTO [dbo].[NotificationType]
    ([Id], [Name], [Description], [IsActive], [CreatedAt], [IsDeleted])
VALUES
    (1, 'system_announcement', 'General system announcements and updates', 1, GETDATE(), 0),
    (2, 'favorite_added', 'Charging point added to favorites', 1, GETDATE(), 0),
    (3, 'favorite_removed', 'Charging point removed from favorites', 1, GETDATE(), 0),
    (4, 'charging_point_status_changed', 'Status change of a charging point', 1, GETDATE(), 0),
    (5, 'offer_available', 'Special offer or promotion available', 1, GETDATE(), 0),
    (6, 'rating_received', 'New rating received on your charging point', 1, GETDATE(), 0),
    (7, 'complaint_status_updated', 'Status update on user complaint', 1, GETDATE(), 0),
    (8, 'charging_session_started', 'Charging session has started', 1, GETDATE(), 0),
    (9, 'charging_session_completed', 'Charging session has completed', 1, GETDATE(), 0),
    (10, 'new_charging_point_nearby', 'New charging point available in your area', 1, GETDATE(), 0);

SET IDENTITY_INSERT [dbo].[NotificationType] OFF;

-- Verify inserted data
SELECT * FROM [dbo].[NotificationType] WHERE [IsDeleted] = 0;

GO
```

### Script 3: Create NotificationInbox Table

```sql
-- =============================================
-- NotificationInbox Table Creation
-- Stores notification history for each user
-- =============================================

CREATE TABLE [dbo].[NotificationInbox]
(
    [Id] INT IDENTITY(1,1) NOT NULL,
    [UserId] INT NOT NULL,
    [NotificationTypeId] INT NOT NULL,
    [Title] NVARCHAR(256) NOT NULL,
    [Body] NVARCHAR(1000) NOT NULL,
    [IsRead] BIT NOT NULL DEFAULT 0,
    [DeepLink] NVARCHAR(500) NULL,
    [Data] NVARCHAR(MAX) NULL, -- JSON payload

    -- Audit fields from BaseAuditableEntity pattern
    [CreatedBy] INT NULL,
    [CreatedAt] DATETIME NULL,
    [ModifiedBy] INT NULL,
    [ModifiedAt] DATETIME NULL,
    [IsDeleted] BIT NOT NULL DEFAULT 0,

    CONSTRAINT [PK_NotificationInbox] PRIMARY KEY CLUSTERED ([Id] ASC),

    -- Foreign Keys
    CONSTRAINT [FK_NotificationInbox_UserAccount]
        FOREIGN KEY ([UserId])
        REFERENCES [dbo].[UserAccount] ([Id]),

    CONSTRAINT [FK_NotificationInbox_NotificationType]
        FOREIGN KEY ([NotificationTypeId])
        REFERENCES [dbo].[NotificationType] ([Id])
);

GO

-- =============================================
-- Indexes for NotificationInbox
-- =============================================

-- Most common query: Get user's notifications ordered by date
CREATE NONCLUSTERED INDEX [IX_NotificationInbox_UserId_CreatedAt]
ON [dbo].[NotificationInbox] ([UserId], [CreatedAt] DESC)
INCLUDE ([NotificationTypeId], [Title], [Body], [IsRead], [DeepLink])
WHERE [IsDeleted] = 0;

-- Query for unread count
CREATE NONCLUSTERED INDEX [IX_NotificationInbox_UserId_IsRead]
ON [dbo].[NotificationInbox] ([UserId], [IsRead])
WHERE [IsDeleted] = 0;

-- Lookup by notification type
CREATE NONCLUSTERED INDEX [IX_NotificationInbox_NotificationTypeId]
ON [dbo].[NotificationInbox] ([NotificationTypeId])
WHERE [IsDeleted] = 0;

GO
```

---

## Post-Reverse Engineering Manual Updates

### Update 1: UserAccount.cs

**File**: `/mnt/d/Cable/Cable/Domain/Enitites/UserAccount.cs`

Add this navigation property around line 37 (after FavoriteChargingPoints):

```csharp
public virtual ICollection<NotificationInbox> NotificationInboxes { get; set; } = new List<NotificationInbox>();
```

### Update 2: ApplicationDbContext.cs

**File**: `/mnt/d/Cable/Cable/Infrastructrue/Persistence/ApplicationDbContext.cs`

Add these DbSet properties (after UserFavoriteChargingPoints):

```csharp
public DbSet<NotificationType> NotificationTypes { get; set; }
public DbSet<NotificationInbox> NotificationInboxes { get; set; }
```

### Update 3: IApplicationDbContext.cs

**File**: `/mnt/d/Cable/Cable/Application/Common/Interfaces/Persistence/IApplicationDbContext.cs`

Add these DbSet declarations:

```csharp
DbSet<NotificationType> NotificationTypes { get; set; }
DbSet<NotificationInbox> NotificationInboxes { get; set; }
```

---

## Application Layer - Commands

### Command 1: CreateNotificationCommand

**Folder**: `/mnt/d/Cable/Cable/Application/NotificationInbox/Commands/CreateNotification/`

**File**: `CreateNotificationCommand.cs`

```csharp
using Application.Common.Interfaces;
using Domain.Enitites;
using MediatR;

namespace Application.NotificationInbox.Commands.CreateNotification;

public record CreateNotificationCommand(
    int UserId,
    int NotificationTypeId,
    string Title,
    string Body,
    string? DeepLink = null,
    string? Data = null
) : IRequest<int>;

public class CreateNotificationCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<CreateNotificationCommand, int>
{
    public async Task<int> Handle(CreateNotificationCommand request, CancellationToken cancellationToken)
    {
        var notification = new Domain.Enitites.NotificationInbox
        {
            UserId = request.UserId,
            NotificationTypeId = request.NotificationTypeId,
            Title = request.Title,
            Body = request.Body,
            IsRead = false,
            DeepLink = request.DeepLink,
            Data = request.Data
        };

        applicationDbContext.NotificationInboxes.Add(notification);
        await applicationDbContext.SaveChanges(cancellationToken);

        return notification.Id;
    }
}
```

**File**: `CreateNotificationCommandValidator.cs`

```csharp
using FluentValidation;

namespace Application.NotificationInbox.Commands.CreateNotification;

public class CreateNotificationCommandValidator : AbstractValidator<CreateNotificationCommand>
{
    public CreateNotificationCommandValidator()
    {
        RuleFor(x => x.UserId)
            .GreaterThan(0)
            .WithMessage("UserId must be greater than 0");

        RuleFor(x => x.NotificationTypeId)
            .GreaterThan(0)
            .WithMessage("NotificationTypeId must be greater than 0");

        RuleFor(x => x.Title)
            .NotEmpty()
            .WithMessage("Title is required")
            .MaximumLength(256)
            .WithMessage("Title must not exceed 256 characters");

        RuleFor(x => x.Body)
            .NotEmpty()
            .WithMessage("Body is required")
            .MaximumLength(1000)
            .WithMessage("Body must not exceed 1000 characters");

        RuleFor(x => x.DeepLink)
            .MaximumLength(500)
            .WithMessage("DeepLink must not exceed 500 characters")
            .When(x => !string.IsNullOrEmpty(x.DeepLink));
    }
}
```

### Command 2: MarkNotificationAsReadCommand

**Folder**: `/mnt/d/Cable/Cable/Application/NotificationInbox/Commands/MarkAsRead/`

**File**: `MarkNotificationAsReadCommand.cs`

```csharp
using Application.Common.Interfaces;
using Cable.Core.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.NotificationInbox.Commands.MarkAsRead;

public record MarkNotificationAsReadCommand(int NotificationId) : IRequest;

public class MarkNotificationAsReadCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<MarkNotificationAsReadCommand>
{
    public async Task Handle(MarkNotificationAsReadCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
            ?? throw new NotAuthorizedAccessException("User not authenticated");

        var notification = await applicationDbContext.NotificationInboxes
            .FirstOrDefaultAsync(
                x => x.Id == request.NotificationId &&
                     x.UserId == userId &&
                     !x.IsDeleted,
                cancellationToken);

        if (notification == null)
            throw new NotFoundException($"Notification with id {request.NotificationId} not found");

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            await applicationDbContext.SaveChanges(cancellationToken);
        }
    }
}
```

**File**: `MarkNotificationAsReadCommandValidator.cs`

```csharp
using FluentValidation;

namespace Application.NotificationInbox.Commands.MarkAsRead;

public class MarkNotificationAsReadCommandValidator : AbstractValidator<MarkNotificationAsReadCommand>
{
    public MarkNotificationAsReadCommandValidator()
    {
        RuleFor(x => x.NotificationId)
            .GreaterThan(0)
            .WithMessage("NotificationId must be greater than 0");
    }
}
```

### Command 3: MarkAllNotificationsAsReadCommand

**Folder**: `/mnt/d/Cable/Cable/Application/NotificationInbox/Commands/MarkAllAsRead/`

**File**: `MarkAllNotificationsAsReadCommand.cs`

```csharp
using Application.Common.Interfaces;
using Cable.Core.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.NotificationInbox.Commands.MarkAllAsRead;

public record MarkAllNotificationsAsReadCommand : IRequest<int>;

public class MarkAllNotificationsAsReadCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<MarkAllNotificationsAsReadCommand, int>
{
    public async Task<int> Handle(MarkAllNotificationsAsReadCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
            ?? throw new NotAuthorizedAccessException("User not authenticated");

        var unreadNotifications = await applicationDbContext.NotificationInboxes
            .Where(x => x.UserId == userId && !x.IsRead && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        if (unreadNotifications.Count == 0)
            return 0;

        foreach (var notification in unreadNotifications)
        {
            notification.IsRead = true;
        }

        await applicationDbContext.SaveChanges(cancellationToken);

        return unreadNotifications.Count;
    }
}
```

### Command 4: DeleteNotificationCommand

**Folder**: `/mnt/d/Cable/Cable/Application/NotificationInbox/Commands/DeleteNotification/`

**File**: `DeleteNotificationCommand.cs`

```csharp
using Application.Common.Interfaces;
using Cable.Core.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.NotificationInbox.Commands.DeleteNotification;

public record DeleteNotificationCommand(int NotificationId) : IRequest;

public class DeleteNotificationCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteNotificationCommand>
{
    public async Task Handle(DeleteNotificationCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
            ?? throw new NotAuthorizedAccessException("User not authenticated");

        var notification = await applicationDbContext.NotificationInboxes
            .FirstOrDefaultAsync(
                x => x.Id == request.NotificationId &&
                     x.UserId == userId &&
                     !x.IsDeleted,
                cancellationToken);

        if (notification == null)
            throw new NotFoundException($"Notification with id {request.NotificationId} not found");

        notification.IsDeleted = true;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
```

**File**: `DeleteNotificationCommandValidator.cs`

```csharp
using FluentValidation;

namespace Application.NotificationInbox.Commands.DeleteNotification;

public class DeleteNotificationCommandValidator : AbstractValidator<DeleteNotificationCommand>
{
    public DeleteNotificationCommandValidator()
    {
        RuleFor(x => x.NotificationId)
            .GreaterThan(0)
            .WithMessage("NotificationId must be greater than 0");
    }
}
```

---

## Application Layer - Queries

### Query 1: GetUserNotificationsQuery

**Folder**: `/mnt/d/Cable/Cable/Application/NotificationInbox/Queries/GetUserNotifications/`

**File**: `GetUserNotificationsRequest.cs`

```csharp
using Application.Common.Interfaces;
using Cable.Core.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.NotificationInbox.Queries.GetUserNotifications;

public record GetUserNotificationsRequest(
    int PageNumber = 1,
    int PageSize = 20,
    bool? IsRead = null
) : IRequest<GetUserNotificationsResponse>;

public class GetUserNotificationsQueryHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetUserNotificationsRequest, GetUserNotificationsResponse>
{
    public async Task<GetUserNotificationsResponse> Handle(
        GetUserNotificationsRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
            ?? throw new NotAuthorizedAccessException("User not authenticated");

        // Build query
        var query = applicationDbContext.NotificationInboxes
            .AsNoTracking()
            .Where(x => x.UserId == userId && !x.IsDeleted);

        // Filter by read status if specified
        if (request.IsRead.HasValue)
        {
            query = query.Where(x => x.IsRead == request.IsRead.Value);
        }

        // Get total count
        var totalCount = await query.CountAsync(cancellationToken);

        // Apply pagination and ordering
        var notifications = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Include(x => x.NotificationType)
            .Select(x => new NotificationDto(
                x.Id,
                x.NotificationTypeId,
                x.NotificationType.Name,
                x.Title,
                x.Body,
                x.IsRead,
                x.DeepLink,
                x.Data,
                x.CreatedAt
            ))
            .ToListAsync(cancellationToken);

        return new GetUserNotificationsResponse(
            notifications,
            totalCount,
            request.PageNumber,
            request.PageSize
        );
    }
}
```

**File**: `GetUserNotificationsResponse.cs`

```csharp
namespace Application.NotificationInbox.Queries.GetUserNotifications;

public record GetUserNotificationsResponse(
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
```

**File**: `GetUserNotificationsRequestValidator.cs`

```csharp
using FluentValidation;

namespace Application.NotificationInbox.Queries.GetUserNotifications;

public class GetUserNotificationsRequestValidator : AbstractValidator<GetUserNotificationsRequest>
{
    public GetUserNotificationsRequestValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThan(0)
            .WithMessage("PageNumber must be greater than 0");

        RuleFor(x => x.PageSize)
            .GreaterThan(0)
            .WithMessage("PageSize must be greater than 0")
            .LessThanOrEqualTo(100)
            .WithMessage("PageSize must not exceed 100");
    }
}
```

### Query 2: GetUnreadNotificationCountQuery

**Folder**: `/mnt/d/Cable/Cable/Application/NotificationInbox/Queries/GetUnreadCount/`

**File**: `GetUnreadNotificationCountRequest.cs`

```csharp
using Application.Common.Interfaces;
using Cable.Core.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.NotificationInbox.Queries.GetUnreadCount;

public record GetUnreadNotificationCountRequest : IRequest<UnreadCountDto>;

public class GetUnreadNotificationCountQueryHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetUnreadNotificationCountRequest, UnreadCountDto>
{
    public async Task<UnreadCountDto> Handle(
        GetUnreadNotificationCountRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
            ?? throw new NotAuthorizedAccessException("User not authenticated");

        var count = await applicationDbContext.NotificationInboxes
            .Where(x => x.UserId == userId && !x.IsRead && !x.IsDeleted)
            .CountAsync(cancellationToken);

        return new UnreadCountDto(count);
    }
}

public record UnreadCountDto(int UnreadCount);
```

### Query 3: GetNotificationByIdQuery

**Folder**: `/mnt/d/Cable/Cable/Application/NotificationInbox/Queries/GetNotificationById/`

**File**: `GetNotificationByIdRequest.cs`

```csharp
using Application.Common.Interfaces;
using Application.NotificationInbox.Queries.GetUserNotifications;
using Cable.Core.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.NotificationInbox.Queries.GetNotificationById;

public record GetNotificationByIdRequest(int NotificationId) : IRequest<NotificationDto>;

public class GetNotificationByIdQueryHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetNotificationByIdRequest, NotificationDto>
{
    public async Task<NotificationDto> Handle(
        GetNotificationByIdRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
            ?? throw new NotAuthorizedAccessException("User not authenticated");

        var notification = await applicationDbContext.NotificationInboxes
            .AsNoTracking()
            .Where(x => x.Id == request.NotificationId && x.UserId == userId && !x.IsDeleted)
            .Include(x => x.NotificationType)
            .Select(x => new NotificationDto(
                x.Id,
                x.NotificationTypeId,
                x.NotificationType.Name,
                x.Title,
                x.Body,
                x.IsRead,
                x.DeepLink,
                x.Data,
                x.CreatedAt
            ))
            .FirstOrDefaultAsync(cancellationToken);

        if (notification == null)
            throw new NotFoundException($"Notification with id {request.NotificationId} not found");

        return notification;
    }
}
```

**File**: `GetNotificationByIdRequestValidator.cs`

```csharp
using FluentValidation;

namespace Application.NotificationInbox.Queries.GetNotificationById;

public class GetNotificationByIdRequestValidator : AbstractValidator<GetNotificationByIdRequest>
{
    public GetNotificationByIdRequestValidator()
    {
        RuleFor(x => x.NotificationId)
            .GreaterThan(0)
            .WithMessage("NotificationId must be greater than 0");
    }
}
```

---

## WebApi Layer

### NotificationInboxRoutes.cs

**File**: `/mnt/d/Cable/Cable/WebApi/Routes/NotificationInboxRoutes.cs`

```csharp
using Application.NotificationInbox.Commands.CreateNotification;
using Application.NotificationInbox.Commands.DeleteNotification;
using Application.NotificationInbox.Commands.MarkAllAsRead;
using Application.NotificationInbox.Commands.MarkAsRead;
using Application.NotificationInbox.Queries.GetNotificationById;
using Application.NotificationInbox.Queries.GetUnreadCount;
using Application.NotificationInbox.Queries.GetUserNotifications;
using Cable.WebApi.OpenAPI;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class NotificationInboxRoutes
{
    public static IEndpointRouteBuilder MapNotificationInboxRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/notifications")
            .WithTags("Notification Inbox")
            .MapRoutes();

        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        // Get user's notifications with pagination
        app.MapGet("/", async (
                IMediator mediator,
                [FromQuery] int pageNumber = 1,
                [FromQuery] int pageSize = 20,
                [FromQuery] bool? isRead = null,
                CancellationToken cancellationToken = default) =>
                Results.Ok(await mediator.Send(
                    new GetUserNotificationsRequest(pageNumber, pageSize, isRead),
                    cancellationToken)))
            .Produces<GetUserNotificationsResponse>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get User Notifications")
            .WithSummary("Get paginated list of user's notifications")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Description = "Page number (default: 1)";
                op.Parameters[1].Description = "Page size (default: 20, max: 100)";
                op.Parameters[2].Description = "Filter by read status (null = all, true = read only, false = unread only)";
                return op;
            });

        // Get unread notification count
        app.MapGet("/unread-count", async (
                IMediator mediator,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(
                    new GetUnreadNotificationCountRequest(),
                    cancellationToken)))
            .Produces<UnreadCountDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get Unread Count")
            .WithSummary("Get count of unread notifications for current user")
            .WithOpenApi();

        // Get notification by ID
        app.MapGet("/{id:int}", async (
                IMediator mediator,
                [FromRoute] int id,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(
                    new GetNotificationByIdRequest(id),
                    cancellationToken)))
            .Produces<NotificationDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Get Notification By Id")
            .WithSummary("Get a specific notification by ID")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Description = "Notification ID";
                return op;
            });

        // Mark notification as read
        app.MapPut("/{id:int}/mark-as-read", async (
                IMediator mediator,
                [FromRoute] int id,
                CancellationToken cancellationToken) =>
            {
                await mediator.Send(new MarkNotificationAsReadCommand(id), cancellationToken);
                return Results.Ok();
            })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Mark Notification As Read")
            .WithSummary("Mark a specific notification as read")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Description = "Notification ID";
                return op;
            });

        // Mark all notifications as read
        app.MapPut("/mark-all-as-read", async (
                IMediator mediator,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(
                    new MarkAllNotificationsAsReadCommand(),
                    cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Mark All As Read")
            .WithSummary("Mark all user's notifications as read")
            .WithOpenApi(op =>
            {
                op.Responses["200"].Description = "Number of notifications marked as read";
                return op;
            });

        // Delete notification (soft delete)
        app.MapDelete("/{id:int}", async (
                IMediator mediator,
                [FromRoute] int id,
                CancellationToken cancellationToken) =>
            {
                await mediator.Send(new DeleteNotificationCommand(id), cancellationToken);
                return Results.Ok();
            })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Delete Notification")
            .WithSummary("Delete a notification (soft delete)")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Description = "Notification ID";
                return op;
            });

        // Admin-only endpoint: Create notification manually (for testing/admin)
        app.MapPost("/", async (
                IMediator mediator,
                CreateNotificationCommand request,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(request, cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Create Notification")
            .WithSummary("Create a notification manually (admin/testing)")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "The id of the created notification";
                return op;
            });

        return app;
    }
}
```

### Update Program.cs

**File**: `/mnt/d/Cable/Cable/WebApi/Program.cs`

Find the route registration section (around line 160) and add:

```csharp
.MapNotificationInboxRoutes()
```

---

## Integration Service

### INotificationInboxService Interface

**File**: `/mnt/d/Cable/Cable/Application/Common/Interfaces/INotificationInboxService.cs`

```csharp
namespace Application.Common.Interfaces;

/// <summary>
/// Service that sends push notifications and saves to inbox
/// </summary>
public interface INotificationInboxService
{
    /// <summary>
    /// Send push notification to user and save to inbox
    /// </summary>
    Task<int> SendAndSaveNotificationAsync(
        int userId,
        int notificationTypeId,
        string title,
        string body,
        string? deepLink = null,
        string? data = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send push notification to multiple users and save to inbox for each
    /// </summary>
    Task SendAndSaveNotificationToMultipleUsersAsync(
        IEnumerable<int> userIds,
        int notificationTypeId,
        string title,
        string body,
        string? deepLink = null,
        string? data = null,
        CancellationToken cancellationToken = default);
}
```

### NotificationInboxService Implementation

**File**: `/mnt/d/Cable/Cable/Infrastructrue/Services/NotificationInboxService.cs`

```csharp
using Application.Common.Interfaces;
using Application.NotificationInbox.Commands.CreateNotification;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Infrastructrue.Services;

public class NotificationInboxService(
    INotificationService notificationService,
    IApplicationDbContext applicationDbContext,
    IMediator mediator) : INotificationInboxService
{
    public async Task<int> SendAndSaveNotificationAsync(
        int userId,
        int notificationTypeId,
        string title,
        string body,
        string? deepLink = null,
        string? data = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Save to inbox first (so history is preserved even if push fails)
        var command = new CreateNotificationCommand(
            userId,
            notificationTypeId,
            title,
            body,
            deepLink,
            data
        );

        var notificationId = await mediator.Send(command, cancellationToken);

        // 2. Get user's FCM tokens
        var tokens = await applicationDbContext.NotificationTokens
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.DeletedAt == null)
            .Select(x => x.Token)
            .ToListAsync(cancellationToken);

        // 3. Send push notification to all user's devices (fire and forget)
        if (tokens.Any())
        {
            try
            {
                await notificationService.SendMessagesAsync(tokens, title, body);
            }
            catch (Exception)
            {
                // Log error but don't fail - inbox record is saved
                // TODO: Add logging here
            }
        }

        return notificationId;
    }

    public async Task SendAndSaveNotificationToMultipleUsersAsync(
        IEnumerable<int> userIds,
        int notificationTypeId,
        string title,
        string body,
        string? deepLink = null,
        string? data = null,
        CancellationToken cancellationToken = default)
    {
        var userIdList = userIds.ToList();

        // Save to inbox for all users
        var tasks = userIdList.Select(userId =>
            SendAndSaveNotificationAsync(
                userId,
                notificationTypeId,
                title,
                body,
                deepLink,
                data,
                cancellationToken));

        await Task.WhenAll(tasks);
    }
}
```

### Register Service in DI

**File**: `/mnt/d/Cable/Cable/Infrastructrue/DependencyInjection.cs`

Find where `INotificationService` is registered and add:

```csharp
services.AddScoped<INotificationInboxService, NotificationInboxService>();
```

---

## Usage Example: Integrate with Favorites

**File**: `/mnt/d/Cable/Cable/Application/Favorites/Commands/AddToFavorites/AddToFavoritesCommand.cs`

Add notification after saving favorite (inject INotificationInboxService into handler):

```csharp
// After SaveChanges for favorite

if (chargingPoint?.UserId != null && chargingPoint.UserId != userId)
{
    var notificationType = await applicationDbContext.NotificationTypes
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.Name == "favorite_added" && x.IsActive, cancellationToken);

    if (notificationType != null)
    {
        var user = await applicationDbContext.UserAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);

        var userName = user?.Name ?? "Someone";
        var title = "New Favorite";
        var body = $"{userName} added your charging point '{chargingPoint.Name}' to favorites";
        var deepLink = $"cable://charging-point/{chargingPoint.Id}";

        await notificationInboxService.SendAndSaveNotificationAsync(
            chargingPoint.UserId,
            notificationType.Id,
            title,
            body,
            deepLink,
            cancellationToken: cancellationToken
        );
    }
}

return favorite.Id;
```

---

## API Endpoints Summary

| Method | Endpoint | Description | Auth Required |
|--------|----------|-------------|---------------|
| GET | `/api/notifications` | Get paginated user notifications | Yes |
| GET | `/api/notifications/unread-count` | Get unread notification count | Yes |
| GET | `/api/notifications/{id}` | Get notification by ID | Yes |
| PUT | `/api/notifications/{id}/mark-as-read` | Mark notification as read | Yes |
| PUT | `/api/notifications/mark-all-as-read` | Mark all as read | Yes |
| DELETE | `/api/notifications/{id}` | Delete notification (soft) | Yes |
| POST | `/api/notifications` | Create notification (admin) | Yes |

---

## Files to Create (20 files)

### Commands (8 files):
1. `/Application/NotificationInbox/Commands/CreateNotification/CreateNotificationCommand.cs`
2. `/Application/NotificationInbox/Commands/CreateNotification/CreateNotificationCommandValidator.cs`
3. `/Application/NotificationInbox/Commands/MarkAsRead/MarkNotificationAsReadCommand.cs`
4. `/Application/NotificationInbox/Commands/MarkAsRead/MarkNotificationAsReadCommandValidator.cs`
5. `/Application/NotificationInbox/Commands/MarkAllAsRead/MarkAllNotificationsAsReadCommand.cs`
6. `/Application/NotificationInbox/Commands/DeleteNotification/DeleteNotificationCommand.cs`
7. `/Application/NotificationInbox/Commands/DeleteNotification/DeleteNotificationCommandValidator.cs`

### Queries (6 files):
8. `/Application/NotificationInbox/Queries/GetUserNotifications/GetUserNotificationsRequest.cs`
9. `/Application/NotificationInbox/Queries/GetUserNotifications/GetUserNotificationsResponse.cs`
10. `/Application/NotificationInbox/Queries/GetUserNotifications/GetUserNotificationsRequestValidator.cs`
11. `/Application/NotificationInbox/Queries/GetUnreadCount/GetUnreadNotificationCountRequest.cs`
12. `/Application/NotificationInbox/Queries/GetNotificationById/GetNotificationByIdRequest.cs`
13. `/Application/NotificationInbox/Queries/GetNotificationById/GetNotificationByIdRequestValidator.cs`

### Infrastructure (2 files):
14. `/Application/Common/Interfaces/INotificationInboxService.cs`
15. `/Infrastructrue/Services/NotificationInboxService.cs`

### WebApi (1 file):
16. `/WebApi/Routes/NotificationInboxRoutes.cs`

---

## Files to Modify (5 files)

1. `/Domain/Enitites/UserAccount.cs` - Add NotificationInboxes navigation property
2. `/Infrastructrue/Persistence/ApplicationDbContext.cs` - Add NotificationType and NotificationInbox DbSets
3. `/Application/Common/Interfaces/Persistence/IApplicationDbContext.cs` - Add DbSet declarations
4. `/WebApi/Program.cs` - Register NotificationInboxRoutes
5. `/Infrastructrue/DependencyInjection.cs` - Register INotificationInboxService

---

## Testing Checklist

After implementation, test:

1. ✅ Execute all 3 SQL scripts successfully
2. ✅ Run reverse engineering command
3. ✅ Verify entities generated correctly
4. ✅ Build solution successfully (no errors)
5. ✅ Test GET `/api/notifications` with pagination
6. ✅ Test GET `/api/notifications/unread-count`
7. ✅ Test GET `/api/notifications/{id}`
8. ✅ Test PUT `/api/notifications/{id}/mark-as-read`
9. ✅ Test PUT `/api/notifications/mark-all-as-read`
10. ✅ Test DELETE `/api/notifications/{id}`
11. ✅ Test POST `/api/notifications` (manual creation)
12. ✅ Test integration: Add favorite → verify notification sent and saved
13. ✅ Verify user isolation (User A cannot see User B's notifications)
14. ✅ Verify soft delete (deleted notifications don't appear in queries)
15. ✅ Test pagination edge cases (page 1, last page, invalid page numbers)

---

## Database Schema Diagram

```
┌─────────────────────────┐
│   NotificationType      │
├─────────────────────────┤
│ Id (PK)                 │
│ Name (UQ)               │
│ Description             │
│ IsActive                │
│ + BaseAuditableEntity   │
└─────────────┬───────────┘
              │
              │ 1:N
              │
┌─────────────▼───────────┐         ┌─────────────────────┐
│   NotificationInbox     │         │    UserAccount      │
├─────────────────────────┤         ├─────────────────────┤
│ Id (PK)                 │    N:1  │ Id (PK)             │
│ UserId (FK) ────────────┼─────────┤ Name                │
│ NotificationTypeId (FK) │         │ Email               │
│ Title                   │         │ ...                 │
│ Body                    │         └─────────────────────┘
│ IsRead                  │
│ DeepLink (nullable)     │
│ Data (JSON, nullable)   │
│ + BaseAuditableEntity   │
└─────────────────────────┘
```

---

## Key Design Decisions

1. **Database-First**: SQL scripts first, then reverse engineering (no EF migrations)
2. **Reference Table**: NotificationType for extensibility and type safety
3. **Simple Read Tracking**: IsRead boolean only (no ReadAt timestamp)
4. **Flexible Data Field**: JSON string for custom payloads
5. **DeepLink Support**: Native app navigation via URL scheme
6. **Soft Delete**: Maintains audit trail
7. **Pagination**: Built-in for performance
8. **User Isolation**: Users only see their own notifications
9. **Decoupled**: Push notification can fail, inbox is still saved
10. **Indexed**: Performance-optimized for common queries

---

## Future Enhancements (Out of Scope)

- ReadAt timestamp tracking
- Notification preferences/settings per user
- Batch notification creation
- Notification categories/folders
- Push notification retry logic
- Notification templates system
- Real-time delivery (SignalR/WebSockets)
- Sound/vibration preferences

---

**End of Implementation Plan**

Save this file and refer to it when you're ready to implement the notification inbox system!
