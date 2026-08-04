using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Enitites;

#nullable disable

namespace Infrastructrue.Persistence.Configurations;

public partial class OfferAttachmentConfiguration : IEntityTypeConfiguration<OfferAttachment>
{
    public void Configure(EntityTypeBuilder<OfferAttachment> builder)
    {
        builder.ToTable("OfferAttachment");

        builder.Property(e => e.CreatedAt).HasColumnType("datetime");
        builder.Property(e => e.FileExtension).HasMaxLength(50);
        builder.Property(e => e.ModifiedAt).HasColumnType("datetime");

        builder.Property(x => x.ContentType).HasMaxLength(50);
        builder.Property(x => x.FileName).HasMaxLength(255);

        builder.HasOne(d => d.Offer).WithMany(p => p.OfferAttachments)
            .HasForeignKey(d => d.OfferId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_OfferAttachment_ProviderOffer");

        OnConfigurePartial(builder);
    }

    partial void OnConfigurePartial(EntityTypeBuilder<OfferAttachment> entity);
}
