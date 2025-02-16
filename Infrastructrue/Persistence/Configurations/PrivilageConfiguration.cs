﻿// <auto-generated> This file has been auto generated by EF Core Power Tools. </auto-generated>
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using Domain.Enitites;


#nullable disable

namespace Infrastructrue.Persistence.Configurations;

public partial class PrivilageConfiguration : IEntityTypeConfiguration<Privilege>
{
    public void Configure(EntityTypeBuilder<Privilege> entity)
    {
        entity.HasKey(e => e.Id).HasName("PK_Privalge");

        entity.ToTable("Privilage");

        entity.Property(e => e.Code).HasMaxLength(255);
        entity.Property(e => e.Description).HasMaxLength(255);

        OnConfigurePartial(entity);
    }

    partial void OnConfigurePartial(EntityTypeBuilder<Privilege> entity);
}

