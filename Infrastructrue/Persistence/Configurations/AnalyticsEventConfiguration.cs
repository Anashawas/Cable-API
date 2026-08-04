using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class AnalyticsEventConfiguration : IEntityTypeConfiguration<AnalyticsEvent>
{
    public void Configure(EntityTypeBuilder<AnalyticsEvent> builder)
    {
        builder.ToTable("AnalyticsEvent");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.EntityType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.EntityId)
            .IsRequired();

        builder.Property(e => e.EventType)
            .IsRequired();

        builder.Property(e => e.AnonymousId)
            .HasMaxLength(100);

        builder.Property(e => e.Source)
            .HasMaxLength(20);

        builder.Property(e => e.OccurredAt)
            .HasColumnType("datetime")
            .IsRequired();

        // Primary drill-down: events for one entity, by type, over time.
        builder.HasIndex(e => new { e.EntityType, e.EntityId, e.EventType, e.OccurredAt })
            .HasDatabaseName("IX_AnalyticsEvent_Entity_Event_OccurredAt");

        // Global time-range reports.
        builder.HasIndex(e => e.OccurredAt)
            .HasDatabaseName("IX_AnalyticsEvent_OccurredAt");

        // Unique-visitor computations.
        builder.HasIndex(e => e.UserId)
            .HasDatabaseName("IX_AnalyticsEvent_UserId");
    }
}
