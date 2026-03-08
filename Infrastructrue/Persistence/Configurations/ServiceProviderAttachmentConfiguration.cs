using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class ServiceProviderAttachmentConfiguration : IEntityTypeConfiguration<ServiceProviderAttachment>
{
    public void Configure(EntityTypeBuilder<ServiceProviderAttachment> builder)
    {
        builder.ToTable("ServiceProviderAttachment");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.FileExtension)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.FileName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.ContentType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt)
            .HasColumnType("datetime");

        builder.Property(e => e.ModifiedAt)
            .HasColumnType("datetime");

        // Foreign Key
        builder.HasOne(d => d.ServiceProvider)
            .WithMany(p => p.ServiceProviderAttachments)
            .HasForeignKey(d => d.ServiceProviderId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_ServiceProviderAttachment_ServiceProvider");

        // Indexes
        builder.HasIndex(e => e.ServiceProviderId)
            .HasDatabaseName("IX_ServiceProviderAttachment_ServiceProviderId");

        builder.HasIndex(e => e.IsDeleted)
            .HasDatabaseName("IX_ServiceProviderAttachment_IsDeleted")
            .HasFilter("[IsDeleted] = 0");
    }
}
