using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class LoyaltyBoostConfiguration : IEntityTypeConfiguration<LoyaltyBoost>
{
    public void Configure(EntityTypeBuilder<LoyaltyBoost> builder)
    {
        builder.ToTable("LoyaltyBoost");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).IsRequired().HasMaxLength(150);
        builder.Property(e => e.NameAr).HasMaxLength(150);
        builder.Property(e => e.Description).HasMaxLength(500);

        builder.Property(e => e.Multiplier).IsRequired();
        builder.Property(e => e.StartsAt).IsRequired();
        builder.Property(e => e.EndsAt).IsRequired();

        builder.Property(e => e.AppliesToAllProviders).IsRequired();
        builder.Property(e => e.Priority).IsRequired();
        builder.Property(e => e.IsActive).IsRequired();

        // Resolution filters on the window and the active flag on every scan.
        builder.HasIndex(e => new { e.IsActive, e.StartsAt, e.EndsAt });
    }
}

public class LoyaltyBoostProviderConfiguration : IEntityTypeConfiguration<LoyaltyBoostProvider>
{
    public void Configure(EntityTypeBuilder<LoyaltyBoostProvider> builder)
    {
        builder.ToTable("LoyaltyBoostProvider");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ProviderType).IsRequired().HasMaxLength(50);
        builder.Property(e => e.ProviderId).IsRequired();

        builder.HasOne(e => e.LoyaltyBoost)
            .WithMany(b => b.Providers)
            .HasForeignKey(e => e.LoyaltyBoostId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.LoyaltyBoostId, e.ProviderType, e.ProviderId }).IsUnique();
        builder.HasIndex(e => new { e.ProviderType, e.ProviderId });
    }
}
