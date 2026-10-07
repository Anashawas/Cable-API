using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations.Ocpp;

/// <summary>Maps Scripts/OcppConnect_Phase2.sql (dbo.OcppAlert).</summary>
public class OcppAlertConfiguration : IEntityTypeConfiguration<OcppAlert>
{
    public void Configure(EntityTypeBuilder<OcppAlert> builder)
    {
        builder.ToTable("OcppAlert");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Type).IsRequired().HasMaxLength(30);
        builder.Property(e => e.Details).HasMaxLength(300);
        builder.Property(e => e.ConditionSince).HasColumnType("datetime2(3)").IsRequired();
        builder.Property(e => e.NotifiedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.Property(e => e.ResolvedAt).HasColumnType("datetime2(3)");
        builder.Property(e => e.Recipients).IsRequired().HasDefaultValue(0);
        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.CreatedAt).HasColumnType("datetime");
        builder.Property(e => e.ModifiedAt).HasColumnType("datetime");

        builder.HasOne(e => e.ChargePoint)
            .WithMany()
            .HasForeignKey(e => e.OcppChargePointId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_OcppAlert_OcppChargePoint");

        // The job's "is there an open alert for this target" lookup and the admin's open list.
        builder.HasIndex(e => new { e.OcppChargePointId, e.Type, e.ConnectorId, e.ResolvedAt })
            .HasDatabaseName("IX_OcppAlert_Target_Open");
    }
}
