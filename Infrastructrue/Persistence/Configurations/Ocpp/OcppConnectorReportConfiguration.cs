using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations.Ocpp;

/// <summary>Maps Scripts/Sprint5_LiveCharging_B.sql (dbo.OcppConnectorReport).</summary>
public class OcppConnectorReportConfiguration : IEntityTypeConfiguration<OcppConnectorReport>
{
    public void Configure(EntityTypeBuilder<OcppConnectorReport> builder)
    {
        builder.ToTable("OcppConnectorReport");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Reason).IsRequired().HasMaxLength(20);
        builder.Property(e => e.Note).HasMaxLength(500);
        builder.Property(e => e.CommandStatus).HasMaxLength(20);
        builder.Property(e => e.CommandResultStatus).HasMaxLength(40);
        builder.Property(e => e.CommandErrorCode).HasMaxLength(64);
        builder.Property(e => e.ConnectorStatus).HasMaxLength(30);
        builder.Property(e => e.ConnectorErrorCode).HasMaxLength(50);
        builder.Property(e => e.ConnectorStatusAt).HasColumnType("datetime2(3)");
        builder.Property(e => e.ChargerOnline).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.ResolvedAt).HasColumnType("datetime2(0)");
        builder.Property(e => e.ResolutionNote).HasMaxLength(500);
        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.CreatedAt).HasColumnType("datetime");
        builder.Property(e => e.ModifiedAt).HasColumnType("datetime");

        builder.HasOne(e => e.ChargePoint)
            .WithMany()
            .HasForeignKey(e => e.OcppChargePointId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_OcppConnectorReport_OcppChargePoint");

        // /live (open reports per plug) and the partner's open list.
        builder.HasIndex(e => new { e.OcppChargePointId, e.ConnectorId, e.ResolvedAt })
            .HasDatabaseName("IX_OcppConnectorReport_Plug_Open");
        builder.HasIndex(e => new { e.ChargingPointId, e.CreatedAt })
            .HasDatabaseName("IX_OcppConnectorReport_Station_CreatedAt");
    }
}
