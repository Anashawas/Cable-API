using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations.Ocpp;

public class OcppTransactionConfiguration : IEntityTypeConfiguration<OcppTransaction>
{
    public void Configure(EntityTypeBuilder<OcppTransaction> builder)
    {
        builder.ToTable("OcppTransaction");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.IdTag).IsRequired().HasMaxLength(50);
        builder.Property(e => e.StopReason).HasMaxLength(30);
        builder.Property(e => e.EnergyKwh).HasColumnType("decimal(12,3)");
        builder.Property(e => e.StartedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.Property(e => e.StoppedAt).HasColumnType("datetime2(3)");
        builder.Property(e => e.ReceivedStartAt).HasColumnType("datetime2(3)").IsRequired();
        builder.Property(e => e.ReceivedStopAt).HasColumnType("datetime2(3)");
        builder.Property(e => e.IsOpen).IsRequired().HasDefaultValue(true);
        builder.Property(e => e.IsStale).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.IsOrphan).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.WasRejected).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.StartSource).IsRequired().HasMaxLength(10).HasDefaultValue("Card");
        builder.Property(e => e.CostBreakdownJson);
        builder.Property(e => e.PricedAt).HasColumnType("datetime2(0)");

        builder.HasOne(e => e.ChargePoint)
            .WithMany(c => c.Transactions)
            .HasForeignKey(e => e.OcppChargePointId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_OcppTransaction_OcppChargePoint");

        // Session history per charger, newest first.
        builder.HasIndex(e => new { e.OcppChargePointId, e.StartedAt })
            .HasDatabaseName("IX_OcppTransaction_ChargePoint_StartedAt")
            .IsDescending(false, true);

        // Driver history / "my current session".
        builder.HasIndex(e => new { e.StartedByUserId, e.StartedAt })
            .HasDatabaseName("IX_OcppTransaction_StartedBy_StartedAt")
            .HasFilter("[StartedByUserId] IS NOT NULL");

        // The stale-session job and "current session" lookups.
        builder.HasIndex(e => e.IsOpen)
            .HasDatabaseName("IX_OcppTransaction_Open")
            .HasFilter("[IsOpen] = 1");
    }
}
