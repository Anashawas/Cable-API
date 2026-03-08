using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public partial class EmergencyServiceAttachmentConfiguration : IEntityTypeConfiguration<EmergencyServiceAttachment>
{
    public void Configure(EntityTypeBuilder<EmergencyServiceAttachment> builder)
    {
        builder.ToTable("EmergencyServiceAttachment");

        builder.Property(e => e.CreatedAt).HasColumnType("datetime");
        builder.Property(e => e.ModifiedAt).HasColumnType("datetime");
        builder.Property(e => e.FileExtension).HasMaxLength(50);
        builder.Property(e => e.FileName).HasMaxLength(255);
        builder.Property(e => e.ContentType).HasMaxLength(50);

        builder.HasOne(d => d.EmergencyService)
            .WithMany(p => p.EmergencyServiceAttachments)
            .HasForeignKey(d => d.EmergencyServiceId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_EmergencyServiceAttachment_EmergencyService");

        OnConfigurePartial(builder);
    }

    partial void OnConfigurePartial(EntityTypeBuilder<EmergencyServiceAttachment> builder);
}
