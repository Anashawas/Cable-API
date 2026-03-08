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
