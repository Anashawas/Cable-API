using FluentValidation;

namespace Application.Terms.Commands.PublishTermsVersion;

public class PublishTermsVersionCommandValidator : AbstractValidator<PublishTermsVersionCommand>
{
    public PublishTermsVersionCommandValidator()
    {
        RuleFor(x => x.SystemVersion).NotEmpty().MaximumLength(20);
        RuleFor(x => x.ContentEn).NotEmpty();
        RuleFor(x => x.ContentAr).NotEmpty();
    }
}
