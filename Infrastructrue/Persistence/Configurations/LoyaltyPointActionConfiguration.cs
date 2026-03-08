using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class LoyaltyPointActionConfiguration : IEntityTypeConfiguration<LoyaltyPointAction>
{
    public void Configure(EntityTypeBuilder<LoyaltyPointAction> builder)
    {
        builder.ToTable("LoyaltyPointAction");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ActionCode)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.Description)
            .HasMaxLength(500);

        builder.Property(e => e.Points)
            .IsRequired();

        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(e => e.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt)
            .HasColumnType("datetime");

        builder.Property(e => e.ModifiedAt)
            .HasColumnType("datetime");

        // Indexes
        builder.HasIndex(e => e.ActionCode)
            .IsUnique()
            .HasDatabaseName("IX_LoyaltyPointAction_ActionCode_Unique");
    }
}
