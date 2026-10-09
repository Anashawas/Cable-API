using Application.Common.Interfaces;
using Application.Workers.Queries.GetWorkerByProvider;
using Cable.Core;
using Cable.Core.Constants;
using Cable.Core.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Workers.Commands.SetWorkerPrivileges;

/// <summary>
/// Owner chooses what the worker may see and do in the partner app. An empty list
/// leaves the worker with station details only; selecting everything stores null
/// so privileges added later apply automatically.
/// </summary>
public record SetWorkerPrivilegesCommand(int ProviderManagerId, List<string> Privileges) : IRequest<WorkerDto>;

public class SetWorkerPrivilegesCommandValidator : AbstractValidator<SetWorkerPrivilegesCommand>
{
    public SetWorkerPrivilegesCommandValidator()
    {
        RuleFor(x => x.Privileges).NotNull();
        RuleForEach(x => x.Privileges).Must(WorkerPrivileges.IsKnown)
            .WithMessage("Unknown privilege. Allowed: " + string.Join(", ", WorkerPrivileges.All));
    }
}

public class SetWorkerPrivilegesCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<SetWorkerPrivilegesCommand, WorkerDto>
{
    public async Task<WorkerDto> Handle(SetWorkerPrivilegesCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");

        var pm = await db.ProviderManagers.FirstOrDefaultAsync(x => x.Id == request.ProviderManagerId && !x.IsDeleted, cancellationToken)
                 ?? throw new NotFoundException($"Worker assignment {request.ProviderManagerId} not found");

        await WorkerOwnershipHelper.EnsureCallerOwnsProviderAsync(db, pm.ProviderType, pm.ProviderId, callerId, cancellationToken);

        pm.Privileges = request.Privileges.Count == 0 ? "" : WorkerPrivileges.Format(request.Privileges);
        await db.SaveChanges(cancellationToken);

        return (await db.ProviderManagers.AsNoTracking()
            .Where(x => x.Id == pm.Id)
            .Select(x => new WorkerDto(x.Id, x.UserId, x.User.Name, x.User.Phone, x.User.Email, x.IsActive, x.CreatedAt, x.Privileges))
            .FirstAsync(cancellationToken));
    }
}
