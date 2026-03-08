using Cable.Core;
using Cable.Core.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Commands.CreateSeason;

public record CreateSeasonCommand(
    string Name,
    string? Description,
    DateTime StartDate,
    DateTime EndDate,
    bool ActivateImmediately
) : IRequest<int>;

public class CreateSeasonCommandValidator : AbstractValidator<CreateSeasonCommand>
{
    public CreateSeasonCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.StartDate).NotEmpty();
        RuleFor(x => x.EndDate).NotEmpty().GreaterThan(x => x.StartDate);
    }
}

public class CreateSeasonCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<CreateSeasonCommand, int>
{
    public async Task<int> Handle(CreateSeasonCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        // If activating immediately, deactivate any existing active season
        if (request.ActivateImmediately)
        {
            var activeSeason = await applicationDbContext.LoyaltySeasons
                .FirstOrDefaultAsync(s => s.IsActive && !s.IsDeleted, cancellationToken);

            if (activeSeason != null)
                throw new DataValidationException("Season", "There is already an active season. End it first before activating a new one.");
        }

        var now = DateTime.UtcNow;
        var season = new LoyaltySeason
        {
            Name = request.Name,
            Description = request.Description,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsActive = request.ActivateImmediately,
            CreatedAt = now,
            CreatedBy = userId
        };

        applicationDbContext.LoyaltySeasons.Add(season);
        await applicationDbContext.SaveChanges(cancellationToken);

        return season.Id;
    }
}
