using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class PointsConversionRateConfiguration : IEntityTypeConfiguration<PointsConversionRate>
{
    public void Configure(EntityTypeBuilder<PointsConversionRate> builder)
    {
        builder.ToTable("PointsConversionRate");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.CurrencyCode)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(e => e.PointsPerUnit)
            .IsRequired();

        builder.Property(e => e.IsDefault)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(e => e.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt)
            .HasColumnType("datetime");

        builder.Property(e => e.ModifiedAt)
            .HasColumnType("datetime");

        // Indexes
        builder.HasIndex(e => e.IsDefault)
            .HasDatabaseName("IX_PointsConversionRate_IsDefault");

        builder.HasIndex(e => new { e.IsActive, e.IsDeleted })
            .HasDatabaseName("IX_PointsConversionRate_IsActive_IsDeleted");
    }
}
