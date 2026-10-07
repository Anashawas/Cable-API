using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class PayerConfiguration : IEntityTypeConfiguration<Payer>
{
    public void Configure(EntityTypeBuilder<Payer> builder)
    {
        builder.ToTable("Payer");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.Phone).HasMaxLength(50);
        builder.Property(e => e.Email).HasMaxLength(200);
        builder.Property(e => e.Note).HasMaxLength(500);
        builder.Property(e => e.HasWhatsApp).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.OptOut).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.CreatedAt).HasColumnType("datetime");
        builder.Property(e => e.ModifiedAt).HasColumnType("datetime");

        builder.HasOne(d => d.UserAccount)
            .WithMany()
            .HasForeignKey(d => d.UserAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Payer_UserAccount");

        // A user is one payer, so "the owner paid again" reuses the row.
        builder.HasIndex(e => e.UserAccountId)
            .IsUnique()
            .HasFilter("[UserAccountId] IS NOT NULL AND [IsDeleted] = 0")
            .HasDatabaseName("UX_Payer_UserAccount");

        builder.HasIndex(e => e.Phone).HasDatabaseName("IX_Payer_Phone");
    }
}
