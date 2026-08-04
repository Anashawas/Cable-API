using Application.Common.Interfaces;
using Cable.Core;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Cable.Security.Encryption.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Application.Workers.Commands.CreateWorker;

public record CreateWorkerCommand(
    string ProviderType,
    int    ProviderId,
    string Name,
    string Email,
    string Phone,
    string Password
) : IRequest<CreateWorkerResult>;

public record CreateWorkerResult(int WorkerUserId, int ProviderManagerId);

public class CreateWorkerCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    IPasswordHasher passwordHasher)
    : IRequestHandler<CreateWorkerCommand, CreateWorkerResult>
{
    public async Task<CreateWorkerResult> Handle(CreateWorkerCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUserService.UserId
                       ?? throw new NotAuthorizedAccessException("User not authenticated");

        // Only the provider owner may add its worker.
        await WorkerOwnershipHelper.EnsureCallerOwnsProviderAsync(
            applicationDbContext, request.ProviderType, request.ProviderId, callerId, cancellationToken);

        // One worker per provider.
        var alreadyHasWorker = await applicationDbContext.ProviderManagers
            .AnyAsync(pm => pm.ProviderType == request.ProviderType
                         && pm.ProviderId   == request.ProviderId
                         && !pm.IsDeleted, cancellationToken);

        if (alreadyHasWorker)
            throw new DataValidationException("Worker",
                "This provider already has a worker. Remove the current one before adding a new one.");

        // Normalize + uniqueness checks for the new account.
        var normalizedPhone = PhoneNumberUtility.NormalizePhoneNumber(request.Phone)
                              ?? throw new DataValidationException("Phone", "Invalid phone number format.");

        var emailTaken = await applicationDbContext.UserAccounts
            .AnyAsync(u => !u.IsDeleted && u.Email == request.Email, cancellationToken);
        if (emailTaken)
            throw new DataValidationException("Email", "Email is already in use.");

        var phoneTaken = await applicationDbContext.UserAccounts
            .AnyAsync(u => !u.IsDeleted && u.Phone == normalizedPhone, cancellationToken);
        if (phoneTaken)
            throw new DataValidationException("Phone", "Phone number is already in use.");

        var workerRoleId = await applicationDbContext.Roles
            .Where(r => r.Name == "Worker" && !r.IsDeleted)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (workerRoleId == 0)
            throw new CableApplicationException("Worker role not configured in the system");

        // Create the worker account (provider-app login = email + password + phone OTP).
        var worker = new UserAccount
        {
            Name            = request.Name,
            Email           = request.Email,
            Phone           = normalizedPhone,
            Password        = passwordHasher.HashPassword(request.Password),
            RoleId          = workerRoleId,
            IsActive        = true,
            IsDeleted       = false,
            IsPhoneVerified = true,
            PhoneVerifiedAt = DateTime.UtcNow
        };

        applicationDbContext.UserAccounts.Add(worker);
        await applicationDbContext.SaveChanges(cancellationToken);

        var providerManager = new ProviderManager
        {
            ProviderType = request.ProviderType,
            ProviderId   = request.ProviderId,
            UserId       = worker.Id,
            IsActive     = true
        };

        applicationDbContext.ProviderManagers.Add(providerManager);
        await applicationDbContext.SaveChanges(cancellationToken);

        return new CreateWorkerResult(worker.Id, providerManager.Id);
    }
}
