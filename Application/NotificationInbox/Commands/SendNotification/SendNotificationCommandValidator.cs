using Application.Common.Interfaces;
using Application.NotificationInbox.Commands.AddNotification;
using Cable.Core.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.NotificationInbox.Commands.CreateNotification;

public class SendNotificationCommandValidator : AbstractValidator<SendNotificationCommand>
{
    private readonly IApplicationDbContext _applicationDbContext;

    public SendNotificationCommandValidator(IApplicationDbContext applicationDbContext)
    {
        _applicationDbContext = applicationDbContext;

        // UserIds validation only applies when IsForAll = false
        RuleFor(x => x.UserIds)
            .NotEmpty()
            .WithMessage("At least one user ID is required when IsForAll is false")
            .Must(userIds => userIds.All(id => id > 0))
            .WithMessage("All user IDs must be greater than 0")
            .MustAsync(CheckAllUsersExist)
            .WithMessage("One or more users do not exist")
            .When(x => !x.IsForAll);

        RuleFor(x => x.NotificationTypeId)
            .GreaterThan(0)
            .WithMessage("NotificationTypeId must be greater than 0")
            .MustAsync(CheckNotificationTypeExists)
            .WithMessage("Notification type does not exist");

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

        RuleFor(x => x.AppType)
            .IsInEnum()
            .WithMessage("AppType must be a valid Firebase app type (1 = UserApp, 2 = StationApp)");
    }

    private async Task<bool> CheckAllUsersExist(List<int> userIds, CancellationToken cancellationToken)
    {
        var existingUserIds = await _applicationDbContext.UserAccounts
            .Where(x => userIds.Contains(x.Id) && !x.IsDeleted)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        return existingUserIds.Count == userIds.Distinct().Count();
    }

    private async Task<bool> CheckNotificationTypeExists(int notificationTypeId, CancellationToken cancellationToken)
        => await _applicationDbContext.NotificationTypes.AnyAsync(x => x.Id == notificationTypeId , cancellationToken);
}
