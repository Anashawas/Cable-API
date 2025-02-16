﻿// <auto-generated> This file has been auto generated by EF Core Power Tools. </auto-generated>
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using Domain.Enitites;


#nullable disable

namespace Infrastructrue.Persistence.Configurations;

public partial class ChargingPointConfiguration : IEntityTypeConfiguration<ChargingPoint>
{
    public void Configure(EntityTypeBuilder<ChargingPoint> entity)
    {
        entity.ToTable("ChargingPoint");

        entity.Property(e => e.CityName).HasMaxLength(500);
        entity.Property(e => e.CountryName).HasMaxLength(500);
        entity.Property(e => e.CreatedAt).HasColumnType("datetime");
        entity.Property(e => e.FromTime).HasMaxLength(8);
        entity.Property(e => e.MethodPayment).HasMaxLength(20);
        entity.Property(e => e.Name).HasMaxLength(255);
        entity.Property(e => e.Note).HasMaxLength(500);
        entity.Property(e => e.Phone).HasMaxLength(20);
        entity.Property(e => e.Price).HasColumnName("price");
        entity.Property(e => e.ToTime).HasMaxLength(8);

        entity.HasOne(d => d.ChargerPointType).WithMany(p => p.ChargingPoints)
            .HasForeignKey(d => d.ChargerPointTypeId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_ChargingPoint_ChargingPointType");

        entity.HasOne(d => d.Owner).WithMany(p => p.ChargingPoints)
            .HasForeignKey(d => d.OwnerId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_ChargingPoint_UserAccount");

        entity.HasOne(d => d.Status).WithMany(p => p.ChargingPoints)
            .HasForeignKey(d => d.StatusId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_ChargingPoint_Status");

        OnConfigurePartial(entity);
    }

    partial void OnConfigurePartial(EntityTypeBuilder<ChargingPoint> entity);
}