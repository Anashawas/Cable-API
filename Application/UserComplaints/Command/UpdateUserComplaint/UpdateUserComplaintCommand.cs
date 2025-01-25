using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.UserComplaints.Command.UpdateUserComplaint;

public record UpdateUserComplaintCommand(int Id, string Note) : IRequest;


public class UpdateUserComplaintCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<UpdateUserComplaintCommand>
{
    public async Task Handle(UpdateUserComplaintCommand request, CancellationToken cancellationToken)
    {
        var userComplaint = await applicationDbContext.UserComplaints
                                .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
                            ?? throw new NotFoundException($"can not find user complaint with id {request.Id}");
        
        userComplaint.Note = request.Note;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}