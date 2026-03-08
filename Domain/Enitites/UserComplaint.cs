
using System;
using System.Collections.Generic;
using Domain.Common;

namespace Domain.Enitites;

public partial class UserComplaint:BaseAuditableEntity
{
    public int ChargingPointId { get; set; }

    public int UserId { get; set; }

    public string Note { get; set; } = null!;

    public int Status { get; set; }

    public virtual ChargingPoint ChargingPoint { get; set; } = null!;

    public virtual UserAccount User { get; set; } = null!;
}