using FluentValidation;

namespace Application.Workers.Commands.CreateWorker;

public class CreateWorkerCommandValidator : AbstractValidator<CreateWorkerCommand>
{
    private static readonly string[] AllowedProviderTypes = { "ServiceProvider", "ChargingPoint" };

    public CreateWorkerCommandValidator()
    {
        RuleFor(v => v.ProviderType)
            .NotEmpty().WithMessage("ProviderType is required.")
            .Must(t => AllowedProviderTypes.Contains(t))
            .WithMessage("ProviderType must be 'ServiceProvider' or 'ChargingPoint'.");

        RuleFor(v => v.ProviderId)
            .GreaterThan(0).WithMessage("ProviderId must be greater than 0.");

        RuleFor(v => v.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(255);

        RuleFor(v => v.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email is required.")
            .MaximumLength(255);

        RuleFor(v => v.Phone)
            .NotEmpty().WithMessage("Phone is required.");

        RuleFor(v => v.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(6).WithMessage("Password must be at least 6 characters.");
    }
}
