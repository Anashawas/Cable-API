using Application.Common.Security;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Subscriptions.Commands;

/// <summary>
/// Corrects the descriptive fields of a payment — amount, method, note, payer,
/// proof image. Dates are deliberately NOT editable: they define the period the
/// money bought, so a wrong date is a void + re-record, not an edit. The PDF is
/// regenerated so the receipt always matches the row.
/// </summary>
public record UpdatePaymentCommand(
    int PaymentId,
    decimal? Amount = null,
    PaymentMethod? Method = null,
    string? Note = null,
    string? ReceiptImageFileName = null,
    PaymentRecorder.PayerInput? Payer = null) : IRequest;

public class UpdatePaymentCommandValidator : AbstractValidator<UpdatePaymentCommand>
{
    public UpdatePaymentCommandValidator()
    {
        RuleFor(x => x.PaymentId).GreaterThan(0);
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0).When(x => x.Amount.HasValue);
        RuleFor(x => x.Method).IsInEnum().When(x => x.Method.HasValue);
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

public class UpdatePaymentCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    PaymentRecorder recorder,
    IUploadFileService files)
    : IRequestHandler<UpdatePaymentCommand>
{
    public async Task Handle(UpdatePaymentCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var payment = await applicationDbContext.Payments
                          .Include(p => p.Subscription)
                          .Include(p => p.Payer)
                          .FirstOrDefaultAsync(p => p.Id == request.PaymentId && !p.IsDeleted, cancellationToken)
                      ?? throw new NotFoundException($"Payment with id {request.PaymentId} not found");

        if (payment.IsVoid)
            throw new DataValidationException("PaymentId", "A void payment cannot be edited");

        if (request.Amount.HasValue) payment.Amount = request.Amount.Value;
        if (request.Method.HasValue) payment.Method = (int)request.Method.Value;
        if (request.Note != null) payment.Note = request.Note;
        if (request.ReceiptImageFileName != null) payment.ReceiptImageFileName = request.ReceiptImageFileName;
        if (request.Payer != null) payment.Payer = await recorder.ResolvePayerAsync(request.Payer, cancellationToken);

        var entityName = await recorder.ResolveEntityNameAsync(payment.Subscription.EntityType,
                             payment.Subscription.EntityId, cancellationToken) ?? $"#{payment.Subscription.EntityId}";

        var previous = payment.GeneratedReceiptFileName;
        var error = await recorder.GenerateReceiptAsync(payment.Subscription, payment, payment.Payer, entityName, cancellationToken);
        if (error == null && !string.IsNullOrEmpty(previous) && previous != payment.GeneratedReceiptFileName)
            files.DeleteFiles(UploadFileFolders.CableReceipts, [previous], cancellationToken);

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
