using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class ProviderSettlementConfiguration : IEntityTypeConfiguration<ProviderSettlement>
{
    public void Configure(EntityTypeBuilder<ProviderSettlement> builder)
    {
        builder.ToTable("ProviderSettlement");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ProviderType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.ProviderId)
            .IsRequired();

        builder.Property(e => e.PeriodYear)
            .IsRequired();

        builder.Property(e => e.PeriodMonth)
            .IsRequired();

        // Partner Transaction fields
        builder.Property(e => e.PartnerTransactionCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.PartnerTransactionAmount)
            .IsRequired()
            .HasColumnType("decimal(18,3)")
            .HasDefaultValue(0m);

        builder.Property(e => e.PartnerCommissionAmount)
            .IsRequired()
            .HasColumnType("decimal(18,3)")
            .HasDefaultValue(0m);

        // Offer Transaction fields
        builder.Property(e => e.OfferTransactionCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.OfferPaymentAmount)
            .IsRequired()
            .HasColumnType("decimal(18,3)")
            .HasDefaultValue(0m);

        // Net settlement
        builder.Property(e => e.NetAmountDueToProvider)
            .IsRequired()
            .HasColumnType("decimal(18,3)")
            .HasDefaultValue(0m);

        builder.Property(e => e.TotalPointsAwarded)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.TotalPointsDeducted)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.SettlementStatus)
            .IsRequired();

        builder.Property(e => e.InvoicedAt)
            .HasColumnType("datetime");

        builder.Property(e => e.PaidAt)
            .HasColumnType("datetime");

        builder.Property(e => e.PaidAmount)
            .HasColumnType("decimal(18,3)");

        builder.Property(e => e.AdminNote)
            .HasMaxLength(1000);

        builder.Property(e => e.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt)
            .HasColumnType("datetime");

        builder.Property(e => e.ModifiedAt)
            .HasColumnType("datetime");

        // Foreign Keys
        builder.HasOne(d => d.ProviderOwner)
            .WithMany()
            .HasForeignKey(d => d.ProviderOwnerId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_ProviderSettlement_ProviderOwner");

        // Indexes
        builder.HasIndex(e => new { e.ProviderType, e.ProviderId, e.PeriodYear, e.PeriodMonth })
            .IsUnique()
            .HasDatabaseName("IX_ProviderSettlement_Provider_Period");

        builder.HasIndex(e => e.SettlementStatus)
            .HasDatabaseName("IX_ProviderSettlement_SettlementStatus");

        builder.HasIndex(e => e.ProviderOwnerId)
            .HasDatabaseName("IX_ProviderSettlement_ProviderOwnerId");

        builder.HasIndex(e => new { e.PeriodYear, e.PeriodMonth })
            .HasDatabaseName("IX_ProviderSettlement_PeriodYear_PeriodMonth");
    }
}
