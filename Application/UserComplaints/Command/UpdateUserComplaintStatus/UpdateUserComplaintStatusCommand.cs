using Cable.Core.Emuns;
using Hangfire;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.UserComplaints.Command.UpdateUserComplaintStatus;

/// <param name="Reply">Optional answer shown to the driver (kept when null).</param>
public record UpdateUserComplaintStatusCommand(int Id, ComplaintStatus Status, string? Reply = null) : IRequest;

public class UpdateUserComplaintStatusCommandHandler(IApplicationDbContext applicationDbContext, IBackgroundJobClient jobs)
    : IRequestHandler<UpdateUserComplaintStatusCommand>
{
    public async Task Handle(UpdateUserComplaintStatusCommand request, CancellationToken cancellationToken)
    {
        var userComplaint = await applicationDbContext.UserComplaints
                                .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                            ?? throw new NotFoundException($"can not find user complaint with id {request.Id}");

        var changed = userComplaint.Status != (int)request.Status
                      || (!string.IsNullOrWhiteSpace(request.Reply) && request.Reply.Trim() != userComplaint.Reply);
        userComplaint.Status = (int)request.Status;
        if (!string.IsNullOrWhiteSpace(request.Reply)) userComplaint.Reply = request.Reply.Trim();
        await applicationDbContext.SaveChanges(cancellationToken);

        // §13.1: the driver hears about every change on a "problem with this session" complaint.
        if (changed && userComplaint.Kind == Cable.Core.Constants.UserComplaintKind.ChargingSession)
        {
            var id = userComplaint.Id;
            try { jobs.Enqueue<Application.Ocpp.LiveCharging.LiveChargingNotifications>(n => n.NotifySessionIssueStatusAsync(id, CancellationToken.None)); }
            catch { /* saved; the push is best-effort */ }
        }
    }
}
