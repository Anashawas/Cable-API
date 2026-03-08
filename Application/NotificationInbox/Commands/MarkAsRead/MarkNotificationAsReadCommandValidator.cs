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
