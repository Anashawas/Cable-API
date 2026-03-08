using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class ProviderOfferConfiguration : IEntityTypeConfiguration<ProviderOffer>
{
    public void Configure(EntityTypeBuilder<ProviderOffer> builder)
    {
        builder.ToTable("ProviderOffer");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Title)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.TitleAr)
            .HasMaxLength(255);

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        builder.Property(e => e.DescriptionAr)
            .HasMaxLength(1000);

        builder.Property(e => e.ProviderType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.ProviderId)
            .IsRequired();

        builder.Property(e => e.ApprovalStatus)
            .IsRequired();

        builder.Property(e => e.ApprovalNote)
            .HasMaxLength(500);

        builder.Property(e => e.ApprovedAt)
            .HasColumnType("datetime");

        builder.Property(e => e.PointsCost)
            .IsRequired();

        builder.Property(e => e.MonetaryValue)
            .IsRequired()
            .HasColumnType("decimal(18,3)");

        builder.Property(e => e.CurrencyCode)
            .IsRequired()
            .HasMaxLength(10)
            .HasDefaultValue("KWD");

        builder.Property(e => e.CurrentTotalUses)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.OfferCodeExpiryMinutes)
            .IsRequired()
            .HasDefaultValue(30);

        builder.Property(e => e.ImageUrl)
            .HasMaxLength(500);

        builder.Property(e => e.ValidFrom)
            .IsRequired()
            .HasColumnType("datetime");

        builder.Property(e => e.ValidTo)
            .HasColumnType("datetime");

        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(e => e.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt)
            .HasColumnType("datetime");

        builder.Property(e => e.ModifiedAt)
            .HasColumnType("datetime");

        // Foreign Keys
        builder.HasOne(d => d.ProposedByUser)
            .WithMany(p => p.ProposedOffers)
            .HasForeignKey(d => d.ProposedByUserId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_ProviderOffer_ProposedByUser");

        builder.HasOne(d => d.ApprovedByUser)
            .WithMany()
            .HasForeignKey(d => d.ApprovedByUserId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_ProviderOffer_ApprovedByUser");

        // Indexes
        builder.HasIndex(e => new { e.ProviderType, e.ProviderId })
            .HasDatabaseName("IX_ProviderOffer_ProviderType_ProviderId");

        builder.HasIndex(e => e.ApprovalStatus)
            .HasDatabaseName("IX_ProviderOffer_ApprovalStatus");

        builder.HasIndex(e => new { e.IsActive, e.ValidFrom, e.ValidTo })
            .HasDatabaseName("IX_ProviderOffer_IsActive_ValidFrom_ValidTo");

        builder.HasIndex(e => e.ProposedByUserId)
            .HasDatabaseName("IX_ProviderOffer_ProposedByUserId");

        builder.HasIndex(e => e.IsDeleted)
            .HasDatabaseName("IX_ProviderOffer_IsDeleted");
    }
}
