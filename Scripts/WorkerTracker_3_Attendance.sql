-- Worker tracker, step 3 — shifts, public holidays, punch zones, punching (Docs/WorkerTracker-Plan.md §3):
--   * dbo.WorkerShiftSchedule + dbo.WorkerShiftDay: a worker's weekly schedule, versioned by EffectiveFrom.
--   * dbo.PublicHoliday: Jordan public holidays (admin).
--   * dbo.PunchZone + dbo.PunchZoneWorker: circles around the station and the workers they apply to.
--   * dbo.AttendanceDay + dbo.Punch: one row per worker per work date, and every punch / refused attempt.
--   * NotificationType attendance_attempt (owner push on a rejected or review punch).
-- Idempotent: safe to run again. Apply to dev first; deploy the API build of this change only after the script.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.WorkerShiftSchedule', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkerShiftSchedule
    (
        Id                   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WorkerShiftSchedule PRIMARY KEY,
        ProviderManagerId    INT      NOT NULL,
        EffectiveFrom        DATE     NOT NULL,
        EffectiveTo          DATE     NULL,                -- null = current
        GraceMinutes         INT      NOT NULL CONSTRAINT DF_WorkerShiftSchedule_Grace DEFAULT 10,
        EarliestPunchMinutes INT      NOT NULL CONSTRAINT DF_WorkerShiftSchedule_Earliest DEFAULT 60,
        CreatedBy INT NULL, CreatedAt DATETIME NOT NULL, ModifiedBy INT NULL, ModifiedAt DATETIME NULL,
        IsDeleted            BIT      NOT NULL CONSTRAINT DF_WorkerShiftSchedule_IsDeleted DEFAULT 0,
        CONSTRAINT FK_WorkerShiftSchedule_ProviderManager FOREIGN KEY (ProviderManagerId) REFERENCES dbo.ProviderManager (Id)
    );
    CREATE INDEX IX_WorkerShiftSchedule_Worker_From ON dbo.WorkerShiftSchedule (ProviderManagerId, EffectiveFrom) WHERE [IsDeleted] = 0;
    PRINT 'Created dbo.WorkerShiftSchedule';
END
ELSE PRINT 'SKIP dbo.WorkerShiftSchedule exists';
GO

