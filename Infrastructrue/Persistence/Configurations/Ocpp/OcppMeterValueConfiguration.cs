using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations.Ocpp;

public class OcppMeterValueConfiguration : IEntityTypeConfiguration<OcppMeterValue>
{
    public void Configure(EntityTypeBuilder<OcppMeterValue> builder)
    {
        builder.ToTable("OcppMeterValue");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Context).HasMaxLength(30);
        builder.Property(e => e.MeasuredAt).HasColumnType("datetime2(3)").IsRequired();
        builder.Property(e => e.ReceivedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.Property(e => e.CurrentA).HasColumnType("decimal(9,2)");
        builder.Property(e => e.VoltageV).HasColumnType("decimal(9,2)");
        builder.Property(e => e.TemperatureC).HasColumnType("decimal(6,1)");

        builder.HasOne(e => e.ChargePoint)
            .WithMany()
            .HasForeignKey(e => e.OcppChargePointId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_OcppMeterValue_OcppChargePoint");

        builder.HasOne(e => e.Transaction)
            .WithMany(t => t.MeterValues)
            .HasForeignKey(e => e.OcppTransactionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_OcppMeterValue_OcppTransaction");

        // R3: a replayed sample after an outage is the same row, not a second one.
        builder.HasIndex(e => new { e.OcppChargePointId, e.OcppTransactionId, e.MeasuredAt })
            .IsUnique()
            .HasDatabaseName("UX_OcppMeterValue_ChargePoint_Transaction_MeasuredAt");

        // Session curve.
        builder.HasIndex(e => new { e.OcppTransactionId, e.MeasuredAt })
            .HasDatabaseName("IX_OcppMeterValue_Transaction_MeasuredAt");
    }
}
