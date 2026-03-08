using Cable.Core.Enums;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public partial class NotificationTokenConfiguration : IEntityTypeConfiguration<NotificationToken>
{
    public void Configure(EntityTypeBuilder<NotificationToken> builder)
    {
        builder.ToTable("NotificationToken");

        builder.HasKey(x => x.Id);

        // Composite unique constraint on (UserId, AppType)
        // Allows each user to have one token per app type
        builder.HasIndex(x => new { x.UserId, x.AppType })
            .IsUnique()
            .HasDatabaseName("UQ_NotificationToken_UserId_AppType");

        // Index on AppType for query performance
        builder.HasIndex(x => x.AppType)
            .HasDatabaseName("IX_NotificationToken_AppType");

        builder.Property(x => x.Token).IsRequired();
        builder.Property(x => x.OsName).HasMaxLength(100).HasColumnName("OSName").IsRequired();
        builder.Property(x => x.AppVersion).HasMaxLength(100).IsRequired();
        builder.Property(x => x.OsVersion).HasMaxLength(100).HasColumnName("OSVersion").IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("getdate()");
        builder.Property(x => x.UpdatedAt).IsRequired().HasDefaultValueSql("getdate()");

        // AppType configuration
        builder.Property(x => x.AppType)
            .IsRequired()
            .HasDefaultValue(FirebaseAppType.UserApp)
            .HasConversion<int>();

        builder.HasOne(x => x.UserAccount)
            .WithMany(x => x.NotificationTokens)
            .HasForeignKey(x => x.UserId)
            .HasConstraintName("FK_NotificationToken_UserAccount")
            .OnDelete(DeleteBehavior.Cascade);
    }
}