using Application.Common.Interfaces;
using Application.Ocpp;
using Cable.Core.Constants;
using Cable.Ocpp.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cable.Ocpp.Handlers;

/// <summary>
/// The one decision that makes a read-only CSMS safe on a live station: who may
/// charge. Shared by Authorize and StartTransaction so they can never disagree.
/// </summary>
public sealed class TagAuthorizer(IApplicationDbContext db, IOptions<OcppOptions> options, ILogger<TagAuthorizer> log)
{
    public async Task<string> AuthorizeAsync(OcppSession session, string? idTag, CancellationToken cancellationToken)
    {
        switch (options.Value.AuthorizeMode)
        {
            case OcppAuthorizeMode.AcceptAll:
                return OcppAuthorizationStatus.Accepted;
            case OcppAuthorizeMode.RejectAll:
                return OcppAuthorizationStatus.Invalid;
        }

        if (string.IsNullOrWhiteSpace(idTag))
            return OcppAuthorizationStatus.Invalid;

        var tag = OcppCredentials.NormalizeIdTag(idTag);
        var entry = await db.OcppAuthorizedTags.AsNoTracking()
            .Where(t => t.ChargingPointId == session.ChargingPointId && t.IdTag == tag && !t.IsDeleted)
            .Select(t => new { t.IsEnabled, t.ExpiresAt })
            .FirstOrDefaultAsync(cancellationToken);

        // A virtual tag (driver app / operator start) is accepted only against the RemoteStartTransaction
        // we sent with it a moment ago — never from the station's card list, never without that request.
        if (entry is null && (OcppVirtualTag.UserIdOf(tag) is not null || OcppVirtualTag.IsStationTag(tag)))
        {
            var since = DateTime.UtcNow - OcppVirtualTag.RemoteStartAuthorizeWindow;
            var needle = "\"idTag\":\"" + tag + "\"";
            var requested = await db.OcppCommands.AsNoTracking()
                .AnyAsync(c => c.OcppChargePointId == session.OcppChargePointId && c.Action == "RemoteStartTransaction"
                               && !c.IsDeleted && c.CreatedAt >= since && c.ResultStatus == "Accepted"
                               && c.RequestPayload.Contains(needle), cancellationToken);
            if (requested) return OcppAuthorizationStatus.Accepted;
        }

        var status = entry switch
        {
            null => OcppAuthorizationStatus.Invalid,
            { IsEnabled: false } => OcppAuthorizationStatus.Blocked,
            { ExpiresAt: { } exp } when exp <= DateTime.UtcNow => OcppAuthorizationStatus.Expired,
            _ => OcppAuthorizationStatus.Accepted,
        };

        if (status != OcppAuthorizationStatus.Accepted)
            log.LogInformation("{ChargePointId}: tag {Tag} → {Status}", session.ChargePointId, tag, status);

        return status;
    }
}
