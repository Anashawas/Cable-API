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
    CableReceipts
}