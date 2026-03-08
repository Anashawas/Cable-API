using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class LoyaltyRewardConfiguration : IEntityTypeConfiguration<LoyaltyReward>
{
    public void Configure(EntityTypeBuilder<LoyaltyReward> builder)
    {
        builder.ToTable("LoyaltyReward");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        builder.Property(e => e.PointsCost)
            .IsRequired();

        builder.Property(e => e.RewardType)
            .IsRequired();

        builder.Property(e => e.RewardValue)
            .HasMaxLength(500);

        builder.Property(e => e.ProviderType)
            .HasMaxLength(50);

        builder.Property(e => e.CurrentRedemptions)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.ImageUrl)
            .HasMaxLength(500);

        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(e => e.ValidFrom)
            .IsRequired()
            .HasColumnType("datetime");

        builder.Property(e => e.ValidTo)
            .HasColumnType("datetime");

        builder.Property(e => e.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt)
            .HasColumnType("datetime");

        builder.Property(e => e.ModifiedAt)
            .HasColumnType("datetime");

        // Foreign Keys
        builder.HasOne(d => d.ServiceCategory)
            .WithMany()
            .HasForeignKey(d => d.ServiceCategoryId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_LoyaltyReward_ServiceCategory");

        // Indexes
        builder.HasIndex(e => new { e.IsActive, e.ValidFrom, e.ValidTo })
            .HasDatabaseName("IX_LoyaltyReward_IsActive_ValidFrom_ValidTo");

        builder.HasIndex(e => e.RewardType)
            .HasDatabaseName("IX_LoyaltyReward_RewardType");

        builder.HasIndex(e => new { e.ProviderType, e.ProviderId })
            .HasDatabaseName("IX_LoyaltyReward_ProviderType_ProviderId");

        builder.HasIndex(e => e.ServiceCategoryId)
            .HasDatabaseName("IX_LoyaltyReward_ServiceCategoryId");
    }
}
