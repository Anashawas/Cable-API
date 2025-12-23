using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public partial class PhoneVerificationConfiguration : IEntityTypeConfiguration<PhoneVerification>
{
    public void Configure(EntityTypeBuilder<PhoneVerification> entity)
    {
        entity.ToTable("PhoneVerification");

        entity.HasIndex(e => e.ExpiresAt, "IX_PhoneVerification_ExpiresAt").HasFilter("([IsDeleted]=(0) AND [IsUsed]=(0))");

        entity.HasIndex(e => new { e.PhoneNumber, e.CreatedAt }, "IX_PhoneVerification_PhoneNumber_CreatedAt")
            .IsDescending(false, true)
            .HasFilter("([IsDeleted]=(0))");

        entity.HasIndex(e => e.UserId, "IX_PhoneVerification_UserId").HasFilter("([UserId] IS NOT NULL AND [IsDeleted]=(0))");

        entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
        entity.Property(e => e.OtpCode).HasMaxLength(50);
        entity.Property(e => e.PhoneNumber).HasMaxLength(20);

        entity.HasOne(d => d.User).WithMany(p => p.PhoneVerifications)
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        OnConfigurePartial(entity);
    }

    partial void OnConfigurePartial(EntityTypeBuilder<PhoneVerification> entity);
}