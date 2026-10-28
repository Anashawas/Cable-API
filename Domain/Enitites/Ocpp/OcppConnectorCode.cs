using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// The code printed on a plug's sticker (§10): <c>CHG-</c> + 6 characters, also encoded in the sticker's QR as
/// <c>https://cable-app.com/c/CHG-XXXXXX</c>. One active code per plug; "revoke and renew" keeps the old row
/// (RevokedAt set) so a scan of a stolen / leaked sticker answers "revoked" instead of "unknown".
/// </summary>
public class OcppConnectorCode : BaseAuditableEntity
{
    public int OcppConnectorId { get; set; }

    /// <summary>Upper case, with the prefix: CHG-7K2M9Q. Unique across all rows, revoked ones included (never reissued).</summary>
    public string Code { get; set; } = null!;

    /// <summary>Null = active. Set by the admin's "revoke and renew".</summary>
    public DateTime? RevokedAt { get; set; }
    public int? RevokedBy { get; set; }

    public virtual OcppConnector Connector { get; set; } = null!;
}
