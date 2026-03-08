using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.UserComplaints.Command.UpdateUserComplaintStatus;

public record UpdateUserComplaintStatusCommand(int Id, ComplaintStatus Status) : IRequest;

public class UpdateUserComplaintStatusCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<UpdateUserComplaintStatusCommand>
{
    public async Task Handle(UpdateUserComplaintStatusCommand request, CancellationToken cancellationToken)
    {
        var userComplaint = await applicationDbContext.UserComplaints
                                .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                            ?? throw new NotFoundException($"can not find user complaint with id {request.Id}");

        userComplaint.Status = (int)request.Status;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
