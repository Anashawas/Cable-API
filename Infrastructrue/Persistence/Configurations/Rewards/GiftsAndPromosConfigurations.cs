using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations.Rewards;

// Maps Scripts/Sprint5_GiftsAndPromos.sql (Cable gifts + promo codes). Enums are INT columns.

public class CableGiftConfiguration : IEntityTypeConfiguration<CableGift>
{
    public void Configure(EntityTypeBuilder<CableGift> builder)
    {
        builder.ToTable("CableGift");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.NameAr).IsRequired().HasMaxLength(150);
        builder.Property(e => e.NameEn).IsRequired().HasMaxLength(150);
        builder.Property(e => e.DescriptionAr).HasMaxLength(2000);
        builder.Property(e => e.DescriptionEn).HasMaxLength(2000);
        builder.Property(e => e.HowToReceiveAr).HasMaxLength(1000);
        builder.Property(e => e.HowToReceiveEn).HasMaxLength(1000);
        builder.Property(e => e.ImageUrl).HasMaxLength(500);
        builder.Property(e => e.Cities).HasMaxLength(500);
        builder.Property(e => e.IsActive).IsRequired();
        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.HasIndex(e => new { e.IsActive, e.SortOrder }).HasDatabaseName("IX_CableGift_Active_Sort");
    }
}

public class GiftOrderConfiguration : IEntityTypeConfiguration<GiftOrder>
{
    public void Configure(EntityTypeBuilder<GiftOrder> builder)
    {
        builder.ToTable("GiftOrder");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.OrderCode).IsRequired().HasMaxLength(20);
        builder.Property(e => e.ContactPhone).HasMaxLength(30);
        builder.Property(e => e.Note).HasMaxLength(500);
        builder.Property(e => e.FulfillmentNote).HasMaxLength(1000);
        builder.Property(e => e.CancelReason).HasMaxLength(500);
        builder.Property(e => e.IdempotencyKey).HasMaxLength(64);
        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.HasOne(e => e.Gift).WithMany(g => g.Orders).HasForeignKey(e => e.CableGiftId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(e => e.OrderCode).IsUnique().HasDatabaseName("UX_GiftOrder_OrderCode");
        builder.HasIndex(e => new { e.UserId, e.IdempotencyKey }).IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL").HasDatabaseName("UX_GiftOrder_User_IdempotencyKey");
        builder.HasIndex(e => new { e.CableGiftId, e.UserId, e.Status }).HasDatabaseName("IX_GiftOrder_Gift_User_Status");
        builder.HasIndex(e => new { e.Status, e.CreatedAt }).HasDatabaseName("IX_GiftOrder_Status_CreatedAt");
    }
}

public class PromoCampaignConfiguration : IEntityTypeConfiguration<PromoCampaign>
{
    public void Configure(EntityTypeBuilder<PromoCampaign> builder)
    {
        builder.ToTable("PromoCampaign");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TitleAr).IsRequired().HasMaxLength(150);
        builder.Property(e => e.TitleEn).IsRequired().HasMaxLength(150);
        builder.Property(e => e.DescriptionAr).HasMaxLength(1000);
        builder.Property(e => e.DescriptionEn).HasMaxLength(1000);
        builder.Property(e => e.ScopeLabelAr).HasMaxLength(200);
        builder.Property(e => e.ScopeLabelEn).HasMaxLength(200);
        builder.Property(e => e.CableSharePercent).HasColumnType("decimal(5,2)");
        builder.Property(e => e.CableBudgetJod).HasColumnType("decimal(18,3)");
        builder.Property(e => e.CableSpentJod).HasColumnType("decimal(18,3)").HasDefaultValue(0m);
        builder.Property(e => e.StationIds).HasMaxLength(4000);
        builder.Property(e => e.ServiceProviderIds).HasMaxLength(4000);
        builder.Property(e => e.EffectValue).HasColumnType("decimal(18,3)");
        builder.Property(e => e.MaxDiscountJod).HasColumnType("decimal(18,3)");
        builder.Property(e => e.AppliesTo).IsRequired().HasMaxLength(100);
        builder.Property(e => e.ServiceCategoryIds).HasMaxLength(500);
        builder.Property(e => e.Cities).HasMaxLength(500);
        builder.Property(e => e.MinAmountJod).HasColumnType("decimal(18,3)");
        builder.Property(e => e.PublicCode).HasMaxLength(40);
        builder.Property(e => e.PublicCodeNormalized).HasMaxLength(40);
        builder.Property(e => e.DeactivatedReason).HasMaxLength(40);
        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.HasIndex(e => e.PublicCodeNormalized).IsUnique()
            .HasFilter("[PublicCodeNormalized] IS NOT NULL AND [IsDeleted] = 0")
            .HasDatabaseName("UX_PromoCampaign_PublicCodeNormalized");
    }
}

