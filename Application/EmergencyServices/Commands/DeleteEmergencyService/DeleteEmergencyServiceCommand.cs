using Application.Common.Interfaces;
using Cable.Core.Exceptions;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.EmergencyServices.Commands.DeleteEmergencyService;

public record DeleteEmergencyServiceCommand(int Id) : IRequest;

public class DeleteEmergencyServiceCommandHandler(IApplicationDbContext context)
    : IRequestHandler<DeleteEmergencyServiceCommand>
{
    public async Task Handle(DeleteEmergencyServiceCommand request, CancellationToken cancellationToken)
    {
        var emergencyService = await context.EmergencyServices
            .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(EmergencyService), request.Id);

        emergencyService.IsDeleted = true;

        await context.SaveChanges(cancellationToken);
    }
}
