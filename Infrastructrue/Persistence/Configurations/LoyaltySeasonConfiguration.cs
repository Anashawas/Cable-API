using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class LoyaltySeasonConfiguration : IEntityTypeConfiguration<LoyaltySeason>
{
    public void Configure(EntityTypeBuilder<LoyaltySeason> builder)
    {
        builder.ToTable("LoyaltySeason");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.Description)
            .HasMaxLength(500);

        builder.Property(e => e.StartDate)
            .IsRequired()
            .HasColumnType("datetime");

        builder.Property(e => e.EndDate)
            .IsRequired()
            .HasColumnType("datetime");

        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt)
            .HasColumnType("datetime");

        builder.Property(e => e.ModifiedAt)
            .HasColumnType("datetime");

        // Indexes
        builder.HasIndex(e => e.IsActive)
            .HasDatabaseName("IX_LoyaltySeason_IsActive");

        builder.HasIndex(e => new { e.StartDate, e.EndDate })
            .HasDatabaseName("IX_LoyaltySeason_StartDate_EndDate");
    }
}
