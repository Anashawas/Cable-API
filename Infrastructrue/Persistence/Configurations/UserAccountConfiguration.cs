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

        entity.Property(e => e.CreatedAt).HasColumnType("datetime");
        entity.Property(e => e.ModifiedAt).HasColumnType("datetime");
        entity.Property(e => e.Name).HasMaxLength(255);
        entity.Property(e => e.Password).HasMaxLength(255);
        entity.Property(e => e.Phone).HasMaxLength(50);
        entity.Property(e => e.RoleId).HasColumnName("RoleID");
        entity.Property(e => e.UserName).HasMaxLength(50);

        entity.Property(e=>e.Email).HasMaxLength(255);
        entity.HasOne(d => d.Role).WithMany(p => p.UserAccounts)
            .HasForeignKey(d => d.RoleId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_UserAccount_Role");

        OnConfigurePartial(entity);
    }

    partial void OnConfigurePartial(EntityTypeBuilder<UserAccount> entity);
}