public class PromoCodeConfiguration : IEntityTypeConfiguration<PromoCode>
{
    public void Configure(EntityTypeBuilder<PromoCode> builder)
    {
        builder.ToTable("PromoCode");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Code).IsRequired().HasMaxLength(20);
        builder.Property(e => e.NormalizedCode).IsRequired().HasMaxLength(20);
        builder.HasOne(e => e.Campaign).WithMany(c => c.Codes).HasForeignKey(e => e.PromoCampaignId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(e => e.NormalizedCode).IsUnique().HasDatabaseName("UX_PromoCode_NormalizedCode");
        builder.HasIndex(e => e.PromoCampaignId).HasDatabaseName("IX_PromoCode_Campaign");
    }
}

public class UserPromoConfiguration : IEntityTypeConfiguration<UserPromo>
{
    public void Configure(EntityTypeBuilder<UserPromo> builder)
    {
        builder.ToTable("UserPromo");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.RevokeReason).HasMaxLength(500);
        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.HasOne(e => e.Campaign).WithMany(c => c.UserPromos).HasForeignKey(e => e.PromoCampaignId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.PromoCode).WithMany().HasForeignKey(e => e.PromoCodeId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.NoAction);

        // One AVAILABLE promo per user and campaign — also what makes two concurrent claims fail cleanly.
        builder.HasIndex(e => new { e.UserId, e.PromoCampaignId }).IsUnique()
            .HasFilter("[Status] = 1 AND [IsDeleted] = 0")
            .HasDatabaseName("UX_UserPromo_User_Campaign_Available");
        builder.HasIndex(e => new { e.UserId, e.Status }).HasDatabaseName("IX_UserPromo_User_Status");
        builder.HasIndex(e => e.PromoCampaignId).HasDatabaseName("IX_UserPromo_Campaign");
        builder.HasIndex(e => e.AssignmentBatchId).HasDatabaseName("IX_UserPromo_AssignmentBatch");
    }
}

public class PromoUsageConfiguration : IEntityTypeConfiguration<PromoUsage>
{
    public void Configure(EntityTypeBuilder<PromoUsage> builder)
    {
        builder.ToTable("PromoUsage");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TransactionType).IsRequired().HasMaxLength(30);
        builder.Property(e => e.ProviderType).IsRequired().HasMaxLength(50);
        builder.Property(e => e.OriginalAmountJod).HasColumnType("decimal(18,3)");
        builder.Property(e => e.DiscountJod).HasColumnType("decimal(18,3)");
        builder.Property(e => e.FinalAmountJod).HasColumnType("decimal(18,3)");
        builder.Property(e => e.CableShareJod).HasColumnType("decimal(18,3)");
        builder.Property(e => e.ProviderShareJod).HasColumnType("decimal(18,3)");
        builder.Property(e => e.CommissionBeforeJod).HasColumnType("decimal(18,3)");
        builder.Property(e => e.CommissionAfterJod).HasColumnType("decimal(18,3)");

        builder.HasOne(e => e.UserPromo).WithMany().HasForeignKey(e => e.UserPromoId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.Campaign).WithMany().HasForeignKey(e => e.PromoCampaignId).OnDelete(DeleteBehavior.NoAction);

        // Single use: a promo can back one transaction only, even under a race.
        builder.HasIndex(e => e.UserPromoId).IsUnique().HasDatabaseName("UX_PromoUsage_UserPromo");
        builder.HasIndex(e => new { e.TransactionType, e.TransactionId }).HasDatabaseName("IX_PromoUsage_Transaction");
        builder.HasIndex(e => e.PromoCampaignId).HasDatabaseName("IX_PromoUsage_Campaign");
        builder.HasIndex(e => new { e.ProviderType, e.ProviderId, e.CreatedAt }).HasDatabaseName("IX_PromoUsage_Provider_CreatedAt");
    }
}

public class PromoClaimAttemptConfiguration : IEntityTypeConfiguration<PromoClaimAttempt>
{
    public void Configure(EntityTypeBuilder<PromoClaimAttempt> builder)
    {
        builder.ToTable("PromoClaimAttempt");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.CodeTried).IsRequired().HasMaxLength(60);
        builder.HasIndex(e => new { e.UserId, e.AttemptedAt }).HasDatabaseName("IX_PromoClaimAttempt_User_AttemptedAt");
    }
}
