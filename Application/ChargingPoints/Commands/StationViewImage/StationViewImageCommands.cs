using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Commands.StationViewImage;

public record ViewImageResult(string? ViewImage, string? ViewImageStatus);

/// <summary>
/// Partner uploads the ONE promo image for the premium home card. A new upload
/// REPLACES the old one. Partner uploads land as "pending" until an admin
/// approves; an admin's own upload is approved immediately.
/// </summary>
public record UploadStationViewImageCommand(int ChargingPointId, IFormFile File) : IRequest<ViewImageResult>;

/// <summary>Admin: approve or reject the pending view image.</summary>
public record ReviewStationViewImageCommand(int ChargingPointId, bool Approve) : IRequest<ViewImageResult>;

/// <summary>Remove the view image (owner, worker, or admin).</summary>
public record DeleteStationViewImageCommand(int ChargingPointId) : IRequest;

public static class StationViewImageGuard
{
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    public const long MaxSizeBytes = 5 * 1024 * 1024;

    public static void ValidateFile(IFormFile file)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            throw new DataValidationException("File", "Only jpg, png or webp images are allowed");
        if (file.Length is 0 or > MaxSizeBytes)
            throw new DataValidationException("File", "Image must be between 1 byte and 5 MB");
    }

    /// <summary>Owner, active worker, or admin — same access rule as the other provider features.</summary>
    public static async Task<Domain.Enitites.ChargingPoint> EnsureCanManageAsync(
        IApplicationDbContext db, ICurrentUserService currentUser, int chargingPointId, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");

        var station = await db.ChargingPoints
                          .FirstOrDefaultAsync(x => x.Id == chargingPointId && !x.IsDeleted, ct)
                      ?? throw new NotFoundException($"can not find charging point with id {chargingPointId}");

        var isWorker = await db.ProviderManagers.AsNoTracking()
            .AnyAsync(pm => pm.ProviderType == "ChargingPoint" && pm.ProviderId == chargingPointId
                            && pm.UserId == userId && pm.IsActive && !pm.IsDeleted, ct);

        if (station.OwnerId != userId && !isWorker
            && !await AdminRoleGuard.IsAdminAsync(db, currentUser, ct))
            throw new ForbiddenAccessException("Only the station owner, its worker, or an admin can manage the view image.");

        return station;
    }
}

public class UploadStationViewImageCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    IUploadFileService uploadFileService)
    : IRequestHandler<UploadStationViewImageCommand, ViewImageResult>
{
    public async Task<ViewImageResult> Handle(UploadStationViewImageCommand request, CancellationToken cancellationToken)
    {
        StationViewImageGuard.ValidateFile(request.File);
        var station = await StationViewImageGuard.EnsureCanManageAsync(
            applicationDbContext, currentUserService, request.ChargingPointId, cancellationToken);

        // ONE image per station — replace, never accumulate.
        if (!string.IsNullOrEmpty(station.ViewImage))
            uploadFileService.DeleteFiles(UploadFileFolders.CableViewImages, [station.ViewImage], cancellationToken);

        var fileName = await uploadFileService.SaveFileAsync(request.File, UploadFileFolders.CableViewImages, cancellationToken);

        var isAdmin = await AdminRoleGuard.IsAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        station.ViewImage = fileName;
        station.ViewImageStatus = isAdmin ? "approved" : "pending";

        await applicationDbContext.SaveChanges(cancellationToken);

        return new ViewImageResult(
            uploadFileService.GetFilePath(UploadFileFolders.CableViewImages, fileName),
            station.ViewImageStatus);
    }
}

public class ReviewStationViewImageCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    IUploadFileService uploadFileService)
    : IRequestHandler<ReviewStationViewImageCommand, ViewImageResult>
{
    public async Task<ViewImageResult> Handle(ReviewStationViewImageCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var station = await applicationDbContext.ChargingPoints
                          .FirstOrDefaultAsync(x => x.Id == request.ChargingPointId && !x.IsDeleted, cancellationToken)
                      ?? throw new NotFoundException($"can not find charging point with id {request.ChargingPointId}");

        if (string.IsNullOrEmpty(station.ViewImage))
            throw new DataValidationException("ViewImage", "This station has no view image to review");

        station.ViewImageStatus = request.Approve ? "approved" : "rejected";
        await applicationDbContext.SaveChanges(cancellationToken);

        return new ViewImageResult(
            uploadFileService.GetFilePath(UploadFileFolders.CableViewImages, station.ViewImage),
            station.ViewImageStatus);
    }
}

public class DeleteStationViewImageCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    IUploadFileService uploadFileService)
    : IRequestHandler<DeleteStationViewImageCommand>
{
    public async Task Handle(DeleteStationViewImageCommand request, CancellationToken cancellationToken)
    {
        var station = await StationViewImageGuard.EnsureCanManageAsync(
            applicationDbContext, currentUserService, request.ChargingPointId, cancellationToken);

        if (!string.IsNullOrEmpty(station.ViewImage))
            uploadFileService.DeleteFiles(UploadFileFolders.CableViewImages, [station.ViewImage], cancellationToken);

        station.ViewImage = null;
        station.ViewImageStatus = null;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
