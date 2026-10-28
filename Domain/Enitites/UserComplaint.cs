
using System;
using System.Collections.Generic;
using Cable.Core.Emuns;
using Domain.Common;

namespace Domain.Enitites;

public partial class UserComplaint:BaseAuditableEntity
{
    public int ChargingPointId { get; set; }

    public int UserId { get; set; }

    public string Note { get; set; } = null!;

    /// <summary>
    /// Stored as int; values come from <see cref="ComplaintStatus"/>.
    /// Default is <see cref="ComplaintStatus.New"/> (0).
    /// </summary>
    public int Status { get; set; } = (int)ComplaintStatus.New;

    /// <summary>General (the classic station complaint) | ChargingSession ("problem with this session", §13.1). See UserComplaintKind.</summary>
    public string Kind { get; set; } = "General";

    /// <summary>ChargingSession: the session it is about; the station, charger, plug, times, energy and cost come from it.</summary>
    public int? OcppTransactionId { get; set; }

    /// <summary>ChargingSession: WrongCost | StoppedEarly | NoEnergy | CableStuck | PlugBroken | Other.</summary>
    public string? Reason { get; set; }

    /// <summary>Optional photo (e.g. the charger's screen), uploaded first through the file endpoints.</summary>
    public string? ImageFileName { get; set; }

    /// <summary>The partner's / admin's answer, shown to the driver with the status.</summary>
    public string? Reply { get; set; }

    public virtual ChargingPoint ChargingPoint { get; set; } = null!;

    public virtual UserAccount User { get; set; } = null!;
}