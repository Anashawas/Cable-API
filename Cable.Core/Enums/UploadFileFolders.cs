namespace Cable.Core.Emuns;

public enum UploadFileFolders
{
    CableAttachments =1,
    CableBanners,
    CableChargingPoint,
    CableEmergencyService,
    CableServiceProvider,
    CableOfferAttachments,
    CableSocialMediaIcons,
    CableCarTypes,
    CableViewImages,
    CableAnnouncements,
    /// <summary>Generated payment receipts. Deliberately absent from AllowedUploadFiles: served only through the authorized receipt endpoint.</summary>
    CableReceipts,
    /// <summary>Cable gift images (Sprint 5). Public, like offer images.</summary>
    CableGifts,
    /// <summary>Photos on "problem with this session" issues (Sprint 5). Private: absent from AllowedUploadFiles, served only by GET /api/usercomplaints/{id}/photo.</summary>
    CableIssuePhotos
}