using Application.Common.Security;
using Application.Subscriptions.Queries;
using Cable.Core.Constants;
using Cable.Core.Enums;
using Cable.Core.Utilities;
using FluentValidation;

namespace Application.Subscriptions.Commands;

public record RecordPaymentResult(
    int SubscriptionId,
    int PaymentId,
    string ReferenceNo,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    DateTime ExpiresAt,
    string? ReceiptDownloadPath,
    /// <summary>Set when the payment was recorded but the PDF could not be rendered.</summary>
    string? ReceiptError);

/// <summary>
/// Admin records an offline payment (CliQ / cash) for a station's premium, a
/// banner run, or a service provider's premium. The period is computed from
/// the plan — never typed — and a renewal while the current period is still
/// running extends from its expiry. A branded PDF receipt is generated and
/// stored; sending it is Phase 2.
/// </summary>
public record RecordPaymentCommand(
    string EntityType,
    int EntityId,
    int PlanMonths,
    decimal Amount,
    PaymentMethod Method,
    /// <summary>Jordan local date/time the money was received.</summary>
    DateTime PaidDate,
    PaymentRecorder.PayerInput Payer,
    string? Currency = null,
    string? Note = null,
    /// <summary>File name of an already-uploaded proof image (CableAttachments).</summary>
    string? ReceiptImageFileName = null) : IRequest<RecordPaymentResult>;

public class RecordPaymentCommandValidator : AbstractValidator<RecordPaymentCommand>
{
    public RecordPaymentCommandValidator()
    {
        RuleFor(x => x.EntityType).Must(SubscriptionEntityTypes.IsValid)
            .WithMessage($"EntityType must be one of: {string.Join(", ", SubscriptionEntityTypes.All)}");
        RuleFor(x => x.EntityId).GreaterThan(0);
        // 1 / 3 / 6 are the sold plans; the bound is wide so a custom deal does
        // not need a release, but a typo like 60 is still caught.
        RuleFor(x => x.PlanMonths).InclusiveBetween(1, 24);
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Payer).NotNull();
        RuleFor(x => x.Currency).Length(3).When(x => !string.IsNullOrEmpty(x.Currency));
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

public class RecordPaymentCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    PaymentRecorder recorder)
    : IRequestHandler<RecordPaymentCommand, RecordPaymentResult>
{
    public async Task<RecordPaymentResult> Handle(RecordPaymentCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var recorded = await recorder.RecordAsync(
            request.EntityType, request.EntityId,
            planMonths: request.PlanMonths, explicitPeriodEnd: null,
            request.Amount, request.Currency, request.Method,
            paidDateUtc: JordanTime.ToUtc(request.PaidDate),
            request.Payer, request.Note, request.ReceiptImageFileName, cancellationToken);

        var p = recorded.Payment;
        return new RecordPaymentResult(
            recorded.Subscription.Id, p.Id, p.ReferenceNo, p.PeriodStart, p.PeriodEnd,
            recorded.Subscription.ExpiresAt,
            p.GeneratedReceiptFileName == null ? null : string.Format(SubscriptionDtoMapper.ReceiptPathFormat, p.Id),
            recorded.ReceiptError);
    }
}
