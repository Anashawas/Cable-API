using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class AnalyticsDailyRollupConfiguration : IEntityTypeConfiguration<AnalyticsDailyRollup>
{
    public void Configure(EntityTypeBuilder<AnalyticsDailyRollup> builder)
    {
        builder.ToTable("AnalyticsDailyRollup");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.EntityType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.EntityId)
            .IsRequired();

        builder.Property(e => e.EventType)
            .IsRequired();

        builder.Property(e => e.Day)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(e => e.Count)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.LastEventAt)
            .HasColumnType("datetime")
            .IsRequired();

        // One bucket per entity/event/day — also the lookup for the atomic upsert.
        builder.HasIndex(e => new { e.EntityType, e.EntityId, e.EventType, e.Day })
            .IsUnique()
            .HasDatabaseName("UX_AnalyticsDailyRollup_Entity_Event_Day");
    }
}
