using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class ChargingPointUpdateRequestAttachmentConfiguration
    : IEntityTypeConfiguration<ChargingPointUpdateRequestAttachment>
{
    public void Configure(EntityTypeBuilder<ChargingPointUpdateRequestAttachment> builder)
    {
        builder.ToTable("ChargingPointUpdateRequestAttachment");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AttachmentAction).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(255);
        builder.Property(x => x.FileExtension).HasMaxLength(50);
        builder.Property(x => x.ContentType).HasMaxLength(50);

        builder.HasOne(x => x.UpdateRequest)
            .WithMany(x => x.AttachmentChanges)
            .HasForeignKey(x => x.UpdateRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ExistingAttachment)
            .WithMany()
            .HasForeignKey(x => x.ExistingAttachmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.UpdateRequestId)
            .HasFilter("[IsDeleted] = 0");
    }
}
