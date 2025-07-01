using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public partial class NotificationTokenConfiguration : IEntityTypeConfiguration<NotificationToken>
{
    public void Configure(EntityTypeBuilder<NotificationToken> builder)
    {
        builder.ToTable("NotificationToken");
        builder.Property(x => x.OsName).HasMaxLength(100);
        builder.Property(x => x.AppVersion).HasMaxLength(100);
        builder.Property(x => x.OsVersion).HasMaxLength(100);
        builder.HasOne(x => x.UserAccount).WithMany(x => x.NotificationTokens).HasForeignKey(x => x.UserId)
            .HasConstraintName("FK_NotificationToken_UserAccount");
    }
}