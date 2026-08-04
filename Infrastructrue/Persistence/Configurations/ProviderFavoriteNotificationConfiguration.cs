using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class ProviderFavoriteNotificationConfiguration : IEntityTypeConfiguration<ProviderFavoriteNotification>
{
    public void Configure(EntityTypeBuilder<ProviderFavoriteNotification> builder)
    {
        builder.ToTable("ProviderFavoriteNotification");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ProviderType).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(256);
        builder.Property(x => x.Body).IsRequired().HasMaxLength(1000);
        builder.Property(x => x.Status).IsRequired().HasMaxLength(10).HasDefaultValue("sent");

        builder.HasOne(x => x.SentBy)
            .WithMany()
            .HasForeignKey(x => x.SentByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_ProviderFavoriteNotification_UserAccount");

        // Rate-limit lookup: sends for a provider within a time window.
        builder.HasIndex(x => new { x.ProviderType, x.ProviderId, x.CreatedAt })
            .HasDatabaseName("IX_ProviderFavoriteNotification_Provider_CreatedAt");
    }
}
