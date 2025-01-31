using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class BannerAttachmentConfigurations : IEntityTypeConfiguration<BannerAttachment>
{
    public void Configure(EntityTypeBuilder<BannerAttachment> builder)
    {
        builder.ToTable(nameof(BannerAttachment));
        builder.Property(x => x.ContentType).HasMaxLength(500);
        builder.Property(x => x.FileName).HasMaxLength(255);
        builder.Property(x => x.FileExtension).HasMaxLength(50);
        builder.HasOne(x=>x.Banner).WithMany(x=>x.BannerAttachments).HasForeignKey(x=>x.BannerId).OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_BannerAttachment_Banner");
    }
}