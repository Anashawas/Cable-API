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
