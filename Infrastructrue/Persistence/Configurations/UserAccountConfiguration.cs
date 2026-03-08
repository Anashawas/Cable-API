using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Domain.Enitites;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace Infrastructrue.Persistence.Configurations;

public partial class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> entity)
    {
        entity.ToTable("UserAccount");

        entity.HasIndex(e => e.Email, "IX_UserAccount_Email_Unique")
            .IsUnique()
            .HasFilter("([Email] IS NOT NULL)");

        entity.HasIndex(e => new { e.Phone, e.IsDeleted }, "IX_UserAccount_Phone_IsDeleted").HasFilter("([Phone] IS NOT NULL)");

        entity.Property(e => e.City).HasMaxLength(50);
        entity.Property(e => e.Country).HasMaxLength(50);
        entity.Property(e => e.CreatedAt).HasColumnType("datetime");
        entity.Property(e => e.Email).HasMaxLength(255);
        entity.Property(e => e.FirebaseUId)
            .HasMaxLength(255)
            .HasColumnName("FirebaseUId");
        entity.Property(e => e.ModifiedAt).HasColumnType("datetime");
        entity.Property(e => e.Name).HasMaxLength(255);
        entity.Property(e => e.Password).HasMaxLength(255);
        entity.Property(e => e.Phone).HasMaxLength(50);
        entity.Property(e => e.RegistrationProvider).HasMaxLength(255);
        entity.Property(e => e.SecurityStamp).HasMaxLength(50);
        entity.Property(e => e.RoleId).HasColumnName("RoleID");

        entity.HasOne(d => d.Role).WithMany(p => p.UserAccounts)
            .HasForeignKey(d => d.RoleId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_UserAccount_Role");

        OnConfigurePartial(entity);
    }

    partial void OnConfigurePartial(EntityTypeBuilder<UserAccount> entity);
}