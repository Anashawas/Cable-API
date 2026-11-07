-- Worker tracker, step 4 — attendance reports, manual entry, corrections, jobs (Docs/WorkerTracker-Plan.md §4):
--   * dbo.AttendanceCorrection: every owner correction (field, from → to, note, who, when). Never deleted.
--   * dbo.AttendanceEvent: what the attendance job already did (worker reminders, owner "did not punch" alert, daily
--     summary) so nothing is sent twice.
--   * Notification types: attendance_reminder (worker), attendance_missing (owner / ViewAttendance), attendance_manual
--     (owner, when a supervisor records by hand), attendance_correction (worker), attendance_summary (owner, 21:00).
-- Idempotent: safe to run again. Apply to dev first; deploy the API build of this change only after the script.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.AttendanceCorrection', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AttendanceCorrection
    (
        Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AttendanceCorrection PRIMARY KEY,
        AttendanceDayId   INT           NOT NULL,
        Field             NVARCHAR(20)  NOT NULL,   -- in | out | status | review
        FromValue         NVARCHAR(100) NULL,
        ToValue           NVARCHAR(100) NULL,
        Note              NVARCHAR(500) NOT NULL,
        CorrectedByUserId INT           NOT NULL,
        CorrectedAt       DATETIME2(0)  NOT NULL,
        CreatedBy INT NULL, CreatedAt DATETIME NOT NULL, ModifiedBy INT NULL, ModifiedAt DATETIME NULL,
        IsDeleted         BIT           NOT NULL CONSTRAINT DF_AttendanceCorrection_IsDeleted DEFAULT 0,
        CONSTRAINT FK_AttendanceCorrection_Day FOREIGN KEY (AttendanceDayId) REFERENCES dbo.AttendanceDay (Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_AttendanceCorrection_Day ON dbo.AttendanceCorrection (AttendanceDayId);
    PRINT 'Created dbo.AttendanceCorrection';
END
ELSE PRINT 'SKIP dbo.AttendanceCorrection exists';
GO

IF OBJECT_ID('dbo.AttendanceEvent', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AttendanceEvent
    (
        Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AttendanceEvent PRIMARY KEY,
        ProviderType      NVARCHAR(50) NOT NULL,
        ProviderId        INT          NOT NULL,
        ProviderManagerId INT          NULL,           -- null = a per-station event (daily summary)
        WorkDate          DATE         NOT NULL,
        Kind              NVARCHAR(30) NOT NULL,       -- reminderIn | reminderOut | ownerMissing | dailySummary
        SentAt            DATETIME2(0) NOT NULL,
        CreatedBy INT NULL, CreatedAt DATETIME NOT NULL, ModifiedBy INT NULL, ModifiedAt DATETIME NULL,
        IsDeleted         BIT          NOT NULL CONSTRAINT DF_AttendanceEvent_IsDeleted DEFAULT 0
    );
    CREATE UNIQUE INDEX UX_AttendanceEvent_Key ON dbo.AttendanceEvent (ProviderType, ProviderId, ProviderManagerId, WorkDate, Kind);
    PRINT 'Created dbo.AttendanceEvent';
END
ELSE PRINT 'SKIP dbo.AttendanceEvent exists';
GO

INSERT INTO dbo.NotificationType ([Name], [Description])
SELECT v.Name, v.Description
FROM (VALUES
    ('attendance_reminder',   'Worker: your shift started — punch in / do not forget to punch out'),
    ('attendance_missing',    'Owner and ViewAttendance workers: a worker did not punch in 15 min after the shift start (grouped)'),
    ('attendance_manual',     'Owner: a supervisor started or ended a worker''s shift by hand'),
    ('attendance_correction', 'Worker: the owner corrected one of your attendance days'),
    ('attendance_summary',    'Owner: the day''s attendance summary at 21:00')
) AS v(Name, Description)
WHERE NOT EXISTS (SELECT 1 FROM dbo.NotificationType t WHERE t.Name = v.Name);
PRINT CONCAT('Notification types added: ', @@ROWCOUNT);
GO