IF OBJECT_ID('dbo.WorkerShiftDay', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkerShiftDay
    (
        Id                    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WorkerShiftDay PRIMARY KEY,
        WorkerShiftScheduleId INT      NOT NULL,
        DayOfWeek             INT      NOT NULL,           -- 0 = Sunday … 6 = Saturday
        StartTime             TIME(0)  NULL,
        EndTime               TIME(0)  NULL,               -- <= StartTime = crosses midnight
        IsOff                 BIT      NOT NULL CONSTRAINT DF_WorkerShiftDay_IsOff DEFAULT 0,
        CreatedBy INT NULL, CreatedAt DATETIME NOT NULL, ModifiedBy INT NULL, ModifiedAt DATETIME NULL,
        IsDeleted             BIT      NOT NULL CONSTRAINT DF_WorkerShiftDay_IsDeleted DEFAULT 0,
        CONSTRAINT FK_WorkerShiftDay_Schedule FOREIGN KEY (WorkerShiftScheduleId) REFERENCES dbo.WorkerShiftSchedule (Id) ON DELETE CASCADE
    );
    PRINT 'Created dbo.WorkerShiftDay';
END
ELSE PRINT 'SKIP dbo.WorkerShiftDay exists';
GO

IF OBJECT_ID('dbo.PublicHoliday', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PublicHoliday
    (
        Id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PublicHoliday PRIMARY KEY,
        [Date]    DATE          NOT NULL,
        NameAr    NVARCHAR(200) NOT NULL,
        NameEn    NVARCHAR(200) NOT NULL,
        CreatedBy INT NULL, CreatedAt DATETIME NOT NULL, ModifiedBy INT NULL, ModifiedAt DATETIME NULL,
        IsDeleted BIT           NOT NULL CONSTRAINT DF_PublicHoliday_IsDeleted DEFAULT 0
    );
    CREATE INDEX IX_PublicHoliday_Date ON dbo.PublicHoliday ([Date]);
    PRINT 'Created dbo.PublicHoliday';
END
ELSE PRINT 'SKIP dbo.PublicHoliday exists';
GO

IF OBJECT_ID('dbo.PunchZone', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PunchZone
    (
        Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PunchZone PRIMARY KEY,
        ProviderType NVARCHAR(50)  NOT NULL,
        ProviderId   INT           NOT NULL,
        [Name]       NVARCHAR(200) NOT NULL,
        Lat          FLOAT         NOT NULL,
        Lng          FLOAT         NOT NULL,
        RadiusMeters INT           NOT NULL,              -- 50–800
        IsActive     BIT           NOT NULL CONSTRAINT DF_PunchZone_IsActive DEFAULT 1,
        CreatedBy INT NULL, CreatedAt DATETIME NOT NULL, ModifiedBy INT NULL, ModifiedAt DATETIME NULL,
        IsDeleted    BIT           NOT NULL CONSTRAINT DF_PunchZone_IsDeleted DEFAULT 0
    );
    CREATE INDEX IX_PunchZone_Provider ON dbo.PunchZone (ProviderType, ProviderId) WHERE [IsDeleted] = 0;
    PRINT 'Created dbo.PunchZone';
END
ELSE PRINT 'SKIP dbo.PunchZone exists';
GO

IF OBJECT_ID('dbo.PunchZoneWorker', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PunchZoneWorker
    (
        Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PunchZoneWorker PRIMARY KEY,
        PunchZoneId       INT NOT NULL,
        ProviderManagerId INT NOT NULL,
        CreatedBy INT NULL, CreatedAt DATETIME NOT NULL, ModifiedBy INT NULL, ModifiedAt DATETIME NULL,
        IsDeleted         BIT NOT NULL CONSTRAINT DF_PunchZoneWorker_IsDeleted DEFAULT 0,
        CONSTRAINT FK_PunchZoneWorker_Zone FOREIGN KEY (PunchZoneId) REFERENCES dbo.PunchZone (Id) ON DELETE CASCADE,
        CONSTRAINT FK_PunchZoneWorker_ProviderManager FOREIGN KEY (ProviderManagerId) REFERENCES dbo.ProviderManager (Id)
    );
    CREATE INDEX IX_PunchZoneWorker_Worker ON dbo.PunchZoneWorker (ProviderManagerId) WHERE [IsDeleted] = 0;
    PRINT 'Created dbo.PunchZoneWorker';
END
ELSE PRINT 'SKIP dbo.PunchZoneWorker exists';
GO

IF OBJECT_ID('dbo.AttendanceDay', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AttendanceDay
    (
        Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AttendanceDay PRIMARY KEY,
        ProviderManagerId INT          NOT NULL,
        WorkDate          DATE         NOT NULL,          -- Jordan date of the shift start
        ShiftStartUtc     DATETIME2(0) NULL,
        ShiftEndUtc       DATETIME2(0) NULL,
        FirstInAt         DATETIME2(0) NULL,
        LastOutAt         DATETIME2(0) NULL,
        IsOpen            BIT          NOT NULL CONSTRAINT DF_AttendanceDay_IsOpen DEFAULT 0,   -- last punch is an "in"
        MinutesWorked     INT          NOT NULL CONSTRAINT DF_AttendanceDay_Worked DEFAULT 0,
        LateMinutes       INT          NOT NULL CONSTRAINT DF_AttendanceDay_Late DEFAULT 0,
        ExtraMinutes      INT          NOT NULL CONSTRAINT DF_AttendanceDay_Extra DEFAULT 0,
        [Status]          NVARCHAR(20) NOT NULL,          -- present | missingOut | absent | leave | holiday | off
        Flags             NVARCHAR(200) NULL,             -- late, review, noZone, extra, manual, corrected
        AutoClosedAt      DATETIME2(0) NULL,
        CreatedBy INT NULL, CreatedAt DATETIME NOT NULL, ModifiedBy INT NULL, ModifiedAt DATETIME NULL,
        IsDeleted         BIT          NOT NULL CONSTRAINT DF_AttendanceDay_IsDeleted DEFAULT 0,
        CONSTRAINT FK_AttendanceDay_ProviderManager FOREIGN KEY (ProviderManagerId) REFERENCES dbo.ProviderManager (Id)
    );
    CREATE UNIQUE INDEX UX_AttendanceDay_Worker_Date ON dbo.AttendanceDay (ProviderManagerId, WorkDate) WHERE [IsDeleted] = 0;
    CREATE INDEX IX_AttendanceDay_Worker_Open ON dbo.AttendanceDay (ProviderManagerId, IsOpen) WHERE [IsDeleted] = 0 AND [IsOpen] = 1;
    PRINT 'Created dbo.AttendanceDay';
END
ELSE PRINT 'SKIP dbo.AttendanceDay exists';
GO

IF OBJECT_ID('dbo.Punch', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Punch
    (
        Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Punch PRIMARY KEY,
        AttendanceDayId   INT           NULL,              -- null = a refused attempt
        ProviderManagerId INT           NOT NULL,
        WorkDate          DATE          NOT NULL,
        Direction         NVARCHAR(10)  NOT NULL,          -- in | out
        [At]              DATETIME2(0)  NOT NULL,          -- server time, UTC
        Lat               FLOAT         NULL,              -- scrubbed after 90 days (step 4)
        Lng               FLOAT         NULL,
        AccuracyMeters    INT           NULL,
        IsMocked          BIT           NOT NULL CONSTRAINT DF_Punch_IsMocked DEFAULT 0,
        DeviceId          NVARCHAR(200) NULL,
        PunchZoneId       INT           NULL,
        DistanceMeters    INT           NULL,
        Verdict           NVARCHAR(20)  NOT NULL,          -- accepted | review | rejected
        Reason            NVARCHAR(30)  NULL,              -- lowAccuracy | outsideZone | mocked | borderline | noZone
        Source            NVARCHAR(20)  NOT NULL,          -- app | manual | correction
        RecordedByUserId  INT           NULL,
        Note              NVARCHAR(500) NULL,
        CreatedBy INT NULL, CreatedAt DATETIME NOT NULL, ModifiedBy INT NULL, ModifiedAt DATETIME NULL,
        IsDeleted         BIT           NOT NULL CONSTRAINT DF_Punch_IsDeleted DEFAULT 0,
        CONSTRAINT FK_Punch_AttendanceDay FOREIGN KEY (AttendanceDayId) REFERENCES dbo.AttendanceDay (Id),
        CONSTRAINT FK_Punch_ProviderManager FOREIGN KEY (ProviderManagerId) REFERENCES dbo.ProviderManager (Id)
    );
    CREATE INDEX IX_Punch_Worker_At ON dbo.Punch (ProviderManagerId, [At]);
    CREATE INDEX IX_Punch_Verdict_At ON dbo.Punch (Verdict, [At]) WHERE [Verdict] <> 'accepted';
    PRINT 'Created dbo.Punch';
END
ELSE PRINT 'SKIP dbo.Punch exists';
GO

INSERT INTO dbo.NotificationType ([Name], [Description])
SELECT v.Name, v.Description
FROM (VALUES
    ('attendance_attempt', 'A worker''s punch was refused or needs a look (sent to the owner and ViewAttendance workers)')
) AS v(Name, Description)
WHERE NOT EXISTS (SELECT 1 FROM dbo.NotificationType t WHERE t.Name = v.Name);
PRINT CONCAT('Notification types added: ', @@ROWCOUNT);
GO

SELECT OBJECT_ID('dbo.WorkerShiftSchedule','U') AS Shifts, OBJECT_ID('dbo.PunchZone','U') AS Zones, OBJECT_ID('dbo.AttendanceDay','U') AS Days, OBJECT_ID('dbo.Punch','U') AS Punches;
GO
