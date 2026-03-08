using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class PasswordResetConfiguration : IEntityTypeConfiguration<PasswordReset>
{
    public void Configure(EntityTypeBuilder<PasswordReset> builder)
    {
        builder.ToTable("PasswordReset");

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.Code)
            .HasDatabaseName("IX_PasswordResets_Code");

        builder.HasIndex(x => x.UserId)
            .HasDatabaseName("IX_PasswordResets_UserId");

        builder.HasIndex(x => x.ExpiresAt)
            .HasDatabaseName("IX_PasswordResets_ExpiresAt");

        builder.Property(x => x.Code)
            .IsRequired()
            .HasMaxLength(6);

        builder.Property(x => x.ExpiresAt)
            .IsRequired();

        builder.Property(x => x.IsUsed)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(x => x.UsedAt)
            .IsRequired(false);

        builder.Property(x => x.FailedAttempts)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(x => x.IpAddress)
            .HasMaxLength(45)
            .IsRequired(false);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .HasConstraintName("FK_PasswordResets_UserAccounts")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
