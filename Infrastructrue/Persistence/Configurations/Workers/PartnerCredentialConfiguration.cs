using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations.Workers;

public class PartnerCredentialConfiguration : IEntityTypeConfiguration<PartnerCredential>
{
    public void Configure(EntityTypeBuilder<PartnerCredential> builder)
    {
        builder.ToTable("PartnerCredential");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.PasswordHash).IsRequired().HasMaxLength(1000);
        builder.Property(e => e.MustChangePassword).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.FailedLoginCount).IsRequired().HasDefaultValue(0);
        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.CreatedAt).HasColumnType("datetime");
        builder.Property(e => e.ModifiedAt).HasColumnType("datetime");

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_PartnerCredential_UserAccount");

        // One partner password per account.
        builder.HasIndex(e => e.UserId)
            .IsUnique()
            .HasDatabaseName("UX_PartnerCredential_User");
    }
}
