
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

    public virtual ChargingPoint ChargingPoint { get; set; } = null!;

    public virtual UserAccount User { get; set; } = null!;
}