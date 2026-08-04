using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class StationPremiumSubscriptionConfiguration : IEntityTypeConfiguration<StationPremiumSubscription>
{
    public void Configure(EntityTypeBuilder<StationPremiumSubscription> builder)
    {
        builder.ToTable("StationPremiumSubscription");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ChargingPointId)
            .IsRequired();

        builder.Property(e => e.PaymentDate)
            .HasColumnType("datetime")
            .IsRequired();

        builder.Property(e => e.ExpiresAt)
            .HasColumnType("datetime")
            .IsRequired();

        builder.Property(e => e.Amount)
            .HasColumnType("decimal(18,3)");

        builder.Property(e => e.Note)
            .HasMaxLength(500);

        builder.Property(e => e.CreatedAt)
            .HasColumnType("datetime");

        builder.Property(e => e.ModifiedAt)
            .HasColumnType("datetime");

        builder.HasOne(d => d.ChargingPoint)
            .WithMany()
            .HasForeignKey(d => d.ChargingPointId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_StationPremiumSubscription_ChargingPoint");

        builder.HasIndex(e => new { e.ChargingPointId, e.PaymentDate })
            .HasDatabaseName("IX_StationPremiumSubscription_ChargingPoint_PaymentDate");
    }
}
