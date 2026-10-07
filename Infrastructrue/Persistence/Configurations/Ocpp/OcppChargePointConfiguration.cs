using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations.Ocpp;

public class OcppChargePointConfiguration : IEntityTypeConfiguration<OcppChargePoint>
{
    public void Configure(EntityTypeBuilder<OcppChargePoint> builder)
    {
        builder.ToTable("OcppChargePoint");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ChargePointId).IsRequired().HasMaxLength(40);
        builder.Property(e => e.PasswordHash).HasMaxLength(500);
        builder.Property(e => e.DisplayName).HasMaxLength(100);
        builder.Property(e => e.Vendor).HasMaxLength(100);
        builder.Property(e => e.Model).HasMaxLength(100);
        builder.Property(e => e.FirmwareVersion).HasMaxLength(100);
        builder.Property(e => e.SerialNumber).HasMaxLength(100);
        builder.Property(e => e.ChargeBoxSerialNumber).HasMaxLength(100);
        builder.Property(e => e.Iccid).HasMaxLength(40);
        builder.Property(e => e.Imsi).HasMaxLength(40);
        builder.Property(e => e.MeterSerialNumber).HasMaxLength(100);
        builder.Property(e => e.LastRemoteIp).HasMaxLength(64);

        builder.Property(e => e.HeartbeatInterval).IsRequired().HasDefaultValue(60);
        builder.Property(e => e.IsEnabled).IsRequired().HasDefaultValue(true);
        builder.Property(e => e.IsConnected).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.FailedAuthCount).IsRequired().HasDefaultValue(0);
        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.Property(e => e.ConnectedAt).HasColumnType("datetime2(3)");
        builder.Property(e => e.DisconnectedAt).HasColumnType("datetime2(3)");
        builder.Property(e => e.LastBootAt).HasColumnType("datetime2(3)");
        builder.Property(e => e.LastMessageAt).HasColumnType("datetime2(3)");
        builder.Property(e => e.LockedUntil).HasColumnType("datetime2(0)");
        builder.Property(e => e.LocalListSyncedAt).HasColumnType("datetime2(3)");
        builder.Property(e => e.LocalListStatus).HasMaxLength(20);
        builder.Property(e => e.CreatedAt).HasColumnType("datetime");
        builder.Property(e => e.ModifiedAt).HasColumnType("datetime");

        builder.HasOne(e => e.ChargingPoint)
            .WithMany()
            .HasForeignKey(e => e.ChargingPointId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_OcppChargePoint_ChargingPoint");

        // The handshake lookup: one live charger per id.
        builder.HasIndex(e => e.ChargePointId)
            .IsUnique()
            .HasDatabaseName("UX_OcppChargePoint_ChargePointId")
            .HasFilter("[IsDeleted] = 0");

        builder.HasIndex(e => e.ChargingPointId)
            .HasDatabaseName("IX_OcppChargePoint_ChargingPoint");
    }
}
