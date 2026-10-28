using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations.Billing;

/// <summary>Maps Scripts/Sprint5_PartnerBilling.sql.</summary>
public class BillingPlanConfiguration : IEntityTypeConfiguration<BillingPlan>
{
    public void Configure(EntityTypeBuilder<BillingPlan> builder)
    {
        builder.ToTable("BillingPlan");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Key).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Category).IsRequired().HasMaxLength(20);
        builder.Property(e => e.NameAr).IsRequired().HasMaxLength(100);
        builder.Property(e => e.NameEn).IsRequired().HasMaxLength(100);
        builder.Property(e => e.DescriptionAr).HasMaxLength(1000);
        builder.Property(e => e.DescriptionEn).HasMaxLength(1000);
        builder.Property(e => e.AppliesTo).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Features).IsRequired();
        builder.Property(e => e.IncludesPlanIds).IsRequired().HasMaxLength(200);
        builder.Property(e => e.LegacyEntityTypes).HasMaxLength(200);
        builder.Property(e => e.BadgeColor).HasMaxLength(20);
        builder.Property(e => e.TargetMarginPercent).HasColumnType("decimal(5,2)");
        builder.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(e => e.IsVisible).IsRequired().HasDefaultValue(true);
        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.CreatedAt).HasColumnType("datetime");
        builder.Property(e => e.ModifiedAt).HasColumnType("datetime");

        builder.HasMany(e => e.PriceOptions).WithOne(o => o.Plan)
            .HasForeignKey(o => o.BillingPlanId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_BillingPlanPriceOption_BillingPlan");

        builder.HasIndex(e => e.Key).IsUnique().HasDatabaseName("UX_BillingPlan_Key");
    }
}

public class BillingPlanPriceOptionConfiguration : IEntityTypeConfiguration<BillingPlanPriceOption>
{
    public void Configure(EntityTypeBuilder<BillingPlanPriceOption> builder)
    {
        builder.ToTable("BillingPlanPriceOption");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.PriceJod).HasColumnType("decimal(18,3)").IsRequired();
        builder.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.CreatedAt).HasColumnType("datetime");
        builder.Property(e => e.ModifiedAt).HasColumnType("datetime");
        builder.HasIndex(e => e.BillingPlanId).HasDatabaseName("IX_BillingPlanPriceOption_Plan");
    }
}

public class BillingPlanChangeLogConfiguration : IEntityTypeConfiguration<BillingPlanChangeLog>
{
    public void Configure(EntityTypeBuilder<BillingPlanChangeLog> builder)
    {
        builder.ToTable("BillingPlanChangeLog");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.ChangedAt).HasColumnType("datetime");
        builder.Property(e => e.Changes).IsRequired();
        builder.Property(e => e.Note).HasMaxLength(1000);
        builder.HasIndex(e => new { e.BillingPlanId, e.ChangedAt }).HasDatabaseName("IX_BillingPlanChangeLog_Plan_ChangedAt");
    }
}

public class PlanSubscriptionConfiguration : IEntityTypeConfiguration<PlanSubscription>
{
    public void Configure(EntityTypeBuilder<PlanSubscription> builder)
    {
        builder.ToTable("PlanSubscription");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.ProviderType).IsRequired().HasMaxLength(50);
        builder.Property(e => e.PlanKey).IsRequired().HasMaxLength(50);
        builder.Property(e => e.PlanCategory).IsRequired().HasMaxLength(20);
        builder.Property(e => e.PlanNameAr).IsRequired().HasMaxLength(100);
        builder.Property(e => e.PlanNameEn).IsRequired().HasMaxLength(100);
        builder.Property(e => e.PriceOptionSnapshot).HasMaxLength(400);
        builder.Property(e => e.StartsAt).HasColumnType("datetime").IsRequired();
        builder.Property(e => e.EndsAt).HasColumnType("datetime");
        builder.Property(e => e.ScansUsed).IsRequired().HasDefaultValue(0);
        builder.Property(e => e.ScansCarriedOver).IsRequired().HasDefaultValue(0);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20).HasDefaultValue("Active");
        builder.Property(e => e.PricePaidJod).HasColumnType("decimal(18,3)").IsRequired();
        builder.Property(e => e.CreditAppliedJod).HasColumnType("decimal(18,3)").IsRequired();
        builder.Property(e => e.Source).IsRequired().HasMaxLength(20);
        builder.Property(e => e.CancelledAt).HasColumnType("datetime");
        builder.Property(e => e.CancelReason).HasMaxLength(500);
        builder.Property(e => e.Note).HasMaxLength(1000);
        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.CreatedAt).HasColumnType("datetime");
        builder.Property(e => e.ModifiedAt).HasColumnType("datetime");

        builder.HasOne(e => e.Plan).WithMany()
            .HasForeignKey(e => e.BillingPlanId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_PlanSubscription_BillingPlan");

        builder.HasIndex(e => new { e.ProviderType, e.ProviderId, e.PlanCategory }).HasDatabaseName("IX_PlanSubscription_Provider");
        builder.HasIndex(e => e.BillingPlanId).HasDatabaseName("IX_PlanSubscription_Plan");
        builder.HasIndex(e => e.PaymentId).HasDatabaseName("IX_PlanSubscription_Payment");
        builder.HasIndex(e => e.SubscriptionId).HasDatabaseName("IX_PlanSubscription_Subscription");
    }
}

