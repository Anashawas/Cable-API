using Application.Common.Security;
using Application.Settings;
using Cable.Core;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Subscriptions.Commands;

// ---------------------------------------------------------------------------
// Switch a subscription on / off (the manual grace control)
// ---------------------------------------------------------------------------

/// <summary>
/// The admin's kill switch. Off ends premium placement / the banner run now,
/// whatever the expiry says; on restores it from the subscription's dates.
/// Recording a new payment also turns it back on.
/// </summary>
public record SwitchSubscriptionCommand(int SubscriptionId, bool On) : IRequest;

public class SwitchSubscriptionCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<SwitchSubscriptionCommand>
{
    public async Task Handle(SwitchSubscriptionCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var sub = await applicationDbContext.Subscriptions
                      .FirstOrDefaultAsync(s => s.Id == request.SubscriptionId && !s.IsDeleted, cancellationToken)
                  ?? throw new NotFoundException($"Subscription with id {request.SubscriptionId} not found");

        var now = DateTime.UtcNow;
        if (request.On)
        {
            if (!await applicationDbContext.Payments.AnyAsync(p => p.SubscriptionId == sub.Id && !p.IsVoid && !p.IsDeleted, cancellationToken))
                throw new DataValidationException("SubscriptionId", "Nothing has been paid for — record a payment instead");
            sub.IsSwitchedOff = false;
            sub.SwitchedOffAt = null;
            sub.SwitchedOffByUserId = null;
        }
        else
        {
            sub.IsSwitchedOff = true;
            sub.SwitchedOffAt = now;
            sub.SwitchedOffByUserId = currentUserService.UserId;
        }

        await SubscriptionEntitySync.ApplyAsync(applicationDbContext, sub, on: request.On, now, cancellationToken);
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// Per-subscription grace override
// ---------------------------------------------------------------------------

/// <summary>
/// Overrides the global grace rule for one subscription. Null <paramref name="GraceMode"/>
/// clears the override so the global default applies again.
/// </summary>
public record SetSubscriptionGraceCommand(int SubscriptionId, string? GraceMode, int? GraceDays) : IRequest;

public class SetSubscriptionGraceCommandValidator : AbstractValidator<SetSubscriptionGraceCommand>
{
    public SetSubscriptionGraceCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).GreaterThan(0);
        RuleFor(x => x.GraceMode)
            .Must(m => m == null || Enum.TryParse<SubscriptionGraceMode>(m, true, out _))
            .WithMessage("GraceMode must be Manual, AfterDays, or null to inherit the default");
        RuleFor(x => x.GraceDays).InclusiveBetween(0, 365).When(x => x.GraceDays.HasValue);
    }
}

public class SetSubscriptionGraceCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<SetSubscriptionGraceCommand>
{
    public async Task Handle(SetSubscriptionGraceCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var sub = await applicationDbContext.Subscriptions
                      .FirstOrDefaultAsync(s => s.Id == request.SubscriptionId && !s.IsDeleted, cancellationToken)
                  ?? throw new NotFoundException($"Subscription with id {request.SubscriptionId} not found");

        if (request.GraceMode == null)
        {
            sub.GraceMode = null;
            sub.GraceDays = null;
        }
        else
        {
            var mode = Enum.Parse<SubscriptionGraceMode>(request.GraceMode, true);
            sub.GraceMode = (int)mode;
            sub.GraceDays = mode == SubscriptionGraceMode.AfterDays ? Math.Max(0, request.GraceDays ?? 0) : null;
        }

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// Global grace default (AppSetting-backed, like nearby-radius)
// ---------------------------------------------------------------------------

public record SubscriptionGraceSettingsDto(string GraceMode, int GraceDays, bool IsConfigured);

public record GetSubscriptionGraceSettingsRequest : IRequest<SubscriptionGraceSettingsDto>;

public class GetSubscriptionGraceSettingsRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetSubscriptionGraceSettingsRequest, SubscriptionGraceSettingsDto>
{
    public async Task<SubscriptionGraceSettingsDto> Handle(GetSubscriptionGraceSettingsRequest request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        var (mode, days) = await AppSettingsProvider.GetSubscriptionGraceAsync(applicationDbContext, cancellationToken);
        var configured = await applicationDbContext.AppSettings.AsNoTracking()
            .AnyAsync(s => s.Key == AppSettingsProvider.SubscriptionGraceModeKey && !s.IsDeleted, cancellationToken);
        return new SubscriptionGraceSettingsDto(mode.ToString(), days, configured);
    }
}

/// <summary>Sets the global rule. Manual ignores <paramref name="GraceDays"/>.</summary>
public record UpdateSubscriptionGraceSettingsCommand(string GraceMode, int GraceDays = 0) : IRequest;

public class UpdateSubscriptionGraceSettingsCommandValidator : AbstractValidator<UpdateSubscriptionGraceSettingsCommand>
{
    public UpdateSubscriptionGraceSettingsCommandValidator()
    {
        RuleFor(x => x.GraceMode)
            .Must(m => Enum.TryParse<SubscriptionGraceMode>(m, true, out _))
            .WithMessage("GraceMode must be Manual or AfterDays");
        RuleFor(x => x.GraceDays).InclusiveBetween(0, 365);
    }
}

public class UpdateSubscriptionGraceSettingsCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateSubscriptionGraceSettingsCommand>
{
    public async Task Handle(UpdateSubscriptionGraceSettingsCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var mode = Enum.Parse<SubscriptionGraceMode>(request.GraceMode, true);
        await Upsert(AppSettingsProvider.SubscriptionGraceModeKey, mode.ToString(), cancellationToken);
        await Upsert(AppSettingsProvider.SubscriptionGraceDaysKey,
            (mode == SubscriptionGraceMode.AfterDays ? request.GraceDays : 0).ToString(), cancellationToken);
        await applicationDbContext.SaveChanges(cancellationToken);
    }

    private async Task Upsert(string key, string value, CancellationToken ct)
    {
        var row = await applicationDbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == key && !s.IsDeleted, ct);
        if (row == null) applicationDbContext.AppSettings.Add(new AppSetting { Key = key, Value = value });
        else row.Value = value;
    }
}
