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
