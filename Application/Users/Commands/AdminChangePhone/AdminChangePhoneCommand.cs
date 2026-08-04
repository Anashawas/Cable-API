using Application.Common.Interfaces;
using Cable.Core;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Users.Commands.AdminChangePhone;

public record AdminChangePhoneCommand(
    int UserId,
    string PhoneNumber
) : IRequest<AdminChangePhoneDto>;

public record AdminChangePhoneDto(
    int UserId,
    string? OldPhoneNumber,
    string NewPhoneNumber,
    DateTime ChangedAt
);

public class AdminChangePhoneCommandHandler(
    IApplicationDbContext context,
    ICurrentUserService currentUserService
) : IRequestHandler<AdminChangePhoneCommand, AdminChangePhoneDto>
{
    public async Task<AdminChangePhoneDto> Handle(AdminChangePhoneCommand request, CancellationToken cancellationToken)
    {
        if (!currentUserService.UserId.HasValue)
            throw new NotAuthorizedAccessException();

        var user = await context.UserAccounts
            .FirstOrDefaultAsync(x => x.Id == request.UserId && !x.IsDeleted, cancellationToken)
            ?? throw new NotFoundException($"User with id {request.UserId} not found");

        var normalizedPhone = PhoneNumberUtility.NormalizePhoneNumber(request.PhoneNumber)
            ?? throw new DataValidationException("PhoneNumber", "Invalid phone number format. Please use a valid Jordan mobile number.");

        var existingUser = await context.UserAccounts
            .FirstOrDefaultAsync(x => x.Phone == normalizedPhone && x.Id != request.UserId && !x.IsDeleted, cancellationToken);

        if (existingUser != null)
            throw new DataValidationException("PhoneNumber", $"This phone number is already linked to another account (User ID: {existingUser.Id}).");

        var oldPhone = user.Phone;
        user.Phone = normalizedPhone;
        user.IsPhoneVerified = true;
        user.PhoneVerifiedAt = DateTime.UtcNow;

        await context.SaveChanges(cancellationToken);

        return new AdminChangePhoneDto(
            user.Id,
            oldPhone,
            normalizedPhone,
            DateTime.UtcNow
        );
    }
}
