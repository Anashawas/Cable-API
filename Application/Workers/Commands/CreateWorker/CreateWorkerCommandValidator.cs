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
            .EmailAddress().WithMessage("A valid email is required.")
            .MaximumLength(255)
            .When(v => !string.IsNullOrWhiteSpace(v.Email));

        RuleFor(v => v.Phone)
            .NotEmpty().WithMessage("Phone is required.");

        RuleFor(v => v.TempPassword)
            .MinimumLength(8).WithMessage("The temporary password must be at least 8 characters.")
            .MaximumLength(64)
            .When(v => !string.IsNullOrWhiteSpace(v.TempPassword));
    }
}
