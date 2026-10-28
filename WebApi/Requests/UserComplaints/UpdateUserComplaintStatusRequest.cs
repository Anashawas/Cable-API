using Cable.Core.Emuns;

namespace Cable.Requests.UserComplaints;

/// <param name="Reply">Optional answer to the driver (charging-session complaints push it to them).</param>
public record UpdateUserComplaintStatusRequest(ComplaintStatus Status, string? Reply = null);
