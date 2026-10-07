using Application.Common.Security;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.Subscriptions.Commands;

// ---------------------------------------------------------------------------
// Create / edit a payer
// ---------------------------------------------------------------------------

/// <summary>
/// Creates a payer, or edits one when <paramref name="Id"/> is set. For a
/// user-linked payer, name/phone/email are ignored — they come from the user.
/// </summary>
public record UpsertPayerCommand(
    int? Id,
    int? UserAccountId,
    string? Name,
    string? Phone,
    bool HasWhatsApp,
    string? Email,
    string? Note,
    bool OptOut = false) : IRequest<int>;

public class UpsertPayerCommandValidator : AbstractValidator<UpsertPayerCommand>
{
    public UpsertPayerCommandValidator()
    {
        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.Note).MaximumLength(500);
        RuleFor(x => x)
            .Must(x => x.Id.HasValue || x.UserAccountId.HasValue || !string.IsNullOrWhiteSpace(x.Name) || !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage("A payer needs a user, or at least a name or phone");
    }
}

public class UpsertPayerCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    PaymentRecorder recorder)
    : IRequestHandler<UpsertPayerCommand, int>
{
    public async Task<int> Handle(UpsertPayerCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        Payer payer;
        if (request.Id is int id)
        {
            payer = await applicationDbContext.Payers.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken)
                    ?? throw new NotFoundException($"Payer with id {id} not found");

            if (payer.UserAccountId == null)
            {
                if (request.Name != null) payer.Name = request.Name.Trim();
                if (request.Phone != null)
                    payer.Phone = PhoneNumberUtility.NormalizePhoneNumber(request.Phone) ?? request.Phone.Trim();
                if (request.Email != null) payer.Email = request.Email.Trim();
            }
            payer.HasWhatsApp = request.HasWhatsApp;
            if (request.Note != null) payer.Note = request.Note;
        }
        else
        {
            payer = await recorder.ResolvePayerAsync(
                new PaymentRecorder.PayerInput(null, request.UserAccountId, request.Name, request.Phone,
                    request.HasWhatsApp, request.Email, request.Note), cancellationToken);
        }

        payer.OptOut = request.OptOut;
        await applicationDbContext.SaveChanges(cancellationToken);
        return payer.Id;
    }
}

// ---------------------------------------------------------------------------
// Upload the proof image (CliQ screenshot) for a payment
// ---------------------------------------------------------------------------

/// <summary>Stores the proof in CableAttachments (same folder as other uploads) and links it.</summary>
public record UploadPaymentProofCommand(int PaymentId, IFormFile File) : IRequest<string>;

public class UploadPaymentProofCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    IUploadFileService files)
    : IRequestHandler<UploadPaymentProofCommand, string>
{
    public async Task<string> Handle(UploadPaymentProofCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var payment = await applicationDbContext.Payments
                          .FirstOrDefaultAsync(p => p.Id == request.PaymentId && !p.IsDeleted, cancellationToken)
                      ?? throw new NotFoundException($"Payment with id {request.PaymentId} not found");

        var collection = new FormFileCollection { request.File };
        if (!files.IsValidExtension(collection))
            throw new DataValidationException("File", "Unsupported file type");
        if (!files.IsValidSize(collection))
            throw new DataValidationException("File", "File is too large");

        var previous = payment.ReceiptImageFileName;
        payment.ReceiptImageFileName = await files.SaveFileAsync(request.File, UploadFileFolders.CableAttachments, cancellationToken);
        await applicationDbContext.SaveChanges(cancellationToken);

        if (!string.IsNullOrEmpty(previous))
            files.DeleteFiles(UploadFileFolders.CableAttachments, [previous], cancellationToken);

        return files.GetFilePath(UploadFileFolders.CableAttachments, payment.ReceiptImageFileName);
    }
}
