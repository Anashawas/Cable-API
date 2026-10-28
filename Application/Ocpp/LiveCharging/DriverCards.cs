using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Exceptions;
using Domain.Enitites;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.LiveCharging;

// ---------------------------------------------------------------------------
// 4.9 — a driver links their RFID card to their account (OcppUserIdTag), so card
// sessions also show in their history, get the driver pushes and earn points.
// The link takes effect from the next StartTransaction with that card: past
// sessions are not re-credited. Cable's own virtual tags are never listed here.
// ---------------------------------------------------------------------------

public record OcppCardDto(int Id, string IdTag, string? Label, bool IsEnabled, DateTime LinkedAt,
    /// <summary>Admin only: true for the app's own virtual tag (CBL-U{userId}), which is not a card.</summary>
    bool IsAppTag = false);

internal static class OcppCardQueries
{
    public static IQueryable<OcppUserIdTag> OfUser(IApplicationDbContext db, int userId) =>
        db.OcppUserIdTags.Where(u => u.UserId == userId && !u.IsDeleted);

    public static OcppCardDto ToDto(OcppUserIdTag u) =>
        new(u.Id, u.IdTag, u.Label, u.IsEnabled, u.CreatedAt, DriverCardRules.IsAppTag(u.IdTag));
}

// ---- Driver ------------------------------------------------------------------

public record GetMyOcppCardsRequest : IRequest<List<OcppCardDto>>;

public class GetMyOcppCardsRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetMyOcppCardsRequest, List<OcppCardDto>>
{
    public async Task<List<OcppCardDto>> Handle(GetMyOcppCardsRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var rows = await OcppCardQueries.OfUser(db, userId).AsNoTracking().OrderBy(u => u.Id).ToListAsync(cancellationToken);
        return rows.Where(r => !DriverCardRules.IsAppTag(r.IdTag)).Select(OcppCardQueries.ToDto).ToList();
    }
}

public record LinkMyOcppCardCommand(string IdTag, string? Label) : IRequest<OcppCardDto>;

public class LinkMyOcppCardCommandValidator : AbstractValidator<LinkMyOcppCardCommand>
{
    public LinkMyOcppCardCommandValidator()
    {
        RuleFor(x => x.IdTag).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Label).MaximumLength(100);
    }
}

public class LinkMyOcppCardCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<LinkMyOcppCardCommand, OcppCardDto>
{
    public async Task<OcppCardDto> Handle(LinkMyOcppCardCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");

        var tag = DriverCardRules.Normalize(request.IdTag);
        if (!DriverCardRules.IsValidFormat(tag)) throw new DataValidationException("IdTag", LiveChargingTexts.CardInvalid());
        if (DriverCardRules.IsReserved(tag)) throw new DataValidationException("IdTag", LiveChargingTexts.CardReserved());
        var label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim();

        var existing = await db.OcppUserIdTags.FirstOrDefaultAsync(u => u.IdTag == tag && !u.IsDeleted, cancellationToken);
        if (existing is not null)
        {
            if (existing.UserId != userId)
                throw new ConflictException(DriverCardRules.AlreadyLinkedCode, LiveChargingTexts.CardAlreadyLinked());
            // Linking the same card again: idempotent, the label is updated.
            if (label is not null && label != existing.Label)
            {
                existing.Label = label;
                await db.SaveChanges(cancellationToken);
            }
            return OcppCardQueries.ToDto(existing);
        }

        var mine = await OcppCardQueries.OfUser(db, userId).AsNoTracking().Select(u => u.IdTag).ToListAsync(cancellationToken);
        if (mine.Count(t => !DriverCardRules.IsAppTag(t)) >= DriverCardRules.MaxCardsPerUser)
            throw new UnprocessableException(DriverCardRules.LimitReachedCode, LiveChargingTexts.CardLimitReached(DriverCardRules.MaxCardsPerUser),
                new Dictionary<string, object?> { ["maxCards"] = DriverCardRules.MaxCardsPerUser });

        var row = new OcppUserIdTag { UserId = userId, IdTag = tag, Label = label, IsEnabled = true };
        db.OcppUserIdTags.Add(row);
        try
        {
            await db.SaveChanges(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost a race on the unique tag index: someone linked it a moment ago.
            throw new ConflictException(DriverCardRules.AlreadyLinkedCode, LiveChargingTexts.CardAlreadyLinked());
        }
        return OcppCardQueries.ToDto(row);
    }
}

public record UnlinkMyOcppCardCommand(int Id) : IRequest;

public class UnlinkMyOcppCardCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<UnlinkMyOcppCardCommand>
{
    public async Task Handle(UnlinkMyOcppCardCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var row = await OcppCardQueries.OfUser(db, userId).FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken);
        if (row is null || DriverCardRules.IsAppTag(row.IdTag)) throw new NotFoundException(LiveChargingTexts.CardNotFound());
        row.IsDeleted = true;   // soft delete: history keeps the sessions already credited; the tag can be linked again
        await db.SaveChanges(cancellationToken);
    }
}

// ---- Admin -------------------------------------------------------------------

public record GetUserOcppCardsRequest(int UserId) : IRequest<List<OcppCardDto>>;

public class GetUserOcppCardsRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetUserOcppCardsRequest, List<OcppCardDto>>
{
    public async Task<List<OcppCardDto>> Handle(GetUserOcppCardsRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var rows = await OcppCardQueries.OfUser(db, request.UserId).AsNoTracking().OrderBy(u => u.Id).ToListAsync(cancellationToken);
        return rows.Select(OcppCardQueries.ToDto).ToList();
    }
}

public record UnlinkUserOcppCardCommand(int UserId, int Id) : IRequest;

public class UnlinkUserOcppCardCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<UnlinkUserOcppCardCommand>
{
    public async Task Handle(UnlinkUserOcppCardCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var row = await OcppCardQueries.OfUser(db, request.UserId).FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken)
                  ?? throw new NotFoundException("cannot find card " + request.Id + " of user " + request.UserId);
        // The app's own tag is switched off rather than removed (EnsureUserTagAsync would just recreate it):
        // that blocks app charging for the account, which is what an admin removing it means.
        if (DriverCardRules.IsAppTag(row.IdTag)) row.IsEnabled = false;
        else row.IsDeleted = true;
        await db.SaveChanges(cancellationToken);
    }
}
