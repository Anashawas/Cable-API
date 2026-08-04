using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.ToTable("NotificationTemplate");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Body).IsRequired().HasMaxLength(1000);

        builder.HasOne(x => x.NotificationType)
            .WithMany()
            .HasForeignKey(x => x.NotificationTypeId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_NotificationTemplate_NotificationType");

        builder.HasIndex(x => x.NotificationTypeId)
            .HasDatabaseName("IX_NotificationTemplate_NotificationTypeId");
    }
}
