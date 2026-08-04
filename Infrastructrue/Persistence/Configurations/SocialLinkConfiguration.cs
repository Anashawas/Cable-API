using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class SocialLinkConfiguration : IEntityTypeConfiguration<SocialLink>
{
    public void Configure(EntityTypeBuilder<SocialLink> builder)
    {
        builder.ToTable("SocialLink", t =>
        {
            t.HasCheckConstraint(
                "CK_SocialLink_ProviderType",
                "ProviderType IN (N'ServiceProvider', N'ChargingPoint')");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ProviderType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.Url)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(e => e.DisplayOrder)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt).HasColumnType("datetime");
        builder.Property(e => e.ModifiedAt).HasColumnType("datetime");

        builder.HasOne(e => e.SocialMediaPlatform)
            .WithMany(p => p.Links)
            .HasForeignKey(e => e.SocialMediaPlatformId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_SocialLink_Platform");

        builder.HasIndex(e => new { e.ProviderType, e.ProviderId })
            .HasDatabaseName("IX_SocialLink_Provider")
            .IncludeProperties(e => new { e.SocialMediaPlatformId, e.Url, e.DisplayOrder, e.IsDeleted })
            .HasFilter("[IsDeleted] = 0");
    }
}
