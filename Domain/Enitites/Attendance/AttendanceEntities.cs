using Domain.Common;

namespace Domain.Enitites;

// Worker tracker step 3 (WORKER_TRACKER §3.5–§3.8): shifts, public holidays, punch zones, punches and the
// attendance day they belong to. Times are stored UTC; the work date is the Jordan calendar date of the shift's
// start (a shift crossing midnight belongs to the day it started on).

/// <summary>A worker's weekly schedule, valid from <see cref="EffectiveFrom"/> until <see cref="EffectiveTo"/> (null = current).</summary>
public class WorkerShiftSchedule : BaseAuditableEntity
{
    public int ProviderManagerId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    /// <summary>Minutes after the shift start before an "in" counts as late (default 10).</summary>
    public int GraceMinutes { get; set; } = 10;
    /// <summary>How early before the shift an "in" is accepted (default 60).</summary>
    public int EarliestPunchMinutes { get; set; } = 60;

    public virtual ProviderManager ProviderManager { get; set; } = null!;
    public virtual ICollection<WorkerShiftDay> Days { get; set; } = new List<WorkerShiftDay>();
}

/// <summary>One weekday of a schedule. 0 = Sunday … 6 = Saturday. EndTime ≤ StartTime = the shift crosses midnight.</summary>
public class WorkerShiftDay : BaseAuditableEntity
{
    public int WorkerShiftScheduleId { get; set; }
    public int DayOfWeek { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public bool IsOff { get; set; }

    public virtual WorkerShiftSchedule Schedule { get; set; } = null!;
}

/// <summary>Jordan public holidays, set by the admin: never an absence, never deducted from leave, work on them is extra.</summary>
public class PublicHoliday : BaseAuditableEntity
{
    public DateOnly Date { get; set; }
    public string NameAr { get; set; } = null!;
    public string NameEn { get; set; } = null!;
}

/// <summary>A circle around the station inside which punches are accepted. Applies to its assigned workers only.</summary>
public class PunchZone : BaseAuditableEntity
{
    public string ProviderType { get; set; } = null!;
    public int ProviderId { get; set; }
    public string Name { get; set; } = null!;
    public double Lat { get; set; }
    public double Lng { get; set; }
    /// <summary>50–800 m.</summary>
    public int RadiusMeters { get; set; }
    public bool IsActive { get; set; } = true;

    public virtual ICollection<PunchZoneWorker> Workers { get; set; } = new List<PunchZoneWorker>();
}

public class PunchZoneWorker : BaseAuditableEntity
{
    public int PunchZoneId { get; set; }
    public int ProviderManagerId { get; set; }

    public virtual PunchZone Zone { get; set; } = null!;
    public virtual ProviderManager ProviderManager { get; set; } = null!;
}

/// <summary>One work day of one worker — the row the attendance table shows. Punches hang off it.</summary>
public class AttendanceDay : BaseAuditableEntity
{
    public int ProviderManagerId { get; set; }
    public DateOnly WorkDate { get; set; }
    /// <summary>UTC copy of the shift at the time (null = no shift that day).</summary>
    public DateTime? ShiftStartUtc { get; set; }
    public DateTime? ShiftEndUtc { get; set; }
    public DateTime? FirstInAt { get; set; }
    public DateTime? LastOutAt { get; set; }
    /// <summary>True while the last punch is an "in".</summary>
    public bool IsOpen { get; set; }
    public int MinutesWorked { get; set; }
    public int LateMinutes { get; set; }
    public int ExtraMinutes { get; set; }
    /// <summary>present | missingOut | absent | leave | holiday | off.</summary>
    public string Status { get; set; } = null!;
    /// <summary>Comma-separated: late, review, noZone, extra, manual, corrected.</summary>
    public string? Flags { get; set; }
    public DateTime? AutoClosedAt { get; set; }

    public virtual ProviderManager ProviderManager { get; set; } = null!;
    public virtual ICollection<Punch> Punches { get; set; } = new List<Punch>();
}

/// <summary>Step 4: every owner correction of a day (field, from → to, note). Never deleted.</summary>
public class AttendanceCorrection : BaseAuditableEntity
{
    public int AttendanceDayId { get; set; }
    /// <summary>in | out | status | review.</summary>
    public string Field { get; set; } = null!;
    public string? FromValue { get; set; }
    public string? ToValue { get; set; }
    public string Note { get; set; } = null!;
    public int CorrectedByUserId { get; set; }
    public DateTime CorrectedAt { get; set; }

    public virtual AttendanceDay Day { get; set; } = null!;
}

/// <summary>Step 4: things the attendance job did once (reminders, owner alerts, daily summary), keyed so they never repeat.</summary>
public class AttendanceEvent : BaseAuditableEntity
{
    public string ProviderType { get; set; } = null!;
    public int ProviderId { get; set; }
    /// <summary>null for a per-station event (the daily summary).</summary>
    public int? ProviderManagerId { get; set; }
    public DateOnly WorkDate { get; set; }
    /// <summary>reminderIn | reminderOut | ownerMissing | dailySummary.</summary>
    public string Kind { get; set; } = null!;
    public DateTime SentAt { get; set; }
}

/// <summary>One punch (or a refused attempt: Verdict rejected, no day). Time is the server's.</summary>
public class Punch : BaseAuditableEntity
{
    public int? AttendanceDayId { get; set; }
    public int ProviderManagerId { get; set; }
    public DateOnly WorkDate { get; set; }
    /// <summary>in | out.</summary>
    public string Direction { get; set; } = null!;
    public DateTime At { get; set; }
    public double? Lat { get; set; }
    public double? Lng { get; set; }
    public int? AccuracyMeters { get; set; }
    public bool IsMocked { get; set; }
    public string? DeviceId { get; set; }
    public int? PunchZoneId { get; set; }
    public int? DistanceMeters { get; set; }
    /// <summary>accepted | review | rejected.</summary>
    public string Verdict { get; set; } = null!;
    /// <summary>lowAccuracy | outsideZone | mocked | borderline | noZone — why it was refused or needs a look.</summary>
    public string? Reason { get; set; }
    /// <summary>app | manual | correction.</summary>
    public string Source { get; set; } = null!;
    public int? RecordedByUserId { get; set; }
    public string? Note { get; set; }

    public virtual AttendanceDay? Day { get; set; }
    public virtual ProviderManager ProviderManager { get; set; } = null!;
}
