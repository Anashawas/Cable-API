using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Constants;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.LiveCharging;

// ---------------------------------------------------------------------------
// §13.1 — the photo on a "problem with this session" issue. The driver creates the issue first and then attaches
// one photo to it, so a photo can only ever belong to the driver's own issue. Files live in a private folder
// (UploadFileFolders.CableIssuePhotos, deliberately absent from AllowedUploadFiles) and are served only here:
// to the driver who reported it, the station's owner / managers with ViewFeedback, and admins.
// ---------------------------------------------------------------------------

public record SessionIssuePhotoDto(int IssueId, string ImageFileName);

/// <summary>POST /api/users/me/ocpp-sessions/{id}/issues/{issueId}/photo — multipart, form key "file", one jpg / png / webp up to 5 MB. Replaces an earlier photo.</summary>
public record UploadSessionIssuePhotoCommand(int TransactionId, int IssueId, IFormFile? File) : IRequest<SessionIssuePhotoDto>;

public class UploadSessionIssuePhotoCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IUploadFileService files)
    : IRequestHandler<UploadSessionIssuePhotoCommand, SessionIssuePhotoDto>
{
    public async Task<SessionIssuePhotoDto> Handle(UploadSessionIssuePhotoCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var issue = await db.UserComplaints
                        .FirstOrDefaultAsync(c => c.Id == request.IssueId && c.OcppTransactionId == request.TransactionId
                                                  && c.UserId == userId && !c.IsDeleted, cancellationToken)
                    ?? throw new NotFoundException(Lang.Pick("ما لقينا هالبلاغ", "Issue not found"));

        SessionIssuePhotoRules.Validate(request.File);
        var fileName = await files.SaveFileAsync(request.File!, UploadFileFolders.CableIssuePhotos, cancellationToken);
        issue.ImageFileName = fileName;
        await db.SaveChanges(cancellationToken);
        return new SessionIssuePhotoDto(issue.Id, fileName);
    }
}

/// <summary>GET /api/usercomplaints/{id}/photo — the issue's photo for its driver, the station's owner / managers (ViewFeedback) or an admin.</summary>
public record GetComplaintPhotoRequest(int ComplaintId) : IRequest<LiveChargingFile>;

public class GetComplaintPhotoRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser, IUploadFileService files)
    : IRequestHandler<GetComplaintPhotoRequest, LiveChargingFile>
{
    public async Task<LiveChargingFile> Handle(GetComplaintPhotoRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var complaint = await db.UserComplaints.AsNoTracking()
                            .Where(c => c.Id == request.ComplaintId && !c.IsDeleted)
                            .Select(c => new { c.UserId, c.ChargingPointId, c.ImageFileName })
                            .FirstOrDefaultAsync(cancellationToken)
                        ?? throw new NotFoundException(Lang.Pick("ما لقينا هالشكوى", "Complaint not found"));

        if (complaint.UserId != userId && !await AdminRoleGuard.IsAdminAsync(db, currentUser, cancellationToken))
            await ProviderAccessGuard.EnsureCanActForProviderAsync(db, currentUser, ProviderAccessGuard.ChargingPoint, complaint.ChargingPointId,
                cancellationToken, requiredPrivilege: WorkerPrivileges.ViewFeedback);

        if (string.IsNullOrWhiteSpace(complaint.ImageFileName))
            throw new NotFoundException(Lang.Pick("ما في صورة على هالشكوى", "This complaint has no photo"));

        // Only a bare file name from our own folder: never a path the client could steer elsewhere.
        var name = Path.GetFileName(complaint.ImageFileName);
        byte[] content;
        try { content = await files.GetFileAsync(UploadFileFolders.CableIssuePhotos, name, cancellationToken); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        { throw new NotFoundException(Lang.Pick("الصورة مش موجودة", "The photo file is missing")); }

        return new LiveChargingFile(content, SessionIssuePhotoRules.ContentType(name), name);
    }
}

public static class SessionIssuePhotoRules
{
    public static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    public const long MaxSizeBytes = 5 * 1024 * 1024;

    public static void Validate(IFormFile? file)
    {
        if (file is null)
            throw new DataValidationException("File", Lang.Pick("أرفق الصورة بالحقل 'file'", "Attach the photo as form field 'file'"));
        if (!AllowedExtensions.Contains(Path.GetExtension(file.FileName).ToLowerInvariant()))
            throw new DataValidationException("File", Lang.Pick("الصورة لازم تكون jpg أو png أو webp", "Only jpg, png or webp images are allowed"));
        if (file.Length is 0 or > MaxSizeBytes)
            throw new DataValidationException("File", Lang.Pick("حجم الصورة لازم يكون أقل من 5 ميغا", "The image must be smaller than 5 MB"));
    }

    public static string ContentType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg",
    };
}
