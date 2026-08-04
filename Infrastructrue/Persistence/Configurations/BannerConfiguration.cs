using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public partial class BannerConfiguration : IEntityTypeConfiguration<Banner>
{
    public void Configure(EntityTypeBuilder<Banner> builder)
    {
        builder.ToTable(nameof(Banner));
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Phone).HasMaxLength(50);
        builder.Property(x => x.Email).HasMaxLength(50);

        // Location targeting + ad-serving fields
        builder.Property(x => x.TargetType).IsRequired().HasMaxLength(20).HasDefaultValue("national");
        builder.Property(x => x.TargetCity).HasMaxLength(100);
        builder.Property(x => x.LinkedEntityType).HasMaxLength(20);

        builder.HasOne(x => x.Campaign)
            .WithMany()
            .HasForeignKey(x => x.CampaignId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Banner_Campaign");
    }
}