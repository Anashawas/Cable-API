﻿// <auto-generated> This file has been auto generated by EF Core Power Tools. </auto-generated>
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using Domain.Enitites;


#nullable disable

namespace Infrastructrue.Persistence.Configurations;

public partial class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> entity)
    {
        entity.ToTable("Role");

        entity.Property(e => e.CreatedAt).HasColumnType("datetime");
        entity.Property(e => e.ModifiedAt).HasColumnType("datetime");
        entity.Property(e => e.Name).HasMaxLength(50);

        OnConfigurePartial(entity);
    }

    partial void OnConfigurePartial(EntityTypeBuilder<Role> entity);
}