public class PlanUpgradeRequestConfiguration : IEntityTypeConfiguration<PlanUpgradeRequest>
{
    public void Configure(EntityTypeBuilder<PlanUpgradeRequest> builder)
    {
        builder.ToTable("PlanUpgradeRequest");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.ProviderType).IsRequired().HasMaxLength(50);
        builder.Property(e => e.PlanKey).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Kind).IsRequired().HasMaxLength(20);
        builder.Property(e => e.Note).HasMaxLength(1000);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20).HasDefaultValue("Pending");
        builder.Property(e => e.HandledAt).HasColumnType("datetime");
        builder.Property(e => e.RejectReason).HasMaxLength(500);
        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.CreatedAt).HasColumnType("datetime");
        builder.Property(e => e.ModifiedAt).HasColumnType("datetime");
        builder.HasIndex(e => new { e.ProviderType, e.ProviderId, e.Status }).HasDatabaseName("IX_PlanUpgradeRequest_Provider_Status");
        builder.HasIndex(e => e.Status).HasDatabaseName("IX_PlanUpgradeRequest_Status");
    }
}

public class BillingEventConfiguration : IEntityTypeConfiguration<BillingEvent>
{
    public void Configure(EntityTypeBuilder<BillingEvent> builder)
    {
        builder.ToTable("BillingEvent");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.EventKey).IsRequired().HasMaxLength(150);
        builder.Property(e => e.Kind).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Audience).IsRequired().HasMaxLength(10);
        builder.Property(e => e.ProviderType).HasMaxLength(50);
        builder.Property(e => e.Title).HasMaxLength(300);
        builder.Property(e => e.Body).HasMaxLength(1000);
        builder.Property(e => e.CreatedAt).HasColumnType("datetime");
        builder.Property(e => e.AcknowledgedAt).HasColumnType("datetime");
        builder.HasIndex(e => e.EventKey).IsUnique().HasDatabaseName("UX_BillingEvent_EventKey");
        builder.HasIndex(e => new { e.Audience, e.CreatedAt }).HasDatabaseName("IX_BillingEvent_Audience_CreatedAt");
    }
}

public class PartnerTransactionBundleScanConfiguration : IEntityTypeConfiguration<PartnerTransactionBundleScan>
{
    public void Configure(EntityTypeBuilder<PartnerTransactionBundleScan> builder)
    {
        builder.ToTable("PartnerTransactionBundleScan");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.QuotaReached).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.ConsumedAt).HasColumnType("datetime");
        builder.Property(e => e.RefundedAt).HasColumnType("datetime");
        builder.Property(e => e.CreatedAt).HasColumnType("datetime");

        builder.HasOne(e => e.PartnerTransaction).WithMany()
            .HasForeignKey(e => e.PartnerTransactionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_PartnerTransactionBundleScan_PartnerTransaction");

        builder.HasIndex(e => e.PartnerTransactionId).IsUnique().HasDatabaseName("UX_PartnerTransactionBundleScan_Transaction");
        builder.HasIndex(e => new { e.ConsumedPlanSubscriptionId, e.ConsumedAt }).HasDatabaseName("IX_PartnerTransactionBundleScan_Consumed");
    }
}
