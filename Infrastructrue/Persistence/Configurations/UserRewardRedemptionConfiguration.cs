using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class UserRewardRedemptionConfiguration : IEntityTypeConfiguration<UserRewardRedemption>
{
    public void Configure(EntityTypeBuilder<UserRewardRedemption> builder)
    {
        builder.ToTable("UserRewardRedemption");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.PointsSpent)
            .IsRequired();

        builder.Property(e => e.Status)
            .IsRequired();

        builder.Property(e => e.RedemptionCode)
            .HasMaxLength(50);

        builder.Property(e => e.ProviderType)
            .HasMaxLength(50);

        builder.Property(e => e.RedeemedAt)
            .IsRequired()
            .HasColumnType("datetime");

        builder.Property(e => e.FulfilledAt)
            .HasColumnType("datetime");

        builder.Property(e => e.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt)
            .HasColumnType("datetime");

        builder.Property(e => e.ModifiedAt)
            .HasColumnType("datetime");

        // Foreign Keys
        builder.HasOne(d => d.User)
            .WithMany(p => p.RewardRedemptions)
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_UserRewardRedemption_UserAccount");

        builder.HasOne(d => d.Reward)
            .WithMany(p => p.Redemptions)
            .HasForeignKey(d => d.LoyaltyRewardId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_UserRewardRedemption_LoyaltyReward");

        builder.HasOne(d => d.Transaction)
            .WithMany(p => p.Redemptions)
            .HasForeignKey(d => d.LoyaltyPointTransactionId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_UserRewardRedemption_LoyaltyPointTransaction");

        // Indexes
        builder.HasIndex(e => e.UserId)
            .HasDatabaseName("IX_UserRewardRedemption_UserId");

        builder.HasIndex(e => e.LoyaltyRewardId)
            .HasDatabaseName("IX_UserRewardRedemption_LoyaltyRewardId");

        builder.HasIndex(e => e.Status)
            .HasDatabaseName("IX_UserRewardRedemption_Status");

        builder.HasIndex(e => e.RedemptionCode)
            .HasDatabaseName("IX_UserRewardRedemption_RedemptionCode");

        builder.HasIndex(e => new { e.ProviderType, e.ProviderId })
            .HasDatabaseName("IX_UserRewardRedemption_ProviderType_ProviderId");
    }
}
