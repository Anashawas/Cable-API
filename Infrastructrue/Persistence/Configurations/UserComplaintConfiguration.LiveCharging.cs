using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

/// <summary>Sprint 5 (§13.1): "problem with this session" complaints. Columns from Scripts/Sprint5_LiveCharging_B.sql.</summary>
public partial class UserComplaintConfiguration
{
    partial void OnConfigurePartial(EntityTypeBuilder<UserComplaint> entity)
    {
        entity.Property(e => e.Kind).IsRequired().HasMaxLength(20).HasDefaultValue("General");
        entity.Property(e => e.Reason).HasMaxLength(20);
        entity.Property(e => e.ImageFileName).HasMaxLength(260);
        entity.Property(e => e.Reply).HasMaxLength(1000);

        // "One open issue per session" and the session's issue list.
        entity.HasIndex(e => e.OcppTransactionId)
            .HasDatabaseName("IX_UserComplaints_OcppTransactionId")
            .HasFilter("[OcppTransactionId] IS NOT NULL");
    }
}
