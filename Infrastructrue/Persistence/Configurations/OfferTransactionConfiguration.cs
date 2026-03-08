using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class OfferTransactionConfiguration : IEntityTypeConfiguration<OfferTransaction>
{
    public void Configure(EntityTypeBuilder<OfferTransaction> builder)
    {
        builder.ToTable("OfferTransaction");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.OfferCode)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(e => e.Status)
            .IsRequired();

        builder.Property(e => e.PointsDeducted)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.MonetaryValue)
            .IsRequired()
            .HasColumnType("decimal(18,3)");

        builder.Property(e => e.CurrencyCode)
            .IsRequired()
            .HasMaxLength(10)
            .HasDefaultValue("KWD");

        builder.Property(e => e.ProviderType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.ProviderId)
            .IsRequired();

        builder.Property(e => e.CodeExpiresAt)
            .IsRequired()
            .HasColumnType("datetime");

        builder.Property(e => e.CompletedAt)
            .HasColumnType("datetime");

        builder.Property(e => e.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt)
            .HasColumnType("datetime");

        builder.Property(e => e.ModifiedAt)
            .HasColumnType("datetime");

        // Foreign Keys
        builder.HasOne(d => d.Offer)
            .WithMany(p => p.Transactions)
            .HasForeignKey(d => d.ProviderOfferId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_OfferTransaction_ProviderOffer");

        builder.HasOne(d => d.User)
            .WithMany(p => p.OfferTransactions)
            .HasForeignKey(d => d.UserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_OfferTransaction_User");

        builder.HasOne(d => d.ConfirmedByUser)
            .WithMany()
            .HasForeignKey(d => d.ConfirmedByUserId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_OfferTransaction_ConfirmedByUser");

        // Indexes
        builder.HasIndex(e => e.OfferCode)
            .IsUnique()
            .HasDatabaseName("IX_OfferTransaction_OfferCode_Unique");

        builder.HasIndex(e => e.ProviderOfferId)
            .HasDatabaseName("IX_OfferTransaction_ProviderOfferId");

        builder.HasIndex(e => e.UserId)
            .HasDatabaseName("IX_OfferTransaction_UserId");

        builder.HasIndex(e => e.Status)
            .HasDatabaseName("IX_OfferTransaction_Status");

        builder.HasIndex(e => new { e.ProviderType, e.ProviderId })
            .HasDatabaseName("IX_OfferTransaction_ProviderType_ProviderId");

        builder.HasIndex(e => e.CodeExpiresAt)
            .HasDatabaseName("IX_OfferTransaction_CodeExpiresAt");

        builder.HasIndex(e => e.CompletedAt)
            .HasDatabaseName("IX_OfferTransaction_CompletedAt");
    }
}
