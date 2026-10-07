using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations.Ocpp;

public class OcppUserIdTagConfiguration : IEntityTypeConfiguration<OcppUserIdTag>
{
    public void Configure(EntityTypeBuilder<OcppUserIdTag> builder)
    {
        builder.ToTable("OcppUserIdTag");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.IdTag).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Label).HasMaxLength(100);
        builder.Property(e => e.IsEnabled).IsRequired().HasDefaultValue(true);
        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.CreatedAt).HasColumnType("datetime");
        builder.Property(e => e.ModifiedAt).HasColumnType("datetime");

        builder.HasOne(e => e.UserAccount)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_OcppUserIdTag_UserAccount");

        builder.HasIndex(e => e.IdTag).IsUnique().HasDatabaseName("UX_OcppUserIdTag_IdTag").HasFilter("[IsDeleted] = 0");
        builder.HasIndex(e => e.UserId).HasDatabaseName("IX_OcppUserIdTag_User");
    }
}
