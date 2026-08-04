using Application.Analytics;
using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.Announcements.Commands.AnnouncementAdminExtras;

/// <summary>Admin: HARD delete a welcome message (user states cascade; analytics history remains).</summary>
public record DeleteAnnouncementCommand(int Id) : IRequest;

public class DeleteAnnouncementCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteAnnouncementCommand>
{
    public async Task Handle(DeleteAnnouncementCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var announcement = await applicationDbContext.Announcements
                               .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken)
                           ?? throw new NotFoundException($"can not find announcement with id {request.Id}");

        applicationDbContext.Announcements.Remove(announcement);
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}

/// <summary>
/// Admin: upload the welcome message's image (multipart, form key "files", ONE
/// image, client-compressed). Replaces any previous upload and sets imageUrl.
/// </summary>
public record UploadAnnouncementImageCommand(int Id, IFormFile File) : IRequest<AnnouncementImageResult>;

public record AnnouncementImageResult(string ImageUrl);

public class UploadAnnouncementImageCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    IUploadFileService uploadFileService)
    : IRequestHandler<UploadAnnouncementImageCommand, AnnouncementImageResult>
{
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private const long MaxSizeBytes = 5 * 1024 * 1024;

    public async Task<AnnouncementImageResult> Handle(UploadAnnouncementImageCommand request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var ext = Path.GetExtension(request.File.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            throw new DataValidationException("File", "Only jpg, png or webp images are allowed");
        if (request.File.Length is 0 or > MaxSizeBytes)
            throw new DataValidationException("File", "Image must be between 1 byte and 5 MB");

        var announcement = await applicationDbContext.Announcements
                               .FirstOrDefaultAsync(a => a.Id == request.Id && !a.IsDeleted, cancellationToken)
                           ?? throw new NotFoundException($"can not find announcement with id {request.Id}");

        var fileName = await uploadFileService.SaveFileAsync(request.File, UploadFileFolders.CableAnnouncements, cancellationToken);
        var url = uploadFileService.GetFilePath(UploadFileFolders.CableAnnouncements, fileName);

        announcement.ImageUrl = url;
        await applicationDbContext.SaveChanges(cancellationToken);

        return new AnnouncementImageResult(url);
    }
}

/// <summary>Per-welcome-message engagement report for the admin card strip.</summary>
public record AnnouncementStatsDto(
    int Impressions,
    int Clicks,
    int Dismisses,
    int Conversions,
    double Ctr,
    int UniqueUsers);

public record GetAnnouncementStatsRequest(int Id, DateTime? From = null, DateTime? To = null)
    : IRequest<AnnouncementStatsDto>;

public class GetAnnouncementStatsRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetAnnouncementStatsRequest, AnnouncementStatsDto>
{
    public async Task<AnnouncementStatsDto> Handle(GetAnnouncementStatsRequest request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        if (!await applicationDbContext.Announcements.AnyAsync(a => a.Id == request.Id, cancellationToken))
            throw new NotFoundException($"can not find announcement with id {request.Id}");

        // SQL DATETIME bottoms out at 1753 — DateTime.MinValue overflows it, so
        // omitted bounds must skip the predicate rather than default to extremes.
        var query = applicationDbContext.AnalyticsEvents.AsNoTracking()
            .Where(e => e.EntityType == AnalyticsCatalog.Announcement && e.EntityId == request.Id);
        if (request.From != null)
            query = query.Where(e => e.OccurredAt >= request.From);
        if (request.To != null)
            query = query.Where(e => e.OccurredAt <= request.To);

        var events = await query
            .Select(e => new { e.EventType, e.UserId, e.AnonymousId })
            .ToListAsync(cancellationToken);

        var impressions = events.Count(e => e.EventType == (int)AnalyticsEventType.BannerView);
        var clicks = events.Count(e => e.EventType == (int)AnalyticsEventType.BannerClick);
        var dismisses = events.Count(e => e.EventType == (int)AnalyticsEventType.Dismiss);
        var conversions = events.Count(e => e.EventType == (int)AnalyticsEventType.CtaConversion);

        // Unique = distinct logged-in userIds + distinct guest anonymousIds (§6.3).
        var uniqueUsers = events
            .Select(e => e.UserId != null ? "u" + e.UserId : e.AnonymousId != null ? "a" + e.AnonymousId : null)
            .Where(x => x is not null)
            .Distinct()
            .Count();

        return new AnnouncementStatsDto(
            impressions, clicks, dismisses, conversions,
            impressions > 0 ? Math.Round((double)clicks / impressions, 4) : 0,
            uniqueUsers);
    }
}
