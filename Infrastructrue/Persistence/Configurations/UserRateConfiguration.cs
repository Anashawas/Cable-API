using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class UserRateConfiguration : IEntityTypeConfiguration<UserRate>
{
    public void Configure(EntityTypeBuilder<UserRate> builder)
    {
        builder.ToTable("UserRate");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ProviderType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.Rating)
            .IsRequired();

        builder.Property(e => e.Comment)
            .HasMaxLength(1000);

        builder.Property(e => e.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt)
            .HasColumnType("datetime");

        builder.Property(e => e.ModifiedAt)
            .HasColumnType("datetime");

        // Both FKs point at UserAccount, so neither may cascade — SQL Server
        // rejects the multiple cascade paths that would create. Restrict also
        // keeps EF from trying to null a required FK on delete, which is what
        // ClientSetNull (used elsewhere for optional FKs) would attempt here.
        builder.HasOne(d => d.User)
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_UserRate_UserAccount");

        builder.HasOne(d => d.RatedByUser)
            .WithMany()
            .HasForeignKey(d => d.RatedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_UserRate_RatedByUserAccount");

        builder.HasOne(d => d.PartnerTransaction)
            .WithMany()
            .HasForeignKey(d => d.PartnerTransactionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_UserRate_PartnerTransaction");

        // One rating per transaction. This is the whole anti-spam story: a
        // provider cannot rate the same visit twice, and cannot rate a driver
        // at all without a completed transaction to anchor to.
        builder.HasIndex(e => e.PartnerTransactionId)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_UserRate_PartnerTransactionId");

        // Serves the driver's own average and the provider-facing lookup.
        builder.HasIndex(e => e.UserId)
            .HasDatabaseName("IX_UserRate_UserId");

        builder.HasIndex(e => new { e.ProviderType, e.ProviderId })
            .HasDatabaseName("IX_UserRate_Provider");
    }
}
