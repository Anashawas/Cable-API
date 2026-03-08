using Cable.Core.Enums;
using FluentValidation;

namespace Application.NotificationTokens.Commands.RefreshNotificationToken;

public class RefreshNotificationTokenCommandValidator : AbstractValidator<RefreshNotificationTokenCommand>
{
    public RefreshNotificationTokenCommandValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty()
            .WithMessage("FCM Token is required")
            .MaximumLength(500)
            .WithMessage("Token must not exceed 500 characters");

        RuleFor(x => x.OsName)
            .NotEmpty()
            .WithMessage("OS Name is required")
            .MaximumLength(100)
            .WithMessage("OS Name must not exceed 100 characters");

        RuleFor(x => x.OsVersion)
            .NotEmpty()
            .WithMessage("OS Version is required")
            .MaximumLength(100)
            .WithMessage("OS Version must not exceed 100 characters");

        RuleFor(x => x.AppVersion)
            .NotEmpty()
            .WithMessage("App Version is required")
            .MaximumLength(100)
            .WithMessage("App Version must not exceed 100 characters");

        RuleFor(x => x.AppType)
            .IsInEnum()
            .WithMessage("AppType must be a valid Firebase app type (1 = UserApp, 2 = StationApp)");
    }
}
