using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class SocialMediaPlatformConfiguration : IEntityTypeConfiguration<SocialMediaPlatform>
{
    public void Configure(EntityTypeBuilder<SocialMediaPlatform> builder)
    {
        builder.ToTable("SocialMediaPlatform");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.NameAr)
            .HasMaxLength(100);

        builder.HasIndex(e => e.Name)
            .IsUnique()
            .HasDatabaseName("UQ_SocialMediaPlatform_Name");

        builder.Property(e => e.IconFileName).HasMaxLength(255);
        builder.Property(e => e.IconExtension).HasMaxLength(50);
        builder.Property(e => e.IconContentType).HasMaxLength(50);

        builder.Property(e => e.DisplayOrder)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(e => e.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt).HasColumnType("datetime");
        builder.Property(e => e.ModifiedAt).HasColumnType("datetime");

        builder.HasIndex(e => e.IsActive)
            .HasDatabaseName("IX_SocialMediaPlatform_IsActive")
            .IncludeProperties(e => new { e.DisplayOrder, e.Name })
            .HasFilter("[IsDeleted] = 0");
    }
}
