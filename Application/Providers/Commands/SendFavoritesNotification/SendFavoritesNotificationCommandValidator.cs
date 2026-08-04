using FluentValidation;

namespace Application.Providers.Commands.SendFavoritesNotification;

public class SendFavoritesNotificationCommandValidator : AbstractValidator<SendFavoritesNotificationCommand>
{
    public SendFavoritesNotificationCommandValidator()
    {
        // F5: either a raw title or a notificationTypeId (title then built server-side).
        RuleFor(x => x.Title)
            .NotEmpty()
            .When(x => !x.NotificationTypeId.HasValue)
            .WithMessage("Send a title or a notificationTypeId.");
        RuleFor(x => x.Title).MaximumLength(100);
        RuleFor(x => x.NotificationTypeId).GreaterThan(0).When(x => x.NotificationTypeId.HasValue);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(500);
    }
}
