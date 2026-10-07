using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        // Same physical table the station-premium history used, renamed.
        builder.ToTable("Subscription");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EntityType).IsRequired().HasMaxLength(50);
        builder.Property(e => e.StartDate).HasColumnType("datetime").IsRequired();
        builder.Property(e => e.ExpiresAt).HasColumnType("datetime").IsRequired();
        builder.Property(e => e.SwitchedOffAt).HasColumnType("datetime");
        builder.Property(e => e.IsSwitchedOff).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.Note).HasMaxLength(1000);
        builder.Property(e => e.CreatedAt).HasColumnType("datetime");
        builder.Property(e => e.ModifiedAt).HasColumnType("datetime");

        // One live subscription per billable thing. Renewals extend it; they do
        // not create a sibling.
        builder.HasIndex(e => new { e.EntityType, e.EntityId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Subscription_Entity");

        // The renewals dashboard and the grace job both scan by expiry.
        builder.HasIndex(e => e.ExpiresAt).HasDatabaseName("IX_Subscription_ExpiresAt");
    }
}
