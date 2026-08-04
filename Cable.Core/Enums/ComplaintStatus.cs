namespace Cable.Core.Emuns;

/// <summary>
/// Workflow statuses for a user complaint against a charging point.
/// Values 0..2 are kept stable to preserve historical data:
///   0 used to be "Pending" and is now "New" (same role: default for fresh complaints).
///   1 used to be "Rejected" and is now "NotComplaint" (closest semantic fit).
///   2 remains "Solved".
/// New statuses are appended starting at 3.
/// </summary>
public enum ComplaintStatus
{
    /// <summary>Default status for a freshly submitted complaint.</summary>
    New          = 0,

    /// <summary>Admin determined the submission is not actually a complaint.</summary>
    NotComplaint = 1,

    /// <summary>Complaint resolved.</summary>
    Solved       = 2,

    /// <summary>Admin started working on the complaint.</summary>
    Opened       = 3,

    /// <summary>Waiting on the user or station for additional information.</summary>
    FollowUp     = 4,

    /// <summary>Investigation closed without a fix possible.</summary>
    Unsolved     = 5,

    /// <summary>Root cause is in the Cable platform itself, not the station.</summary>
    SystemIssue  = 6,
}
