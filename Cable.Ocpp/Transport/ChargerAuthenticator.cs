using System.Net.Http.Headers;
using System.Text;
using Application.Common.Interfaces;
using Cable.Security.Encryption.Interfaces;
using Cable.Security.Encryption.Models;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Cable.Ocpp.Transport;

public sealed record ChargerAuthResult(int StatusCode, string Reason, OcppChargePoint? ChargePoint)
{
    public bool Ok => StatusCode == StatusCodes.Status200OK;
}

/// <summary>
/// Decides at the handshake whether this socket belongs to a registered charger.
/// HTTP Basic per the OCPP 1.6 security whitepaper (username = charge point id);
/// a charger with no PasswordHash (Security Profile 0, e.g. the pilot unit) is
/// admitted by id alone. Ten bad passwords lock the id for 15 minutes.
/// </summary>
public sealed class ChargerAuthenticator(IApplicationDbContext db, IPasswordHasher hasher, ILogger<ChargerAuthenticator> log)
{
    private const int MaxFailedAttempts = 10;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<ChargerAuthResult> AuthenticateAsync(string chargePointId, string? authorizationHeader, CancellationToken cancellationToken)
    {
        var cp = await db.OcppChargePoints
            .FirstOrDefaultAsync(c => c.ChargePointId == chargePointId && !c.IsDeleted, cancellationToken);

        if (cp is null)
            return new ChargerAuthResult(StatusCodes.Status404NotFound, "unknown charge point id", null);
        if (!cp.IsEnabled)
            return new ChargerAuthResult(StatusCodes.Status403Forbidden, "charge point is disabled", null);
        if (cp.LockedUntil is { } until && until > DateTime.UtcNow)
            return new ChargerAuthResult(StatusCodes.Status429TooManyRequests, $"locked until {until:u}", null);

        if (cp.PasswordHash is null)
            return new ChargerAuthResult(StatusCodes.Status200OK, "no credentials required", cp);

        var credentials = ParseBasic(authorizationHeader);
        var valid = credentials is { } c
                    && string.Equals(c.User, chargePointId, StringComparison.OrdinalIgnoreCase)
                    && hasher.VerifyHashedPassword(c.Password, cp.PasswordHash) != PasswordVerificationResult.Failed;

        if (!valid)
        {
            cp.FailedAuthCount++;
            if (cp.FailedAuthCount >= MaxFailedAttempts)
            {
                cp.LockedUntil = DateTime.UtcNow + LockoutDuration;
                cp.FailedAuthCount = 0;
                log.LogWarning("{ChargePointId}: locked for {Minutes} min after repeated bad credentials", chargePointId, LockoutDuration.TotalMinutes);
            }
            await db.SaveChanges(cancellationToken);
            return new ChargerAuthResult(StatusCodes.Status401Unauthorized,
                credentials is null ? "Basic authorization required" : "invalid credentials", null);
        }

        if (cp.FailedAuthCount > 0 || cp.LockedUntil is not null)
        {
            cp.FailedAuthCount = 0;
            cp.LockedUntil = null;
            await db.SaveChanges(cancellationToken);
        }

        return new ChargerAuthResult(StatusCodes.Status200OK, "ok", cp);
    }

    private static (string User, string Password)? ParseBasic(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)
            || !AuthenticationHeaderValue.TryParse(header, out var value)
            || !string.Equals(value.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(value.Parameter))
            return null;

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value.Parameter));
        }
        catch (FormatException)
        {
            return null;
        }

        var colon = decoded.IndexOf(':');
        return colon <= 0 ? null : (decoded[..colon], decoded[(colon + 1)..]);
    }
}
