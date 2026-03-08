using FluentValidation;

namespace Application.Favorites.Commands.AddToFavorites;

public class AddToFavoritesCommandValidator : AbstractValidator<AddToFavoritesCommand>
{
    public AddToFavoritesCommandValidator()
    {
        RuleFor(x => x.ChargingPointId)
            .GreaterThan(0)
            .WithMessage("ChargingPointId must be greater than 0");
    }
}
