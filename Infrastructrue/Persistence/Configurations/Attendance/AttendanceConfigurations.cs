using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations.Attendance;

public class WorkerShiftScheduleConfiguration : IEntityTypeConfiguration<WorkerShiftSchedule>
{
    public void Configure(EntityTypeBuilder<WorkerShiftSchedule> b)
    {
        b.ToTable("WorkerShiftSchedule");
        b.HasKey(e => e.Id);
        b.Property(e => e.IsDeleted).HasDefaultValue(false);
        b.Property(e => e.CreatedAt).HasColumnType("datetime");
        b.Property(e => e.ModifiedAt).HasColumnType("datetime");
        b.HasOne(e => e.ProviderManager).WithMany().HasForeignKey(e => e.ProviderManagerId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_WorkerShiftSchedule_ProviderManager");
        b.HasMany(e => e.Days).WithOne(d => d.Schedule).HasForeignKey(d => d.WorkerShiftScheduleId).OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_WorkerShiftDay_Schedule");
        b.HasIndex(e => new { e.ProviderManagerId, e.EffectiveFrom }).HasDatabaseName("IX_WorkerShiftSchedule_Worker_From").HasFilter("[IsDeleted] = 0");
    }
}

public class WorkerShiftDayConfiguration : IEntityTypeConfiguration<WorkerShiftDay>
{
    public void Configure(EntityTypeBuilder<WorkerShiftDay> b)
    {
        b.ToTable("WorkerShiftDay");
        b.HasKey(e => e.Id);
        b.Property(e => e.IsDeleted).HasDefaultValue(false);
        b.Property(e => e.IsOff).HasDefaultValue(false);
        b.Property(e => e.CreatedAt).HasColumnType("datetime");
        b.Property(e => e.ModifiedAt).HasColumnType("datetime");
    }
}

public class PublicHolidayConfiguration : IEntityTypeConfiguration<PublicHoliday>
{
    public void Configure(EntityTypeBuilder<PublicHoliday> b)
    {
        b.ToTable("PublicHoliday");
        b.HasKey(e => e.Id);
        b.Property(e => e.NameAr).IsRequired().HasMaxLength(200);
        b.Property(e => e.NameEn).IsRequired().HasMaxLength(200);
        b.Property(e => e.IsDeleted).HasDefaultValue(false);
        b.Property(e => e.CreatedAt).HasColumnType("datetime");
        b.Property(e => e.ModifiedAt).HasColumnType("datetime");
        b.HasIndex(e => e.Date).HasDatabaseName("IX_PublicHoliday_Date");
    }
}

public class PunchZoneConfiguration : IEntityTypeConfiguration<PunchZone>
{
    public void Configure(EntityTypeBuilder<PunchZone> b)
    {
        b.ToTable("PunchZone");
        b.HasKey(e => e.Id);
        b.Property(e => e.ProviderType).IsRequired().HasMaxLength(50);
        b.Property(e => e.Name).IsRequired().HasMaxLength(200);
        b.Property(e => e.IsActive).HasDefaultValue(true);
        b.Property(e => e.IsDeleted).HasDefaultValue(false);
        b.Property(e => e.CreatedAt).HasColumnType("datetime");
        b.Property(e => e.ModifiedAt).HasColumnType("datetime");
        b.HasMany(e => e.Workers).WithOne(w => w.Zone).HasForeignKey(w => w.PunchZoneId).OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_PunchZoneWorker_Zone");
        b.HasIndex(e => new { e.ProviderType, e.ProviderId }).HasDatabaseName("IX_PunchZone_Provider").HasFilter("[IsDeleted] = 0");
    }
}

public class PunchZoneWorkerConfiguration : IEntityTypeConfiguration<PunchZoneWorker>
{
    public void Configure(EntityTypeBuilder<PunchZoneWorker> b)
    {
        b.ToTable("PunchZoneWorker");
        b.HasKey(e => e.Id);
        b.Property(e => e.IsDeleted).HasDefaultValue(false);
        b.Property(e => e.CreatedAt).HasColumnType("datetime");
        b.Property(e => e.ModifiedAt).HasColumnType("datetime");
        b.HasOne(e => e.ProviderManager).WithMany().HasForeignKey(e => e.ProviderManagerId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_PunchZoneWorker_ProviderManager");
        b.HasIndex(e => e.ProviderManagerId).HasDatabaseName("IX_PunchZoneWorker_Worker").HasFilter("[IsDeleted] = 0");
    }
}

public class AttendanceDayConfiguration : IEntityTypeConfiguration<AttendanceDay>
{
    public void Configure(EntityTypeBuilder<AttendanceDay> b)
    {
        b.ToTable("AttendanceDay");
        b.HasKey(e => e.Id);
        b.Property(e => e.Status).IsRequired().HasMaxLength(20);
        b.Property(e => e.Flags).HasMaxLength(200);
        b.Property(e => e.IsOpen).HasDefaultValue(false);
        b.Property(e => e.IsDeleted).HasDefaultValue(false);
        b.Property(e => e.CreatedAt).HasColumnType("datetime");
        b.Property(e => e.ModifiedAt).HasColumnType("datetime");
        b.HasOne(e => e.ProviderManager).WithMany().HasForeignKey(e => e.ProviderManagerId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_AttendanceDay_ProviderManager");
        b.HasMany(e => e.Punches).WithOne(p => p.Day).HasForeignKey(p => p.AttendanceDayId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Punch_AttendanceDay");
        b.HasIndex(e => new { e.ProviderManagerId, e.WorkDate }).IsUnique().HasDatabaseName("UX_AttendanceDay_Worker_Date").HasFilter("[IsDeleted] = 0");
        b.HasIndex(e => new { e.ProviderManagerId, e.IsOpen }).HasDatabaseName("IX_AttendanceDay_Worker_Open").HasFilter("[IsDeleted] = 0 AND [IsOpen] = 1");
    }
}

public class AttendanceCorrectionConfiguration : IEntityTypeConfiguration<AttendanceCorrection>
{
    public void Configure(EntityTypeBuilder<AttendanceCorrection> b)
    {
        b.ToTable("AttendanceCorrection");
        b.HasKey(e => e.Id);
        b.Property(e => e.Field).IsRequired().HasMaxLength(20);
        b.Property(e => e.FromValue).HasMaxLength(100);
        b.Property(e => e.ToValue).HasMaxLength(100);
        b.Property(e => e.Note).IsRequired().HasMaxLength(500);
        b.Property(e => e.IsDeleted).HasDefaultValue(false);
        b.Property(e => e.CreatedAt).HasColumnType("datetime");
        b.Property(e => e.ModifiedAt).HasColumnType("datetime");
        b.HasOne(e => e.Day).WithMany().HasForeignKey(e => e.AttendanceDayId).OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_AttendanceCorrection_Day");
        b.HasIndex(e => e.AttendanceDayId).HasDatabaseName("IX_AttendanceCorrection_Day");
    }
}

public class AttendanceEventConfiguration : IEntityTypeConfiguration<AttendanceEvent>
{
    public void Configure(EntityTypeBuilder<AttendanceEvent> b)
    {
        b.ToTable("AttendanceEvent");
        b.HasKey(e => e.Id);
        b.Property(e => e.ProviderType).IsRequired().HasMaxLength(50);
        b.Property(e => e.Kind).IsRequired().HasMaxLength(30);
        b.Property(e => e.IsDeleted).HasDefaultValue(false);
        b.Property(e => e.CreatedAt).HasColumnType("datetime");
        b.Property(e => e.ModifiedAt).HasColumnType("datetime");
        b.HasIndex(e => new { e.ProviderType, e.ProviderId, e.ProviderManagerId, e.WorkDate, e.Kind }).IsUnique().HasDatabaseName("UX_AttendanceEvent_Key");
    }
}

public class PunchConfiguration : IEntityTypeConfiguration<Punch>
{
    public void Configure(EntityTypeBuilder<Punch> b)
    {
        b.ToTable("Punch");
        b.HasKey(e => e.Id);
        b.Property(e => e.Direction).IsRequired().HasMaxLength(10);
        b.Property(e => e.Verdict).IsRequired().HasMaxLength(20);
        b.Property(e => e.Reason).HasMaxLength(30);
        b.Property(e => e.Source).IsRequired().HasMaxLength(20);
        b.Property(e => e.DeviceId).HasMaxLength(200);
        b.Property(e => e.Note).HasMaxLength(500);
        b.Property(e => e.IsMocked).HasDefaultValue(false);
        b.Property(e => e.IsDeleted).HasDefaultValue(false);
        b.Property(e => e.CreatedAt).HasColumnType("datetime");
        b.Property(e => e.ModifiedAt).HasColumnType("datetime");
        b.HasOne(e => e.ProviderManager).WithMany().HasForeignKey(e => e.ProviderManagerId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Punch_ProviderManager");
        b.HasIndex(e => new { e.ProviderManagerId, e.At }).HasDatabaseName("IX_Punch_Worker_At");
        b.HasIndex(e => new { e.Verdict, e.At }).HasDatabaseName("IX_Punch_Verdict_At").HasFilter("[Verdict] <> 'accepted'");
    }
}
