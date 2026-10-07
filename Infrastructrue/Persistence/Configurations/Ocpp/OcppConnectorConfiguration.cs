using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations.Ocpp;

public class OcppConnectorConfiguration : IEntityTypeConfiguration<OcppConnector>
{
    public void Configure(EntityTypeBuilder<OcppConnector> builder)
    {
        builder.ToTable("OcppConnector");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Status).IsRequired().HasMaxLength(30);
        builder.Property(e => e.ErrorCode).IsRequired().HasMaxLength(50);
        builder.Property(e => e.VendorErrorCode).HasMaxLength(50);
        builder.Property(e => e.Info).HasMaxLength(500);
        builder.Property(e => e.PowerKw).HasColumnType("decimal(8,2)");
        builder.Property(e => e.StatusUpdatedAt).HasColumnType("datetime2(3)");
        builder.Property(e => e.StatusReceivedAt).HasColumnType("datetime2(3)").IsRequired();

        builder.HasOne(e => e.ChargePoint)
            .WithMany(c => c.Connectors)
            .HasForeignKey(e => e.OcppChargePointId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_OcppConnector_OcppChargePoint");

        builder.HasOne(e => e.PlugType)
            .WithMany()
            .HasForeignKey(e => e.PlugTypeId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("FK_OcppConnector_PlugType");

        builder.HasIndex(e => new { e.OcppChargePointId, e.ConnectorId })
            .IsUnique()
            .HasDatabaseName("UX_OcppConnector_ChargePoint_Connector");
    }
}
