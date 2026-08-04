using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class UserTermsAcceptanceConfiguration : IEntityTypeConfiguration<UserTermsAcceptance>
{
    public void Configure(EntityTypeBuilder<UserTermsAcceptance> builder)
    {
        builder.ToTable("UserTermsAcceptance");
        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_UserTermsAcceptance_UserAccount");

        builder.HasOne(x => x.TermsVersion)
            .WithMany(x => x.Acceptances)
            .HasForeignKey(x => x.TermsVersionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_UserTermsAcceptance_TermsVersion");

        // Accepting the same version twice is idempotent, not a second row.
        builder.HasIndex(x => new { x.UserId, x.TermsVersionId })
            .IsUnique()
            .HasDatabaseName("UX_UserTermsAcceptance_User_Version");

        builder.HasIndex(x => x.TermsVersionId);
    }
}
