using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// Links an OCPP idTag (an RFID card or a virtual tag) to a Cable user. The bridge between
/// the charger world and the app world: it lets us tell the driver "your car is done", award
/// loyalty points for an OCPP session automatically, and later let the driver stop or start
/// a session from the app. One tag belongs to one user; a user may hold several tags.
/// </summary>
public class OcppUserIdTag : BaseAuditableEntity
{
    public int UserId { get; set; }

    /// <summary>Normalised like OcppAuthorizedTag.IdTag (trimmed, upper-case).</summary>
    public string IdTag { get; set; } = null!;

    public string? Label { get; set; }
    public bool IsEnabled { get; set; } = true;

    public virtual UserAccount UserAccount { get; set; } = null!;
}
