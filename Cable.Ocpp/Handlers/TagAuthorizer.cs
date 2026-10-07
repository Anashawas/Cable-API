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
