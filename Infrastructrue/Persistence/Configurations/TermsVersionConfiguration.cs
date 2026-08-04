using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class TermsVersionConfiguration : IEntityTypeConfiguration<TermsVersion>
{
    public void Configure(EntityTypeBuilder<TermsVersion> builder)
    {
        builder.ToTable("TermsVersion");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SystemVersion).IsRequired().HasMaxLength(20);
        builder.Property(x => x.ContentEn).IsRequired();
        builder.Property(x => x.ContentAr).IsRequired();

        builder.HasOne(x => x.Role)
            .WithMany()
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_TermsVersion_Role");

        // One active version per role scope (SQL Server allows a single NULL
        // in a unique index, so "one active general policy" is enforced too).
        builder.HasIndex(x => x.RoleId)
            .IsUnique()
            .HasFilter("[IsActive] = 1 AND [IsDeleted] = 0")
            .HasDatabaseName("UX_TermsVersion_ActivePerRole");
    }
}
