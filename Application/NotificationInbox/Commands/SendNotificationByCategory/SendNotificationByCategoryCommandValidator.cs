using Application.Common.Interfaces;
using Cable.Core.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.NotificationInbox.Commands.SendNotificationByFilter;

public class SendNotificationByCategoryCommandValidator : AbstractValidator<SendNotificationByCategoryCommand>
{
    private readonly IApplicationDbContext _applicationDbContext;

    public SendNotificationByCategoryCommandValidator(IApplicationDbContext applicationDbContext)
    {
        _applicationDbContext = applicationDbContext;

        RuleFor(x => x)
            .Must(HasAtLeastOneFilter)
            .WithMessage("At least one filter must be provided (CarTypeId, CarModelId, or City)");

        RuleFor(x => x.CarTypeId)
            .MustAsync(CheckCarTypeExists)
            .When(x => x.CarTypeId.HasValue)
            .WithMessage("Car type does not exist");

        RuleFor(x => x.CarModelId)
            .MustAsync(CheckCarModelExists)
            .When(x => x.CarModelId.HasValue)
            .WithMessage("Car model does not exist");

        RuleFor(x => x.City)
            .MaximumLength(255)
            .WithMessage("City name must not exceed 255 characters")
            .When(x => !string.IsNullOrWhiteSpace(x.City));

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

    private bool HasAtLeastOneFilter(SendNotificationByCategoryCommand command)
    {
        return command.CarTypeId.HasValue ||
               command.CarModelId.HasValue ||
               !string.IsNullOrWhiteSpace(command.City);
    }

    private async Task<bool> CheckCarTypeExists(int? carTypeId, CancellationToken cancellationToken)
    {
        if (!carTypeId.HasValue)
            return true;

        return await _applicationDbContext.CarTypes
            .AnyAsync(x => x.Id == carTypeId.Value, cancellationToken);
    }

    private async Task<bool> CheckCarModelExists(int? carModelId, CancellationToken cancellationToken)
    {
        if (!carModelId.HasValue)
            return true;

        return await _applicationDbContext.CarModels
            .AnyAsync(x => x.Id == carModelId.Value, cancellationToken);
    }

    private async Task<bool> CheckNotificationTypeExists(int notificationTypeId, CancellationToken cancellationToken)
    {
        return await _applicationDbContext.NotificationTypes
            .AnyAsync(x => x.Id == notificationTypeId , cancellationToken);
    }
}
