﻿// <auto-generated> This file has been auto generated by EF Core Power Tools. </auto-generated>
#nullable enable
using System;
using System.Collections.Generic;
using Domain.Common;

namespace Domain.Enitites;

public partial class PlugType:BaseAuditableEntity
{
    public string? Name { get; set; }
    public string SerialNumber { get; set; } = null!;
    public virtual ICollection<ChargingPlug> ChargingPlugs { get; set; } = new List<ChargingPlug>();
}