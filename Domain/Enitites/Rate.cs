﻿// <auto-generated> This file has been auto generated by EF Core Power Tools. </auto-generated>
#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Domain.Common;

namespace Domain.Enitites;

public partial class Rate:BaseAuditableEntity
{
    public int ChargingPointId { get; set; }
    public int UserId { get; set; }
    
    public int ChargingPointRate { get; set; }
    public double AVGChargingPointRate { get; set; }
    public virtual ChargingPoint ChargingPoint { get; set; } = null!;
    public virtual UserAccount User { get; set; } = null!;

}