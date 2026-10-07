using Application.Common.Interfaces;
using Application.Common.Security;
using Application.Ocpp.Commands.ManageOcppAuthorizedTags;
using Hangfire;
using Cable.Core;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Domain.Enitites;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.Commands.AddOcppAuthorizedTag;

/// <summary>
/// Admin or station owner/manager: allow a card / password to charge at a station.
/// Without an entry here Cable.Ocpp answers Invalid to that tag.
/// </summary>
public record AddOcppAuthorizedTagCommand(
    int ChargingPointId,
    string IdTag,
    string? Label,
    DateTime? ExpiresAt) : IRequest<int>;

public class AddOcppAuthorizedTagCommandValidator : AbstractValidator<AddOcppAuthorizedTagCommand>
{
    public AddOcppAuthorizedTagCommandValidator()
    {
        RuleFor(x => x.ChargingPointId).GreaterThan(0);
        RuleFor(x => x.IdTag).NotEmpty().MaximumLength(20)
            .WithMessage("OCPP 1.6 limits an idTag to 20 characters.");
        RuleFor(x => x.Label).MaximumLength(100);
    }
}

public class AddOcppAuthorizedTagCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IBackgroundJobClient jobs)
    : IRequestHandler<AddOcppAuthorizedTagCommand, int>
{
    public async Task<int> Handle(AddOcppAuthorizedTagCommand request, CancellationToken cancellationToken)
    {
        await ProviderAccessGuard.EnsureCanActForProviderAsync(db, currentUser,
            ProviderAccessGuard.ChargingPoint, request.ChargingPointId, cancellationToken);

        var tag = OcppCredentials.NormalizeIdTag(request.IdTag);
        var expiresAt = JordanTime.ToUtc(request.ExpiresAt);

        var existing = await db.OcppAuthorizedTags
            .FirstOrDefaultAsync(t => t.ChargingPointId == request.ChargingPointId && t.IdTag == tag, cancellationToken);

        if (existing is not null)
        {
            if (!existing.IsDeleted && existing.IsEnabled)
                throw new DataValidationException(nameof(request.IdTag), $"Tag '{tag}' is already allowed at this station.");

            // Re-enable a removed or disabled tag rather than fighting the unique index.
            existing.IsDeleted = false;
            existing.IsEnabled = true;
            existing.Label = request.Label ?? existing.Label;
            existing.ExpiresAt = expiresAt;
            await db.SaveChanges(cancellationToken);
            OcppLocalListSync.Enqueue(jobs, request.ChargingPointId);
            return existing.Id;
        }

        var entity = new OcppAuthorizedTag
        {
            ChargingPointId = request.ChargingPointId,
            IdTag = tag,
            Label = request.Label,
            IsEnabled = true,
            ExpiresAt = expiresAt,
        };
        db.OcppAuthorizedTags.Add(entity);
        await db.SaveChanges(cancellationToken);
        OcppLocalListSync.Enqueue(jobs, request.ChargingPointId);
        return entity.Id;
    }
}
