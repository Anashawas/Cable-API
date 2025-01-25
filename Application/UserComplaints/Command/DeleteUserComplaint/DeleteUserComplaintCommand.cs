using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.UserComplaints.Command.DeleteUserComplaint;

public record DeleteUserComplaintCommand(int Id) : IRequest;

public class DeleteUserComplaintCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<DeleteUserComplaintCommand>
{
    public async Task Handle(DeleteUserComplaintCommand request, CancellationToken cancellationToken)
    {
        var userComplaint = await applicationDbContext.UserComplaints
                                .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
                            ?? throw new NotFoundException($"can not find user complaint with id {request.Id}");

        userComplaint.IsDeleted = true;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}

