using Application.Common.Security;
using Cable.Core;
using Cable.Core.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Subscriptions.Commands;

/// <summary>
/// Reverses a mistaken payment without deleting it: the row stays for the
/// audit trail, flagged void, and the subscription's expiry rolls back to the
/// latest remaining period. Voiding the only payment switches the thing off.
/// </summary>
public record VoidPaymentCommand(int PaymentId, string Reason) : IRequest;

public class VoidPaymentCommandValidator : AbstractValidator<VoidPaymentCommand>
{
    public VoidPaymentCommandValidator()
    {
        RuleFor(x => x.PaymentId).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public class VoidPaymentCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<VoidPaymentCommand>
{
    public async Task Handle(VoidPaymentCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var payment = await applicationDbContext.Payments
                          .Include(p => p.Subscription).ThenInclude(s => s.Payments)
                          .FirstOrDefaultAsync(p => p.Id == request.PaymentId && !p.IsDeleted, cancellationToken)
                      ?? throw new NotFoundException($"Payment with id {request.PaymentId} not found");

        if (payment.IsVoid)
            throw new DataValidationException("PaymentId", "This payment is already void");

        var now = DateTime.UtcNow;
        payment.IsVoid = true;
        payment.VoidReason = request.Reason.Trim();
        payment.VoidedAt = now;
        payment.VoidedByUserId = currentUserService.UserId;

        var sub = payment.Subscription;
        var remaining = sub.Payments.Where(p => !p.IsDeleted && !p.IsVoid && p.Id != payment.Id).ToList();

        if (remaining.Count == 0)
        {
            // Nothing paid for any more: off, expiry pinned to the start so the
            // status reads Expired rather than a phantom future date.
            sub.ExpiresAt = sub.StartDate;
            sub.IsSwitchedOff = true;
            sub.SwitchedOffAt = now;
            sub.SwitchedOffByUserId = currentUserService.UserId;
            await SubscriptionEntitySync.ApplyAsync(applicationDbContext, sub, on: false, now, cancellationToken);
        }
        else
        {
            sub.ExpiresAt = remaining.Max(p => p.PeriodEnd);
            sub.PlanMonths = remaining.OrderByDescending(p => p.PaidDate).First().PeriodEnd > now
                ? sub.PlanMonths : sub.PlanMonths;
            await SubscriptionEntitySync.ApplyAsync(applicationDbContext, sub, on: !sub.IsSwitchedOff, now, cancellationToken);
        }

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
