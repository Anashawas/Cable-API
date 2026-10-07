using FluentValidation;

namespace Application.Offers.Queries.GetBestMatchOffer;

public class GetBestMatchOfferRequestValidator : AbstractValidator<GetBestMatchOfferRequest>
{
    public GetBestMatchOfferRequestValidator()
    {
        RuleFor(x => x.City).NotEmpty();

        // Zero is legitimate — a brand-new user with no points still gets the
        // nearest offer above their balance as a target to work toward.
        RuleFor(x => x.Points)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Points cannot be negative");
    }
}
