using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class ServiceCategoryConfiguration : IEntityTypeConfiguration<ServiceCategory>
{
    public void Configure(EntityTypeBuilder<ServiceCategory> builder)
    {
        builder.ToTable("ServiceCategory");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.NameAr)
            .HasMaxLength(255);

        builder.Property(e => e.Description)
            .HasMaxLength(500);

        builder.Property(e => e.IconUrl)
            .HasMaxLength(500);

        builder.Property(e => e.SortOrder)
            .IsRequired()
            .HasDefaultValue(0);

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
        builder.HasIndex(e => e.SortOrder)
            .HasDatabaseName("IX_ServiceCategory_SortOrder");

        builder.HasIndex(e => new { e.IsActive, e.IsDeleted })
            .HasDatabaseName("IX_ServiceCategory_IsActive_IsDeleted")
            .HasFilter("[IsDeleted] = 0");
    }
}
