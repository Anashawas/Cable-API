using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations.Ocpp;

/// <summary>Maps the phase-0 table (Scripts/OcppConnect_Phase0.sql) as-is; Cable.Ocpp writes it with SqlClient.</summary>
public class OcppRawMessageConfiguration : IEntityTypeConfiguration<OcppRawMessage>
{
    public void Configure(EntityTypeBuilder<OcppRawMessage> builder)
    {
        builder.ToTable("OcppRawMessage");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ChargePointId).IsRequired().HasMaxLength(40);
        builder.Property(e => e.Direction).IsRequired().HasColumnType("char(3)");
        builder.Property(e => e.MessageId).HasMaxLength(64);
        builder.Property(e => e.Action).HasMaxLength(64);
        builder.Property(e => e.RemoteIp).HasMaxLength(64);
        builder.Property(e => e.CreatedAt).HasColumnType("datetime2(3)").IsRequired();

        builder.HasIndex(e => new { e.ChargePointId, e.CreatedAt })
            .HasDatabaseName("IX_OcppRawMessage_ChargePoint_CreatedAt")
            .IsDescending(false, true);
    }
}
