using System;
using System.Collections.Generic;
using Domain.Common;

namespace Domain.Enitites;

public partial class ChargingPoint : BaseAuditableEntity
{
    public string Name { get; set; } = null!;
    public int OwnerId { get; set; }
    public string? Note { get; set; }
    public string? CountryName { get; set; }
    public string? CityName { get; set; }
    public string? Phone { get; set; }
    public string? MethodPayment { get; set; }
    public double? Price { get; set; }
    public string? FromTime { get; set; }
    public string? ToTime { get; set; }

    public int? ChargerSpeed { get; set; }

    public int? ChargersCount { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public int VisitorsCount { get; set; }
    public int ChargerPointTypeId { get; set; }
    public int StatusId { get; set; }

    public virtual ICollection<ChargingPointAttachment> ChargingPointAttachments { get; set; } =
        new List<ChargingPointAttachment>();

    public virtual ChargingPointType ChargerPointType { get; set; } = null!;

    public virtual ICollection<ChargingPlug> ChargingPlugs { get; set; } = new List<ChargingPlug>();

    public virtual UserAccount Owner { get; set; } = null!;

    public virtual ICollection<Rate> Rates { get; set; } = new List<Rate>();

    public virtual Status Status { get; set; } = null!;

    public virtual ICollection<UserComplaint> UserComplaints { get; set; } = new List<UserComplaint>();
}