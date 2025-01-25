using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.UserComplaints.Command.AddUserComplaint;

public record AddUserComplaintCommand(int ChargingPointId,string Note) : IRequest<int>;


public class AddUserComplaintCommandHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<AddUserComplaintCommand, int>
{
    public async Task<int> Handle(AddUserComplaintCommand request, CancellationToken cancellationToken)
    {
        
        var user = await applicationDbContext.UserComplaints.FirstOrDefaultAsync(x=>x.UserId ==currentUserService.UserId,cancellationToken)
            ?? throw new NotFoundException($"can not find user with id {currentUserService.UserId}");
        
        var userComplaint = new UserComplaint
        {
            UserId = user.UserId,
            Note = request.Note,
            ChargingPointId = request.ChargingPointId
        };

         applicationDbContext.UserComplaints.Add(userComplaint);
        await applicationDbContext.SaveChanges(cancellationToken);

        return userComplaint.Id;
    }
}