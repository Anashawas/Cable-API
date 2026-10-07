using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Exceptions;
using Cable.Security.Encryption.Interfaces;
using Domain.Enitites;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.Commands.RegisterOcppChargePoint;

/// <summary>
/// Admin: register one physical charger under a station so Cable.Ocpp will accept
/// its connection. Returns the values the technician types into the charger; the
/// password is shown exactly once and stored hashed.
/// </summary>
/// <param name="ChargePointId">The id the charger will use in the URL. Null = generate CBL-{station}-{nn}. Use the charger's existing id (e.g. "RH4") when it already has one.</param>
/// <param name="RequirePassword">False for units on OCPP Security Profile 0 that cannot send Basic auth.</param>
public record RegisterOcppChargePointCommand(
    int ChargingPointId,
    string? ChargePointId,
    string? DisplayName,
    bool RequirePassword = true,
    int? HeartbeatInterval = null) : IRequest<RegisterOcppChargePointResult>;

public record RegisterOcppChargePointResult(
    int Id,
    int ChargingPointId,
    string ChargePointId,
    /// <summary>Plain-text, shown once. Null when RequirePassword was false.</summary>
    string? Password,
    string Username,
    string UrlPath,
    int HeartbeatInterval,
    /// <summary>wss://host/ocpp16/ — the "Central System URL" field; the unit appends its own id. Null when OcppServer:Url is not configured.</summary>
    string? WebSocketBaseUrl = null,
    /// <summary>443 (wss) / 80 (ws) — for units with a separate port field, which would otherwise keep the previous vendor's port.</summary>
    int? Port = null);

public class RegisterOcppChargePointCommandValidator : AbstractValidator<RegisterOcppChargePointCommand>
{
    public RegisterOcppChargePointCommandValidator()
    {
        RuleFor(x => x.ChargingPointId).GreaterThan(0);
        RuleFor(x => x.ChargePointId)
            .Must(OcppCredentials.IsValidChargePointId)
            .When(x => x.ChargePointId is not null)
            .WithMessage($"ChargePointId may contain letters, digits, '-', '_' and '.', up to {OcppCredentials.MaxChargePointIdLength} characters, and must start with a letter or digit.");
        RuleFor(x => x.DisplayName).MaximumLength(100);
        RuleFor(x => x.HeartbeatInterval).InclusiveBetween(10, 3600).When(x => x.HeartbeatInterval is not null);
    }
}

public class RegisterOcppChargePointCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IPasswordHasher passwordHasher,
    IOcppCommandClient ocppServer)
    : IRequestHandler<RegisterOcppChargePointCommand, RegisterOcppChargePointResult>
{
    public async Task<RegisterOcppChargePointResult> Handle(RegisterOcppChargePointCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);

        var stationExists = await db.ChargingPoints.AsNoTracking()
            .AnyAsync(c => c.Id == request.ChargingPointId && !c.IsDeleted, cancellationToken);
        if (!stationExists)
            throw new NotFoundException($"cannot find charging point with id: {request.ChargingPointId}");

        var chargePointId = request.ChargePointId;
        if (chargePointId is null)
        {
            var existing = await db.OcppChargePoints
                .CountAsync(c => c.ChargingPointId == request.ChargingPointId, cancellationToken);
            chargePointId = OcppCredentials.GenerateChargePointId(request.ChargingPointId, existing + 1);
        }

        var taken = await db.OcppChargePoints.AsNoTracking()
            .AnyAsync(c => c.ChargePointId == chargePointId && !c.IsDeleted, cancellationToken);
        if (taken)
            throw new DataValidationException(nameof(request.ChargePointId),
                $"Charge point id '{chargePointId}' is already registered. Ids are unique across all stations.");

        var password = request.RequirePassword ? OcppCredentials.GeneratePassword() : null;

        var entity = new OcppChargePoint
        {
            ChargingPointId = request.ChargingPointId,
            ChargePointId = chargePointId,
            DisplayName = request.DisplayName,
            PasswordHash = password is null ? null : passwordHasher.HashPassword(password),
            HeartbeatInterval = request.HeartbeatInterval ?? 60,
            IsEnabled = true,
        };

        db.OcppChargePoints.Add(entity);
        await db.SaveChanges(cancellationToken);

        // Per the OCPP security whitepaper the Basic-auth username is the charge point id.
        return new RegisterOcppChargePointResult(entity.Id, entity.ChargingPointId, entity.ChargePointId,
            password, entity.ChargePointId, "/ocpp16/" + entity.ChargePointId, entity.HeartbeatInterval,
            OcppEndpoint.WebSocketBaseUrl(ocppServer.ServerUrl), OcppEndpoint.Port(ocppServer.ServerUrl));
    }
}
