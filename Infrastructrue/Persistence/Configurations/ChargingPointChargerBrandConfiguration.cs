using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class ChargingPointChargerBrandConfiguration : IEntityTypeConfiguration<ChargingPointChargerBrand>
{
    public void Configure(EntityTypeBuilder<ChargingPointChargerBrand> builder)
    {
        builder.ToTable("ChargingPointChargerBrand");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Count)
            .IsRequired()
            .HasDefaultValue(1);

        builder.HasOne(d => d.ChargingPoint)
            .WithMany(p => p.ChargerBrands)
            .HasForeignKey(d => d.ChargingPointId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_ChargingPointChargerBrand_ChargingPoint");

        builder.HasOne(d => d.ChargerBrand)
            .WithMany(p => p.ChargingPointBrands)
            .HasForeignKey(d => d.ChargerBrandId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_ChargingPointChargerBrand_ChargerBrand");

        builder.HasIndex(e => new { e.ChargingPointId, e.ChargerBrandId })
            .IsUnique()
            .HasDatabaseName("UX_ChargingPointChargerBrand_Point_Brand");
    }
}
