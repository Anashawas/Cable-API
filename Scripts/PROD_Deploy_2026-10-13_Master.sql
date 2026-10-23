/* =============================================================================
   Cable - PRODUCTION database deploy, 2026-10-13
   Target: db_ab1977_cableproduction (SmarterASP) - run in SSMS, connected to PRODUCTION
   -----------------------------------------------------------------------------
   Brings production to the dev schema (db_ab1977_cable) as compared on 2026-10-13
   (tables, columns, indexes, foreign keys, check constraints, views/procedures,
   lookup data). Every step is idempotent: running the file twice changes nothing
   the second time.

   WHAT IT ADDS
     1  UserAccount.PartnerLastLoginAt / PartnerLastSeenAt        (partner adoption)
     2  PlugType id 23 "Type 2 AC (Fast)"                          (apps expect id 23)
     3  StationPremiumSubscription RENAMED to Subscription + Payer + Payment;
        the legacy premium row(s) become Payment rows; BannerDuration.SubscriptionId
     4  UserRate index + 3 checks, UserComplaints status check   (dev has them, prod not)
     5-7, 9-10  Cable Connect (OCPP): 10 tables + ChargingPoint live-sharing / alert columns
     8  Price alerts: TouTariff (+4 windows seeded), UserPriceAlert, PriceAlertLog,
        NotificationToken.Language, AppSetting quiet hours 23:30-06:30
     11 ProviderManager.Privileges                                (worker permissions)

   BEFORE
     - Take a full backup of db_ab1977_cableproduction (SmarterASP control panel -> MSSQL -> Backup).
     - Have the new API build ready to publish: step 3 renames StationPremiumSubscription,
       so the CURRENT production API loses premium-subscription screens between this
       script and the publish. Run the script, then publish the API right away.

   AFTER
     - The last result set (VERIFY) must show every row = OK.
     - Publish API (+ admin + partner) to production, then the Cable.Ocpp switch (go-live checklist section 2).

   NOT INCLUDED ON PURPOSE
     - Test / content data that differs on dev: loyalty boosts, notification templates,
       service categories, partner terms text, system versions (production values are newer).
     - UserAccount.OriginalCity / OriginalCountry exist only on production - left untouched.
     - Scripts/IncreaseSharedLinkMaxUsage_2026-09.sql - a one-off data change with its own
       dry run; 125 links created since Sep 1 are still below the cap. Run it separately if wanted.
   ============================================================================= */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

-- Safety: stop unless connected to the production database (or the local rehearsal copy).
DECLARE @db SYSNAME = DB_NAME();
IF @db NOT IN (N'db_ab1977_cableproduction', N'CableProdRehearsal')
BEGIN
    RAISERROR('Wrong database: %s. Connect to db_ab1977_cableproduction and run again. Nothing was changed.', 16, 1, @db) WITH NOWAIT;
    SET NOEXEC ON;   -- every following batch is compiled, not executed
END
GO

/* ------------------------------------------------------------------------------
   STEP 1: Partner adoption columns
   Source: Scripts/PartnerAdoption_Phase1.sql
   ------------------------------------------------------------------------------ */
/* =============================================================================
   Cable — Partner app adoption (Sprint 5 follow-up)
   -----------------------------------------------------------------------------
   Adds two partner-scoped activity columns to dbo.UserAccount:
     PartnerLastLoginAt  — last sign-in via the provider app or partner web
     PartnerLastSeenAt   — last authenticated request from either of those

   LastLoginAt / LastSeenAt already exist but are written by every client,
   consumer app included, and most owners are also drivers — so they cannot
   say whether the owner uses the PARTNER app. Both new columns start NULL and
   fill as owners sign in / act; "has ever used the app" is meanwhile answered
   by the existing ProviderSecurityStamp / ProviderWebSecurityStamp columns.

   Idempotent. Additive, zero downtime. Run on dev first, then production.
   ============================================================================= */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF COL_LENGTH('dbo.UserAccount', 'PartnerLastLoginAt') IS NULL
BEGIN
    ALTER TABLE dbo.UserAccount ADD PartnerLastLoginAt DATETIME NULL;
    PRINT 'Added UserAccount.PartnerLastLoginAt';
END ELSE PRINT 'UserAccount.PartnerLastLoginAt exists - skipped';

IF COL_LENGTH('dbo.UserAccount', 'PartnerLastSeenAt') IS NULL
BEGIN
    ALTER TABLE dbo.UserAccount ADD PartnerLastSeenAt DATETIME NULL;
    PRINT 'Added UserAccount.PartnerLastSeenAt';
END ELSE PRINT 'UserAccount.PartnerLastSeenAt exists - skipped';
GO

/* ------------------------------------------------------------------------------
   STEP 2: Plug type 23 "Type 2 AC (Fast)"
   Source: Scripts/PlugType_Type2AcFast_Seed.sql
   Production checked 2026-10-13: IDENT_CURRENT = 21, id 23 and serial absent.
   ------------------------------------------------------------------------------ */
/* =============================================================================
   Cable — Seed plug type "Type 2 AC (Fast)"  (Sprint 5 §6)
   -----------------------------------------------------------------------------
   The Flutter apps map plug types to icons/labels BY NUMERIC ID and already ship
   with this plug hard-coded as id 23. The next identity value on both dev and
   production is 22, so a plain INSERT would land on 22 and the plug would never
   render in either app. The id is therefore forced to 23 with IDENTITY_INSERT.
   The resulting gap at 22 is harmless — ids 3, 4 and 5 are already gaps.

   Idempotent: skips if id 23 or the serial 'AC TYPE 2 FAST' already exists.
   Verified 2026-09-22: dev and production both at IDENT_CURRENT = 21, id 23
   free, serial absent.

   Run on dev first, then production.
   ============================================================================= */

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Id     INT           = 23;
DECLARE @Name   NVARCHAR(100) = N'Type 2 AC (Fast)';
DECLARE @Serial NVARCHAR(100) = N'AC TYPE 2 FAST';
DECLARE @Family NVARCHAR(50)  = N'EURO';

IF EXISTS (SELECT 1 FROM dbo.PlugType WHERE Id = @Id)
BEGIN
    PRINT CONCAT('PlugType id ', @Id, ' already exists - skipped.');
    SELECT Id, Name, SerialNumber, PlugTypeFamily, IsDeleted FROM dbo.PlugType WHERE Id = @Id;
END
ELSE IF EXISTS (SELECT 1 FROM dbo.PlugType WHERE SerialNumber = @Serial AND IsDeleted = 0)
BEGIN
    PRINT CONCAT('Serial ''', @Serial, ''' already exists under a different id - NOT inserted. Review:');
    SELECT Id, Name, SerialNumber, PlugTypeFamily, IsDeleted FROM dbo.PlugType WHERE SerialNumber = @Serial;
END
ELSE
BEGIN
    BEGIN TRAN;
    SET IDENTITY_INSERT dbo.PlugType ON;

    INSERT INTO dbo.PlugType (Id, Name, SerialNumber, PlugTypeFamily, IsDeleted, CreatedAt, CreatedBy)
    VALUES (@Id, @Name, @Serial, @Family, 0, GETUTCDATE(), NULL);

    SET IDENTITY_INSERT dbo.PlugType OFF;
    COMMIT TRAN;

    PRINT CONCAT('Inserted PlugType id ', @Id, ': ', @Name, ' [', @Serial, ']');
    SELECT Id, Name, SerialNumber, PlugTypeFamily, IsDeleted FROM dbo.PlugType WHERE Id = @Id;
END
GO

/* ------------------------------------------------------------------------------
   STEP 3: Subscriptions + payments (RENAMES StationPremiumSubscription)
   Source: Scripts/Subscriptions_Phase1.sql
   Production checked 2026-10-13: 1 premium row, no NULL dates, no duplicate station.
   ------------------------------------------------------------------------------ */
/* =============================================================================
   Cable — Payment tracking, Phase 1
   -----------------------------------------------------------------------------
   Unified subscription + offline-payment model for station premium, banner
   runs and (later) service-provider premium.

   - dbo.StationPremiumSubscription is RENAMED to dbo.Subscription and
     generalised (EntityType/EntityId, plan, grace, admin switch).
   - dbo.Payer and dbo.Payment are created.
   - Every legacy premium row becomes one Payment (method Cash, payer = the
     station owner where known) so history is preserved, not copied.
   - Legacy money columns are dropped from Subscription once migrated.
   - dbo.BannerDuration gains SubscriptionId to link paid runs.

   Idempotent per step; each step re-checks its own precondition.
   QUOTED_IDENTIFIER must be ON (filtered indexes).
   Run on dev first, then production.
   ============================================================================= */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- 1) Payer ---------------------------------------------------------------------
IF OBJECT_ID(N'dbo.Payer', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Payer (
        Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Payer PRIMARY KEY,
        UserAccountId INT            NULL,
        Name          NVARCHAR(200)  NULL,
        Phone         NVARCHAR(50)   NULL,
        Email         NVARCHAR(200)  NULL,
        HasWhatsApp   BIT            NOT NULL CONSTRAINT DF_Payer_HasWhatsApp DEFAULT 0,
        OptOut        BIT            NOT NULL CONSTRAINT DF_Payer_OptOut DEFAULT 0,
        Note          NVARCHAR(500)  NULL,
        IsDeleted     BIT            NOT NULL CONSTRAINT DF_Payer_IsDeleted DEFAULT 0,
        CreatedBy     INT            NULL,
        CreatedAt     DATETIME       NOT NULL CONSTRAINT DF_Payer_CreatedAt DEFAULT GETUTCDATE(),
        ModifiedBy    INT            NULL,
        ModifiedAt    DATETIME       NULL,
        CONSTRAINT FK_Payer_UserAccount FOREIGN KEY (UserAccountId) REFERENCES dbo.UserAccount(Id)
    );
    CREATE UNIQUE INDEX UX_Payer_UserAccount ON dbo.Payer(UserAccountId) WHERE UserAccountId IS NOT NULL AND IsDeleted = 0;
    CREATE INDEX IX_Payer_Phone ON dbo.Payer(Phone);
    PRINT 'Created dbo.Payer';
END ELSE PRINT 'dbo.Payer exists - skipped';
GO

-- 2) Rename StationPremiumSubscription -> Subscription ------------------------
IF OBJECT_ID(N'dbo.StationPremiumSubscription', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Subscription', N'U') IS NULL
BEGIN
    EXEC sp_rename N'dbo.StationPremiumSubscription', N'Subscription';
    PRINT 'Renamed StationPremiumSubscription -> Subscription';
END
IF OBJECT_ID(N'dbo.Subscription', N'U') IS NULL
BEGIN
    -- Fresh database with no legacy table at all.
    CREATE TABLE dbo.Subscription (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Subscription PRIMARY KEY,
        Note NVARCHAR(1000) NULL,
        IsDeleted BIT NOT NULL CONSTRAINT DF_Subscription_IsDeleted DEFAULT 0,
        CreatedBy INT NULL, CreatedAt DATETIME NOT NULL CONSTRAINT DF_Subscription_CreatedAt DEFAULT GETUTCDATE(),
        ModifiedBy INT NULL, ModifiedAt DATETIME NULL
    );
    PRINT 'Created dbo.Subscription';
END
GO

-- 3) New columns on Subscription (each guarded) ------------------------------
IF COL_LENGTH('dbo.Subscription','EntityType') IS NULL         ALTER TABLE dbo.Subscription ADD EntityType NVARCHAR(50) NULL;
IF COL_LENGTH('dbo.Subscription','EntityId') IS NULL           ALTER TABLE dbo.Subscription ADD EntityId INT NULL;
IF COL_LENGTH('dbo.Subscription','PlanMonths') IS NULL         ALTER TABLE dbo.Subscription ADD PlanMonths INT NULL;
IF COL_LENGTH('dbo.Subscription','StartDate') IS NULL          ALTER TABLE dbo.Subscription ADD StartDate DATETIME NULL;
IF COL_LENGTH('dbo.Subscription','ExpiresAt') IS NULL          ALTER TABLE dbo.Subscription ADD ExpiresAt DATETIME NULL;
IF COL_LENGTH('dbo.Subscription','GraceMode') IS NULL          ALTER TABLE dbo.Subscription ADD GraceMode INT NULL;
IF COL_LENGTH('dbo.Subscription','GraceDays') IS NULL          ALTER TABLE dbo.Subscription ADD GraceDays INT NULL;
IF COL_LENGTH('dbo.Subscription','IsSwitchedOff') IS NULL      ALTER TABLE dbo.Subscription ADD IsSwitchedOff BIT NOT NULL CONSTRAINT DF_Subscription_IsSwitchedOff DEFAULT 0;
IF COL_LENGTH('dbo.Subscription','SwitchedOffAt') IS NULL      ALTER TABLE dbo.Subscription ADD SwitchedOffAt DATETIME NULL;
IF COL_LENGTH('dbo.Subscription','SwitchedOffByUserId') IS NULL ALTER TABLE dbo.Subscription ADD SwitchedOffByUserId INT NULL;
GO

-- 4) Backfill from legacy columns (only while they still exist) --------------
IF COL_LENGTH('dbo.Subscription','ChargingPointId') IS NOT NULL
BEGIN
    EXEC sp_executesql N'
        UPDATE dbo.Subscription
        SET EntityType = N''StationPremium'',
            EntityId   = ChargingPointId,
            StartDate  = ISNULL(StartDate, PaymentDate)
        WHERE EntityType IS NULL OR EntityId IS NULL OR StartDate IS NULL;';
    PRINT 'Backfilled EntityType/EntityId/StartDate from legacy columns';
END
GO

-- 5) Payment -----------------------------------------------------------------
IF OBJECT_ID(N'dbo.Payment', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Payment (
        Id                       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Payment PRIMARY KEY,
        SubscriptionId           INT            NOT NULL,
        PayerId                  INT            NOT NULL,
        Amount                   DECIMAL(18,3)  NOT NULL,
        Currency                 NVARCHAR(3)    NOT NULL CONSTRAINT DF_Payment_Currency DEFAULT N'JOD',
        Method                   INT            NOT NULL,   -- 1 CliQ, 2 Cash
        PaidDate                 DATETIME       NOT NULL,
        PeriodStart              DATETIME       NOT NULL,
        PeriodEnd                DATETIME       NOT NULL,
        ReferenceNo              NVARCHAR(40)   NOT NULL,
        Note                     NVARCHAR(1000) NULL,
        ReceiptImageFileName     NVARCHAR(260)  NULL,
        GeneratedReceiptFileName NVARCHAR(260)  NULL,
        IsVoid                   BIT            NOT NULL CONSTRAINT DF_Payment_IsVoid DEFAULT 0,
        VoidReason               NVARCHAR(500)  NULL,
        VoidedAt                 DATETIME       NULL,
        VoidedByUserId           INT            NULL,
        IsDeleted                BIT            NOT NULL CONSTRAINT DF_Payment_IsDeleted DEFAULT 0,
        CreatedBy                INT            NULL,
        CreatedAt                DATETIME       NOT NULL CONSTRAINT DF_Payment_CreatedAt DEFAULT GETUTCDATE(),
        ModifiedBy               INT            NULL,
        ModifiedAt               DATETIME       NULL,
        CONSTRAINT FK_Payment_Subscription FOREIGN KEY (SubscriptionId) REFERENCES dbo.Subscription(Id),
        CONSTRAINT FK_Payment_Payer        FOREIGN KEY (PayerId)        REFERENCES dbo.Payer(Id),
        CONSTRAINT CK_Payment_Method       CHECK (Method IN (1, 2)),
        CONSTRAINT CK_Payment_Period       CHECK (PeriodEnd > PeriodStart)
    );
    CREATE UNIQUE INDEX UX_Payment_ReferenceNo ON dbo.Payment(ReferenceNo);
    CREATE INDEX IX_Payment_Subscription_PaidDate ON dbo.Payment(SubscriptionId, PaidDate);
    CREATE INDEX IX_Payment_PaidDate ON dbo.Payment(PaidDate);
    PRINT 'Created dbo.Payment';
END ELSE PRINT 'dbo.Payment exists - skipped';
GO

-- 6) Migrate legacy premium rows into Payment --------------------------------
IF COL_LENGTH('dbo.Subscription','PaymentDate') IS NOT NULL
BEGIN
    EXEC sp_executesql N'
        -- one payer per owning user
        INSERT INTO dbo.Payer (UserAccountId, HasWhatsApp, OptOut, IsDeleted, CreatedAt)
        SELECT DISTINCT cp.OwnerId, 0, 0, 0, GETUTCDATE()
        FROM dbo.Subscription s
        JOIN dbo.ChargingPoint cp ON cp.Id = s.EntityId
        WHERE s.EntityType = N''StationPremium'' AND cp.OwnerId IS NOT NULL
          AND NOT EXISTS (SELECT 1 FROM dbo.Payer p WHERE p.UserAccountId = cp.OwnerId AND p.IsDeleted = 0)
          AND NOT EXISTS (SELECT 1 FROM dbo.Payment pm WHERE pm.SubscriptionId = s.Id);

        -- a single placeholder for ownerless stations
        IF NOT EXISTS (SELECT 1 FROM dbo.Payer WHERE Name = N''Unknown (legacy premium)'' AND UserAccountId IS NULL)
            INSERT INTO dbo.Payer (Name, HasWhatsApp, OptOut, IsDeleted, CreatedAt)
            VALUES (N''Unknown (legacy premium)'', 0, 0, 0, GETUTCDATE());

        INSERT INTO dbo.Payment (SubscriptionId, PayerId, Amount, Currency, Method, PaidDate, PeriodStart, PeriodEnd,
                                 ReferenceNo, Note, IsVoid, IsDeleted, CreatedBy, CreatedAt)
        SELECT s.Id,
               COALESCE((SELECT TOP 1 p.Id FROM dbo.Payer p WHERE p.UserAccountId = cp.OwnerId AND p.IsDeleted = 0),
                        (SELECT TOP 1 p.Id FROM dbo.Payer p WHERE p.Name = N''Unknown (legacy premium)'' AND p.UserAccountId IS NULL)),
               ISNULL(s.Amount, 0), N''JOD'', 2, s.PaymentDate, s.PaymentDate, s.ExpiresAt,
               CONCAT(N''RCP-LEGACY-'', RIGHT(CONCAT(N''000000'', s.Id), 6)),
               s.Note, 0, s.IsDeleted, s.CreatedBy, s.CreatedAt
        FROM dbo.Subscription s
        LEFT JOIN dbo.ChargingPoint cp ON cp.Id = s.EntityId
        WHERE s.EntityType = N''StationPremium''
          AND s.ExpiresAt > s.PaymentDate
          AND NOT EXISTS (SELECT 1 FROM dbo.Payment pm WHERE pm.SubscriptionId = s.Id);';
    PRINT 'Migrated legacy premium rows into Payment';
END
GO

-- 7) Drop legacy money columns + old FK/index; tighten new columns ----------
DECLARE @sql NVARCHAR(MAX) = N'';
SELECT @sql += N'ALTER TABLE dbo.Subscription DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';'
FROM sys.foreign_keys fk WHERE fk.parent_object_id = OBJECT_ID(N'dbo.Subscription')
  AND fk.referenced_object_id = OBJECT_ID(N'dbo.ChargingPoint');
SELECT @sql += N'DROP INDEX ' + QUOTENAME(i.name) + N' ON dbo.Subscription;'
FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE i.object_id = OBJECT_ID(N'dbo.Subscription') AND c.name = N'ChargingPointId' AND i.is_primary_key = 0;
IF @sql <> N'' EXEC sp_executesql @sql;

IF COL_LENGTH('dbo.Subscription','ChargingPointId') IS NOT NULL ALTER TABLE dbo.Subscription DROP COLUMN ChargingPointId;
IF COL_LENGTH('dbo.Subscription','PaymentDate')     IS NOT NULL ALTER TABLE dbo.Subscription DROP COLUMN PaymentDate;
IF COL_LENGTH('dbo.Subscription','Amount')          IS NOT NULL ALTER TABLE dbo.Subscription DROP COLUMN Amount;
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Subscription') AND name = N'EntityType' AND is_nullable = 1)
BEGIN
    ALTER TABLE dbo.Subscription ALTER COLUMN EntityType NVARCHAR(50) NOT NULL;
    ALTER TABLE dbo.Subscription ALTER COLUMN EntityId INT NOT NULL;
    ALTER TABLE dbo.Subscription ALTER COLUMN StartDate DATETIME NOT NULL;
    ALTER TABLE dbo.Subscription ALTER COLUMN ExpiresAt DATETIME NOT NULL;
    PRINT 'Tightened Subscription columns to NOT NULL';
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Subscription') AND name = N'UX_Subscription_Entity')
    CREATE UNIQUE INDEX UX_Subscription_Entity ON dbo.Subscription(EntityType, EntityId) WHERE IsDeleted = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Subscription') AND name = N'IX_Subscription_ExpiresAt')
    CREATE INDEX IX_Subscription_ExpiresAt ON dbo.Subscription(ExpiresAt);
GO

-- 8) BannerDuration link ------------------------------------------------------
IF COL_LENGTH('dbo.BannerDuration','SubscriptionId') IS NULL
BEGIN
    ALTER TABLE dbo.BannerDuration ADD SubscriptionId INT NULL;
    PRINT 'Added BannerDuration.SubscriptionId';
END
GO

PRINT 'Subscriptions_Phase1 complete';
SELECT (SELECT COUNT(*) FROM dbo.Subscription) AS Subscriptions,
       (SELECT COUNT(*) FROM dbo.Payment)      AS Payments,
       (SELECT COUNT(*) FROM dbo.Payer)        AS Payers;
GO

/* ------------------------------------------------------------------------------
   STEP 4: rating + complaint constraints that dev has and production lacks
   UserRate was created on production by the Aug 22 deploy with the index keyed
   (Rating, UserId) and without the checks; dev has IX_UserRate_UserId (UserId)
   INCLUDE (Rating) and CK_UserRate_*. Production UserRate is empty and complaint
   statuses are 0..3 (checked 2026-10-13), so the checks apply cleanly.
   ------------------------------------------------------------------------------ */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.UserRate') AND name = N'IX_UserRate_UserId')
   AND NOT EXISTS (SELECT 1 FROM sys.indexes i
                   JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                   JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                   WHERE i.object_id = OBJECT_ID(N'dbo.UserRate') AND i.name = N'IX_UserRate_UserId'
                     AND c.name = N'Rating' AND ic.is_included_column = 1)
BEGIN
    DROP INDEX IX_UserRate_UserId ON dbo.UserRate;
    PRINT 'Dropped old IX_UserRate_UserId (Rating, UserId)';
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.UserRate') AND name = N'IX_UserRate_UserId')
BEGIN
    CREATE INDEX IX_UserRate_UserId ON dbo.UserRate(UserId) INCLUDE (Rating) WHERE IsDeleted = 0;
    PRINT 'Created IX_UserRate_UserId (UserId) INCLUDE (Rating)';
END ELSE PRINT 'IX_UserRate_UserId already in dev shape - skipped';
GO
IF OBJECT_ID(N'dbo.CK_UserRate_ProviderType', N'C') IS NULL
    ALTER TABLE dbo.UserRate WITH CHECK ADD CONSTRAINT CK_UserRate_ProviderType CHECK (ProviderType IN (N'ServiceProvider', N'ChargingPoint'));
IF OBJECT_ID(N'dbo.CK_UserRate_Rating', N'C') IS NULL
    ALTER TABLE dbo.UserRate WITH CHECK ADD CONSTRAINT CK_UserRate_Rating CHECK (Rating BETWEEN 1 AND 5);
IF OBJECT_ID(N'dbo.CK_UserRate_NoSelfRating', N'C') IS NULL
    ALTER TABLE dbo.UserRate WITH CHECK ADD CONSTRAINT CK_UserRate_NoSelfRating CHECK (UserId <> RatedByUserId);
IF OBJECT_ID(N'dbo.CK_UserComplaints_Status_Range', N'C') IS NULL
    ALTER TABLE dbo.UserComplaints WITH CHECK ADD CONSTRAINT CK_UserComplaints_Status_Range CHECK (Status >= 0 AND Status <= 6);
PRINT 'UserRate / UserComplaints checks in place';
GO

/* ------------------------------------------------------------------------------
   STEP 5: Cable Connect phase 0 (raw log, process starts)
   Source: Scripts/OcppConnect_Phase0.sql
   ------------------------------------------------------------------------------ */
-- Cable Connect (OCPP 1.6J) — Phase 0: connection test + host-recycle measurement.
-- Idempotent. Apply to dev first, then to production before the real-charger test.
-- Phase B1 adds OcppChargePoint / OcppConnector / OcppTransaction / OcppMeterValue.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.OcppRawMessage', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OcppRawMessage
    (
        Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OcppRawMessage PRIMARY KEY,
        ChargePointId NVARCHAR(40)   NOT NULL,   -- the id in the URL, whatever the charger sent
        Direction     CHAR(3)        NOT NULL,   -- 'in' | 'out' | 'sys' (connect/disconnect events)
        MessageType   TINYINT        NULL,       -- 2 CALL | 3 CALLRESULT | 4 CALLERROR
        MessageId     NVARCHAR(64)   NULL,
        [Action]      NVARCHAR(64)   NULL,
        Payload       NVARCHAR(MAX)  NULL,       -- the full frame as received / sent
        RemoteIp      NVARCHAR(64)   NULL,
        CreatedAt     DATETIME2(3)   NOT NULL    -- arrival time (UTC); the charger's own timestamp lives inside Payload
    );
    CREATE INDEX IX_OcppRawMessage_ChargePoint_CreatedAt ON dbo.OcppRawMessage (ChargePointId, CreatedAt DESC);
    PRINT 'Created dbo.OcppRawMessage';
END
ELSE PRINT 'SKIP dbo.OcppRawMessage exists';
GO

IF OBJECT_ID('dbo.OcppProcessStart', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OcppProcessStart
    (
        Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OcppProcessStart PRIMARY KEY,
        StartedAt       DATETIME2(0)  NOT NULL,   -- UTC
        MachineName     NVARCHAR(100) NULL,
        ProcessId       INT           NULL,
        EnvironmentName NVARCHAR(30)  NULL,
        Version         NVARCHAR(30)  NULL
    );
    PRINT 'Created dbo.OcppProcessStart';
END
ELSE PRINT 'SKIP dbo.OcppProcessStart exists';
GO

-- Handy while watching the pilot:
-- SELECT TOP 200 * FROM dbo.OcppRawMessage ORDER BY Id DESC;
-- SELECT StartedAt, DATEDIFF(MINUTE, LAG(StartedAt) OVER (ORDER BY StartedAt), StartedAt) AS MinutesSincePrevious FROM dbo.OcppProcessStart ORDER BY StartedAt DESC;
GO

/* ------------------------------------------------------------------------------
   STEP 6: Cable Connect phase 1 (chargers, plugs, sessions, meter values, cards)
   Source: Scripts/OcppConnect_Phase1.sql
   ------------------------------------------------------------------------------ */
-- Cable Connect (OCPP 1.6J) — Phase B1: the business tables.
-- Requires OcppConnect_Phase0.sql (OcppRawMessage, OcppProcessStart). Idempotent.
-- Apply to dev first, then production before the first real session is recorded.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.OcppChargePoint', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OcppChargePoint
    (
        Id                    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OcppChargePoint PRIMARY KEY,
        ChargingPointId       INT            NOT NULL,
        ChargePointId         NVARCHAR(40)   NOT NULL,   -- the id in the WebSocket URL
        PasswordHash          NVARCHAR(500)  NULL,       -- NULL = no Basic auth (Security Profile 0)
        DisplayName           NVARCHAR(100)  NULL,
        Vendor                NVARCHAR(100)  NULL,       -- from BootNotification
        Model                 NVARCHAR(100)  NULL,
        FirmwareVersion       NVARCHAR(100)  NULL,
        SerialNumber          NVARCHAR(100)  NULL,
        ChargeBoxSerialNumber NVARCHAR(100)  NULL,
        Iccid                 NVARCHAR(40)   NULL,
        Imsi                  NVARCHAR(40)   NULL,
        MeterSerialNumber     NVARCHAR(100)  NULL,
        HeartbeatInterval     INT            NOT NULL CONSTRAINT DF_OcppChargePoint_HeartbeatInterval DEFAULT 60,
        IsEnabled             BIT            NOT NULL CONSTRAINT DF_OcppChargePoint_IsEnabled DEFAULT 1,
        IsConnected           BIT            NOT NULL CONSTRAINT DF_OcppChargePoint_IsConnected DEFAULT 0,
        ConnectedAt           DATETIME2(3)   NULL,
        DisconnectedAt        DATETIME2(3)   NULL,
        LastBootAt            DATETIME2(3)   NULL,
        LastMessageAt         DATETIME2(3)   NULL,
        LastRemoteIp          NVARCHAR(64)   NULL,
        FailedAuthCount       INT            NOT NULL CONSTRAINT DF_OcppChargePoint_FailedAuthCount DEFAULT 0,
        LockedUntil           DATETIME2(0)   NULL,
        CreatedBy             INT            NULL,
        CreatedAt             DATETIME       NOT NULL,
        ModifiedBy            INT            NULL,
        ModifiedAt            DATETIME       NULL,
        IsDeleted             BIT            NOT NULL CONSTRAINT DF_OcppChargePoint_IsDeleted DEFAULT 0,
        CONSTRAINT FK_OcppChargePoint_ChargingPoint FOREIGN KEY (ChargingPointId) REFERENCES dbo.ChargingPoint (Id)
    );
    CREATE UNIQUE INDEX UX_OcppChargePoint_ChargePointId ON dbo.OcppChargePoint (ChargePointId) WHERE [IsDeleted] = 0;
    CREATE INDEX IX_OcppChargePoint_ChargingPoint ON dbo.OcppChargePoint (ChargingPointId);
    PRINT 'Created dbo.OcppChargePoint';
END
ELSE PRINT 'SKIP dbo.OcppChargePoint exists';
GO

IF OBJECT_ID('dbo.OcppConnector', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OcppConnector
    (
        Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OcppConnector PRIMARY KEY,
        OcppChargePointId INT            NOT NULL,
        ConnectorId       INT            NOT NULL,   -- 0 = the charger itself, 1..n = plugs
        Status            NVARCHAR(30)   NOT NULL,   -- Available | Preparing | Charging | SuspendedEVSE | SuspendedEV | Finishing | Reserved | Unavailable | Faulted
        ErrorCode         NVARCHAR(50)   NOT NULL,
        VendorErrorCode   NVARCHAR(50)   NULL,
        Info              NVARCHAR(500)  NULL,
        StatusUpdatedAt   DATETIME2(3)   NULL,       -- charger time (R1)
        StatusReceivedAt  DATETIME2(3)   NOT NULL,   -- arrival time (R7)
        PlugTypeId        INT            NULL,
        PowerKw           DECIMAL(8,2)   NULL,
        CONSTRAINT FK_OcppConnector_OcppChargePoint FOREIGN KEY (OcppChargePointId) REFERENCES dbo.OcppChargePoint (Id) ON DELETE CASCADE,
        CONSTRAINT FK_OcppConnector_PlugType FOREIGN KEY (PlugTypeId) REFERENCES dbo.PlugType (Id) ON DELETE SET NULL
    );
    CREATE UNIQUE INDEX UX_OcppConnector_ChargePoint_Connector ON dbo.OcppConnector (OcppChargePointId, ConnectorId);
    PRINT 'Created dbo.OcppConnector';
END
ELSE PRINT 'SKIP dbo.OcppConnector exists';
GO

IF OBJECT_ID('dbo.OcppTransaction', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OcppTransaction
    (
        Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OcppTransaction PRIMARY KEY,  -- = the OCPP transactionId
        OcppChargePointId INT            NOT NULL,
        ConnectorId       INT            NOT NULL,
        IdTag             NVARCHAR(50)   NOT NULL,
        MeterStartWh      BIGINT         NOT NULL,
        MeterStopWh       BIGINT         NULL,
        StartedAt         DATETIME2(3)   NOT NULL,   -- charger time
        StoppedAt         DATETIME2(3)   NULL,
        ReceivedStartAt   DATETIME2(3)   NOT NULL,
        ReceivedStopAt    DATETIME2(3)   NULL,
        StopReason        NVARCHAR(30)   NULL,
        EnergyKwh         DECIMAL(12,3)  NULL,       -- (stop - start) / 1000
        IsOpen            BIT            NOT NULL CONSTRAINT DF_OcppTransaction_IsOpen DEFAULT 1,
        IsStale           BIT            NOT NULL CONSTRAINT DF_OcppTransaction_IsStale DEFAULT 0,
        IsOrphan          BIT            NOT NULL CONSTRAINT DF_OcppTransaction_IsOrphan DEFAULT 0,
        WasRejected       BIT            NOT NULL CONSTRAINT DF_OcppTransaction_WasRejected DEFAULT 0,
        CONSTRAINT FK_OcppTransaction_OcppChargePoint FOREIGN KEY (OcppChargePointId) REFERENCES dbo.OcppChargePoint (Id)
    );
    CREATE INDEX IX_OcppTransaction_ChargePoint_StartedAt ON dbo.OcppTransaction (OcppChargePointId, StartedAt DESC);
    CREATE INDEX IX_OcppTransaction_Open ON dbo.OcppTransaction (IsOpen) WHERE [IsOpen] = 1;
    PRINT 'Created dbo.OcppTransaction';
END
ELSE PRINT 'SKIP dbo.OcppTransaction exists';
GO

IF OBJECT_ID('dbo.OcppMeterValue', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OcppMeterValue
    (
        Id                BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OcppMeterValue PRIMARY KEY,
        OcppChargePointId INT            NOT NULL,
        ConnectorId       INT            NOT NULL,
        OcppTransactionId INT            NULL,
        MeasuredAt        DATETIME2(3)   NOT NULL,   -- charger time (R1)
        ReceivedAt        DATETIME2(3)   NOT NULL,
        Context           NVARCHAR(30)   NULL,       -- Sample.Periodic | Transaction.Begin | Transaction.End | ...
        EnergyWh          BIGINT         NULL,       -- cumulative register
        PowerW            INT            NULL,
        CurrentA          DECIMAL(9,2)   NULL,
        VoltageV          DECIMAL(9,2)   NULL,
        SocPercent        INT            NULL,
        TemperatureC      DECIMAL(6,1)   NULL,
        CONSTRAINT FK_OcppMeterValue_OcppChargePoint FOREIGN KEY (OcppChargePointId) REFERENCES dbo.OcppChargePoint (Id),
        CONSTRAINT FK_OcppMeterValue_OcppTransaction FOREIGN KEY (OcppTransactionId) REFERENCES dbo.OcppTransaction (Id)
    );
    -- R3: replayed samples after an outage must not duplicate.
    CREATE UNIQUE INDEX UX_OcppMeterValue_ChargePoint_Transaction_MeasuredAt ON dbo.OcppMeterValue (OcppChargePointId, OcppTransactionId, MeasuredAt);
    CREATE INDEX IX_OcppMeterValue_Transaction_MeasuredAt ON dbo.OcppMeterValue (OcppTransactionId, MeasuredAt);
    PRINT 'Created dbo.OcppMeterValue';
END
ELSE PRINT 'SKIP dbo.OcppMeterValue exists';
GO

IF OBJECT_ID('dbo.OcppAuthorizedTag', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OcppAuthorizedTag
    (
        Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OcppAuthorizedTag PRIMARY KEY,
        ChargingPointId INT            NOT NULL,
        IdTag           NVARCHAR(50)   NOT NULL,   -- normalised: trimmed, upper-case
        Label           NVARCHAR(100)  NULL,
        IsEnabled       BIT            NOT NULL CONSTRAINT DF_OcppAuthorizedTag_IsEnabled DEFAULT 1,
        ExpiresAt       DATETIME2(0)   NULL,
        CreatedBy       INT            NULL,
        CreatedAt       DATETIME       NOT NULL,
        ModifiedBy      INT            NULL,
        ModifiedAt      DATETIME       NULL,
        IsDeleted       BIT            NOT NULL CONSTRAINT DF_OcppAuthorizedTag_IsDeleted DEFAULT 0,
        CONSTRAINT FK_OcppAuthorizedTag_ChargingPoint FOREIGN KEY (ChargingPointId) REFERENCES dbo.ChargingPoint (Id) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX UX_OcppAuthorizedTag_Station_Tag ON dbo.OcppAuthorizedTag (ChargingPointId, IdTag) WHERE [IsDeleted] = 0;
    PRINT 'Created dbo.OcppAuthorizedTag';
END
ELSE PRINT 'SKIP dbo.OcppAuthorizedTag exists';
GO

/* ------------------------------------------------------------------------------
   STEP 7: Cable Connect phase 2 (commands, alerts, card links, live sharing, reliability)
   Source: Scripts/OcppConnect_Phase2.sql
   Needs Subscription (step 3) for the live-sharing backfill.
   ------------------------------------------------------------------------------ */
-- Cable Connect (OCPP 1.6J) — Phase 2: remote control.
-- Audit trail of central-system commands (Reset, UnlockConnector, ChangeAvailability,
-- TriggerMessage, GetConfiguration, …). Requires OcppConnect_Phase1.sql. Idempotent.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.OcppCommand', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OcppCommand
    (
        Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OcppCommand PRIMARY KEY,
        OcppChargePointId INT            NOT NULL,
        Action            NVARCHAR(64)   NOT NULL,   -- OCPP action, e.g. Reset
        RequestPayload    NVARCHAR(MAX)  NOT NULL,   -- CALL payload as sent
        Status            NVARCHAR(20)   NOT NULL,   -- Answered | CallError | NotConnected | Timeout | Disconnected | Invalid | Unreachable
        ResultStatus      NVARCHAR(40)   NULL,       -- the unit's status word: Accepted | Rejected | Unlocked | Scheduled | ...
        ResponsePayload   NVARCHAR(MAX)  NULL,       -- CALLRESULT payload when answered
        ErrorCode         NVARCHAR(64)   NULL,
        ErrorDescription  NVARCHAR(500)  NULL,
        DurationMs        INT            NULL,
        CompletedAt       DATETIME2(3)   NULL,
        CreatedBy         INT            NULL,       -- the admin / owner who asked
        CreatedAt         DATETIME       NOT NULL,
        ModifiedBy        INT            NULL,
        ModifiedAt        DATETIME       NULL,
        IsDeleted         BIT            NOT NULL CONSTRAINT DF_OcppCommand_IsDeleted DEFAULT 0,
        CONSTRAINT FK_OcppCommand_OcppChargePoint FOREIGN KEY (OcppChargePointId) REFERENCES dbo.OcppChargePoint (Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_OcppCommand_ChargePoint_CreatedAt ON dbo.OcppCommand (OcppChargePointId, CreatedAt DESC);
    PRINT 'Created dbo.OcppCommand';
END
ELSE PRINT 'SKIP dbo.OcppCommand exists';
GO

-- Local authorization list sync state (SendLocalList) on the charger row.
IF COL_LENGTH('dbo.OcppChargePoint', 'LocalListVersion') IS NULL
BEGIN
    ALTER TABLE dbo.OcppChargePoint ADD
        LocalListVersion  INT          NULL,       -- version pushed last (Unix seconds of the push)
        LocalListSyncedAt DATETIME2(3) NULL,
        LocalListStatus   NVARCHAR(20) NULL;       -- Synced | Pending | Failed | NotSupported; NULL = never attempted
    PRINT 'Added OcppChargePoint.LocalList* columns';
END
ELSE PRINT 'SKIP OcppChargePoint.LocalList* columns exist';
GO

-- Alert rules (charger offline / plug Faulted / session open too long): one open row per target.
IF OBJECT_ID('dbo.OcppAlert', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OcppAlert
    (
        Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OcppAlert PRIMARY KEY,
        OcppChargePointId INT            NOT NULL,
        Type              NVARCHAR(30)   NOT NULL,   -- ChargerOffline | ConnectorFaulted | SessionTooLong
        ConnectorId       INT            NULL,       -- plug for connector/session alerts
        OcppTransactionId INT            NULL,
        ConditionSince    DATETIME2(3)   NOT NULL,   -- when the condition began, not when noticed
        NotifiedAt        DATETIME2(3)   NOT NULL,
        ResolvedAt        DATETIME2(3)   NULL,       -- NULL = still open
        Details           NVARCHAR(300)  NULL,
        Recipients        INT            NOT NULL CONSTRAINT DF_OcppAlert_Recipients DEFAULT 0,
        CreatedBy         INT            NULL,
        CreatedAt         DATETIME       NOT NULL,
        ModifiedBy        INT            NULL,
        ModifiedAt        DATETIME       NULL,
        IsDeleted         BIT            NOT NULL CONSTRAINT DF_OcppAlert_IsDeleted DEFAULT 0,
        CONSTRAINT FK_OcppAlert_OcppChargePoint FOREIGN KEY (OcppChargePointId) REFERENCES dbo.OcppChargePoint (Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_OcppAlert_Target_Open ON dbo.OcppAlert (OcppChargePointId, Type, ConnectorId, ResolvedAt);
    PRINT 'Created dbo.OcppAlert';
END
ELSE PRINT 'SKIP dbo.OcppAlert exists';
GO

-- idTag → Cable user (driver pushes, automatic loyalty points, later remote start/stop from the app).
IF OBJECT_ID('dbo.OcppUserIdTag', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OcppUserIdTag
    (
        Id         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OcppUserIdTag PRIMARY KEY,
        UserId     INT            NOT NULL,
        IdTag      NVARCHAR(50)   NOT NULL,   -- normalised: trimmed, upper-case
        Label      NVARCHAR(100)  NULL,
        IsEnabled  BIT            NOT NULL CONSTRAINT DF_OcppUserIdTag_IsEnabled DEFAULT 1,
        CreatedBy  INT            NULL,
        CreatedAt  DATETIME       NOT NULL,
        ModifiedBy INT            NULL,
        ModifiedAt DATETIME       NULL,
        IsDeleted  BIT            NOT NULL CONSTRAINT DF_OcppUserIdTag_IsDeleted DEFAULT 0,
        CONSTRAINT FK_OcppUserIdTag_UserAccount FOREIGN KEY (UserId) REFERENCES dbo.UserAccount (Id)
    );
    CREATE UNIQUE INDEX UX_OcppUserIdTag_IdTag ON dbo.OcppUserIdTag (IdTag) WHERE [IsDeleted] = 0;
    CREATE INDEX IX_OcppUserIdTag_User ON dbo.OcppUserIdTag (UserId);
    PRINT 'Created dbo.OcppUserIdTag';
END
ELSE PRINT 'SKIP dbo.OcppUserIdTag exists';
GO

-- Parked-after-charging alert: two stages (driver, then station).
IF COL_LENGTH('dbo.OcppAlert', 'EscalatedAt') IS NULL
BEGIN
    ALTER TABLE dbo.OcppAlert ADD
        EscalatedAt  DATETIME2(3) NULL,   -- when the station owner / managers were told
        DriverUserId INT          NULL;   -- the driver told first (OcppUserIdTag), when known
    PRINT 'Added OcppAlert.EscalatedAt / DriverUserId';
END
ELSE PRINT 'SKIP OcppAlert.EscalatedAt exists';
GO

-- N-2: live-data sharing consent (owner) + admin veto on the station.
IF COL_LENGTH('dbo.ChargingPoint', 'ShareLiveStatus') IS NULL
BEGIN
    ALTER TABLE dbo.ChargingPoint ADD
        ShareLiveStatus            BIT           NOT NULL CONSTRAINT DF_ChargingPoint_ShareLiveStatus DEFAULT 0,  -- owner's consent; set ON at first OcppConnect activation
        ShareLiveStatusSetAt       DATETIME2(0)  NULL,     -- when the owner last decided (NULL = never; activation may set it ON)
        ShareLiveStatusSetByUserId INT           NULL,
        LiveStatusBlocked          BIT           NOT NULL CONSTRAINT DF_ChargingPoint_LiveStatusBlocked DEFAULT 0, -- admin veto
        LiveStatusBlockedAt        DATETIME2(0)  NULL,
        LiveStatusBlockedByUserId  INT           NULL,
        LiveStatusBlockReason      NVARCHAR(300) NULL;
    PRINT 'Added ChargingPoint.ShareLiveStatus / LiveStatusBlocked';
END
ELSE PRINT 'SKIP ChargingPoint.ShareLiveStatus exists';
GO

-- Backfill: stations that already hold a Cable Connect subscription start with sharing ON
-- (the owner has not decided yet; the activation hook only runs on new payments).
UPDATE cp SET cp.ShareLiveStatus = 1
FROM dbo.ChargingPoint cp
WHERE cp.ShareLiveStatus = 0 AND cp.ShareLiveStatusSetAt IS NULL
  AND EXISTS (SELECT 1 FROM dbo.Subscription s WHERE s.EntityType = 'OcppConnect' AND s.EntityId = cp.Id AND s.IsDeleted = 0);
GO

-- N-6: reliability score per charger (daily job).
IF COL_LENGTH('dbo.OcppChargePoint', 'ReliabilityPct') IS NULL
BEGIN
    ALTER TABLE dbo.OcppChargePoint ADD
        ReliabilityPct              DECIMAL(5,2) NULL,   -- minutes reachable AND fault-free / counted minutes, last 30 days
        ReliabilityOnlinePct        DECIMAL(5,2) NULL,
        ReliabilityFaultFreePct     DECIMAL(5,2) NULL,
        ReliabilityOfflineIncidents INT          NULL,
        ReliabilityFaultIncidents   INT          NULL,
        ReliabilityComputedAt       DATETIME2(0) NULL;
    PRINT 'Added OcppChargePoint.Reliability* columns';
END
ELSE PRINT 'SKIP OcppChargePoint.Reliability* columns exist';
GO

/* ------------------------------------------------------------------------------
   STEP 8: Price alerts (TOU tariff seeded, quiet hours)
   Source: Scripts/PriceAlerts_Phase1.sql
   Before phase 3: session prices use this tariff.
   ------------------------------------------------------------------------------ */
-- Price alerts (time-of-use tariff → push before a window starts). Idempotent.
-- Apply to dev first, then production with the API build that contains PricingRoutes.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.TouTariff', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TouTariff
    (
        Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TouTariff PRIMARY KEY,
        Version       INT           NOT NULL,                 -- bumps on every edit; the app reschedules on change
        EffectiveFrom DATETIME2(0)  NOT NULL,                 -- UTC, like every other datetime; the API converts to Asia/Amman on output
        Timezone      NVARCHAR(40)  NOT NULL CONSTRAINT DF_TouTariff_Timezone DEFAULT 'Asia/Amman',
        Currency      NVARCHAR(3)   NOT NULL CONSTRAINT DF_TouTariff_Currency DEFAULT 'JOD',
        Unit          NVARCHAR(20)  NOT NULL CONSTRAINT DF_TouTariff_Unit DEFAULT 'fils/kWh',
        IsActive      BIT           NOT NULL CONSTRAINT DF_TouTariff_IsActive DEFAULT 1,
        Note          NVARCHAR(300) NULL,
        CreatedBy     INT           NULL,
        CreatedAt     DATETIME      NOT NULL,
        ModifiedBy    INT           NULL,
        ModifiedAt    DATETIME      NULL,
        IsDeleted     BIT           NOT NULL CONSTRAINT DF_TouTariff_IsDeleted DEFAULT 0
    );
    PRINT 'Created dbo.TouTariff';
END
ELSE PRINT 'SKIP dbo.TouTariff exists';
GO

IF OBJECT_ID('dbo.TouTariffWindow', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TouTariffWindow
    (
        Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TouTariffWindow PRIMARY KEY,
        TouTariffId INT          NOT NULL,
        [Key]       NVARCHAR(30) NOT NULL,   -- stable id user preferences are stored against; never renamed
        NameEn      NVARCHAR(60) NOT NULL,
        NameAr      NVARCHAR(60) NOT NULL,
        StartMin    INT          NOT NULL,   -- minutes from midnight, Asia/Amman
        EndMin      INT          NOT NULL,   -- > 1440 when the window crosses midnight
        Tier        NVARCHAR(20) NOT NULL,   -- offPeak | partial | peak
        PriceFils   INT          NOT NULL,
        SortOrder   INT          NOT NULL,
        CONSTRAINT FK_TouTariffWindow_TouTariff FOREIGN KEY (TouTariffId) REFERENCES dbo.TouTariff (Id) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX UX_TouTariffWindow_Tariff_Key ON dbo.TouTariffWindow (TouTariffId, [Key]);
    PRINT 'Created dbo.TouTariffWindow';
END
ELSE PRINT 'SKIP dbo.TouTariffWindow exists';
GO

-- Seed: the tariff the app ships today (station boards, incl. commission). Version 1.
IF NOT EXISTS (SELECT 1 FROM dbo.TouTariff WHERE IsDeleted = 0)
BEGIN
    INSERT INTO dbo.TouTariff (Version, EffectiveFrom, Timezone, Currency, Unit, IsActive, Note, CreatedAt)
    VALUES (1, '2026-03-31T21:00:00', 'Asia/Amman', 'JOD', 'fils/kWh', 1, 'Moved from the app (time_of_use_pricing.dart) unchanged', GETUTCDATE());   -- UTC = 2026-04-01 00:00 Asia/Amman; the API presents Jordan time
    DECLARE @t INT = SCOPE_IDENTITY();
    INSERT INTO dbo.TouTariffWindow (TouTariffId, [Key], NameEn, NameAr, StartMin, EndMin, Tier, PriceFils, SortOrder) VALUES
        (@t, 'offPeak', 'Off-peak',     N'خارج الذروة', 300,  840,  'offPeak', 183, 1),
        (@t, 'midday',  'Partial peak', N'ذروة جزئية',  840,  1020, 'partial', 193, 2),
        (@t, 'peak',    'Peak',         N'الذروة',      1020, 1380, 'peak',    213, 3),
        (@t, 'night',   'Partial peak', N'ذروة جزئية',  1380, 1740, 'partial', 193, 4);
    PRINT 'Seeded TouTariff v1 with 4 windows';
END
ELSE PRINT 'SKIP TouTariff already seeded';
GO

IF OBJECT_ID('dbo.UserPriceAlert', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserPriceAlert
    (
        UserAccountId INT           NOT NULL CONSTRAINT PK_UserPriceAlert PRIMARY KEY,
        IsEnabled     BIT           NOT NULL CONSTRAINT DF_UserPriceAlert_IsEnabled DEFAULT 0,
        LeadMinutes   INT           NOT NULL CONSTRAINT DF_UserPriceAlert_LeadMinutes DEFAULT 15,   -- 15 | 30 | 45 | 60
        Windows       NVARCHAR(200) NOT NULL CONSTRAINT DF_UserPriceAlert_Windows DEFAULT '',       -- CSV of window keys
        UpdatedAt     DATETIME2(0)  NOT NULL,
        CONSTRAINT FK_UserPriceAlert_UserAccount FOREIGN KEY (UserAccountId) REFERENCES dbo.UserAccount (Id) ON DELETE CASCADE
    );
    PRINT 'Created dbo.UserPriceAlert';
END
ELSE PRINT 'SKIP dbo.UserPriceAlert exists';
GO

IF OBJECT_ID('dbo.PriceAlertLog', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PriceAlertLog
    (
        Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PriceAlertLog PRIMARY KEY,
        UserAccountId INT          NOT NULL,
        WindowKey     NVARCHAR(30) NOT NULL,
        AlertDate     DATE         NOT NULL,   -- Jordan date of the window start
        SentAt        DATETIME2(0) NOT NULL,
        LeadMinutes   INT          NOT NULL,
        Language      NVARCHAR(5)  NULL
    );
    CREATE UNIQUE INDEX UX_PriceAlertLog_User_Window_Date ON dbo.PriceAlertLog (UserAccountId, WindowKey, AlertDate);
    PRINT 'Created dbo.PriceAlertLog';
END
ELSE PRINT 'SKIP dbo.PriceAlertLog exists';
GO

-- The device's language, so a push sent by a job (no request, no Accept-Language) can be localised.
IF COL_LENGTH('dbo.NotificationToken', 'Language') IS NULL
BEGIN
    ALTER TABLE dbo.NotificationToken ADD Language NVARCHAR(5) NULL;
    PRINT 'Added NotificationToken.Language';
END
ELSE PRINT 'SKIP NotificationToken.Language exists';
GO

-- Quiet hours (Asia/Amman, HH:mm): an alert that falls inside is cancelled, not delayed.
IF NOT EXISTS (SELECT 1 FROM dbo.AppSetting WHERE [Key] = 'PriceAlerts.QuietFrom' AND IsDeleted = 0)
    INSERT INTO dbo.AppSetting ([Key], Value, CreatedAt, IsDeleted) VALUES ('PriceAlerts.QuietFrom', '23:30', GETUTCDATE(), 0);
IF NOT EXISTS (SELECT 1 FROM dbo.AppSetting WHERE [Key] = 'PriceAlerts.QuietTo' AND IsDeleted = 0)
    INSERT INTO dbo.AppSetting ([Key], Value, CreatedAt, IsDeleted) VALUES ('PriceAlerts.QuietTo', '06:30', GETUTCDATE(), 0);
GO

/* ------------------------------------------------------------------------------
   STEP 9: Cable Connect phase 3 (start without a card, session price)
   Source: Scripts/OcppConnect_Phase3.sql
   ------------------------------------------------------------------------------ */
-- Cable Connect (OCPP 1.6J) — Phase 3: start without a card + session price.
-- OcppTransaction learns who started the session and how (Card | App | Operator),
-- and carries the price computed from the time-of-use tariff (PriceAlerts_Phase1.sql).
-- Requires OcppConnect_Phase2.sql and PriceAlerts_Phase1.sql. Idempotent.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('dbo.OcppTransaction', 'StartSource') IS NULL
BEGIN
    ALTER TABLE dbo.OcppTransaction ADD
        StartSource       NVARCHAR(10)  NOT NULL CONSTRAINT DF_OcppTransaction_StartSource DEFAULT 'Card', -- Card | App | Operator
        StartedByUserId   INT           NULL,          -- driver (app, or a card linked in OcppUserIdTag) or the operator who started it
        CostFils          INT           NULL,          -- price from the TOU tariff in force; NULL while open / energy unknown
        TariffVersion     INT           NULL,          -- TouTariff.Version used
        CostBreakdownJson NVARCHAR(MAX) NULL,          -- [{key, kwh, priceFils, fils}]
        PricedAt          DATETIME2(0)  NULL;
    PRINT 'Added OcppTransaction.StartSource / StartedByUserId / Cost*';
END
ELSE PRINT 'SKIP OcppTransaction phase-3 columns exist';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OcppTransaction_StartedBy_StartedAt' AND object_id = OBJECT_ID('dbo.OcppTransaction'))
BEGIN
    CREATE INDEX IX_OcppTransaction_StartedBy_StartedAt ON dbo.OcppTransaction (StartedByUserId, StartedAt DESC) WHERE StartedByUserId IS NOT NULL;
    PRINT 'Created IX_OcppTransaction_StartedBy_StartedAt';
END
GO

-- Backfill the driver link for sessions started with a card that is tied to a user.
UPDATE t SET t.StartedByUserId = u.UserId
FROM dbo.OcppTransaction t
JOIN dbo.OcppUserIdTag u ON u.IdTag = t.IdTag AND u.IsDeleted = 0 AND u.IsEnabled = 1
WHERE t.StartedByUserId IS NULL;
GO

/* ------------------------------------------------------------------------------
   STEP 10: Cable Connect alert thresholds per station
   Source: Scripts/OcppConnect_AlertThresholds.sql
   ------------------------------------------------------------------------------ */
-- Cable Connect (OCPP 1.6J) — per-station alert thresholds.
-- NULL = the platform default (OcppLimits: offline 15 min, plug faulted 15 min,
-- session open 360 min, car parked after charging 20 min). Set by the admin on the
-- station's Cable Connect tab. Requires OcppConnect_Phase2.sql. Idempotent.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('dbo.ChargingPoint', 'OcppOfflineAlertMin') IS NULL
BEGIN
    ALTER TABLE dbo.ChargingPoint ADD
        OcppOfflineAlertMin     INT NULL,   -- charger unreachable / silent this long → alert
        OcppFaultedAlertMin     INT NULL,   -- plug Faulted this long → escalation alert
        OcppLongSessionAlertMin INT NULL,   -- session open this long → alert
        OcppParkedAlertMin      INT NULL;   -- Finishing / SuspendedEV this long → driver (then station)
    PRINT 'Added ChargingPoint.Ocpp*AlertMin';
END
ELSE PRINT 'SKIP ChargingPoint.Ocpp*AlertMin exist';
GO

/* ------------------------------------------------------------------------------
   STEP 11: Worker permissions
   Source: Scripts/Worker_Privileges.sql
   ------------------------------------------------------------------------------ */
-- Partner app: what a worker may see / do, chosen by the owner.
-- ProviderManager.Privileges = comma-separated keys (see Cable.Core WorkerPrivileges);
-- NULL = everything (existing workers keep their current access). Idempotent.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('dbo.ProviderManager', 'Privileges') IS NULL
BEGIN
    ALTER TABLE dbo.ProviderManager ADD Privileges NVARCHAR(500) NULL;
    PRINT 'Added ProviderManager.Privileges';
END
ELSE PRINT 'SKIP ProviderManager.Privileges exists';
GO

/* ------------------------------------------------------------------------------
   VERIFY - every row must say OK
   ------------------------------------------------------------------------------ */
SET NOCOUNT ON;
EXEC sp_executesql N'SELECT Item, CASE WHEN Present = 1 THEN ''OK'' ELSE ''MISSING'' END AS Result FROM (VALUES
  (''UserAccount.PartnerLastLoginAt'', CASE WHEN COL_LENGTH(''dbo.UserAccount'',''PartnerLastLoginAt'') IS NOT NULL THEN 1 ELSE 0 END),
  (''UserAccount.PartnerLastSeenAt'', CASE WHEN COL_LENGTH(''dbo.UserAccount'',''PartnerLastSeenAt'') IS NOT NULL THEN 1 ELSE 0 END),
  (''PlugType 23 Type 2 AC (Fast)'', CASE WHEN EXISTS (SELECT 1 FROM dbo.PlugType WHERE Id = 23 AND SerialNumber = N''AC TYPE 2 FAST'') THEN 1 ELSE 0 END),
  (''Subscription (renamed)'', CASE WHEN OBJECT_ID(''dbo.Subscription'',''U'') IS NOT NULL AND OBJECT_ID(''dbo.StationPremiumSubscription'',''U'') IS NULL THEN 1 ELSE 0 END),
  (''Subscription legacy columns dropped'', CASE WHEN COL_LENGTH(''dbo.Subscription'',''ChargingPointId'') IS NULL AND COL_LENGTH(''dbo.Subscription'',''Amount'') IS NULL THEN 1 ELSE 0 END),
  (''Payer'', CASE WHEN OBJECT_ID(''dbo.Payer'',''U'') IS NOT NULL THEN 1 ELSE 0 END),
  (''Payment (every legacy premium migrated)'', CASE WHEN OBJECT_ID(''dbo.Payment'',''U'') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Subscription s WHERE s.EntityType = N''StationPremium'' AND NOT EXISTS (SELECT 1 FROM dbo.Payment p WHERE p.SubscriptionId = s.Id)) THEN 1 ELSE 0 END),
  (''BannerDuration.SubscriptionId'', CASE WHEN COL_LENGTH(''dbo.BannerDuration'',''SubscriptionId'') IS NOT NULL THEN 1 ELSE 0 END),
  (''IX_UserRate_UserId (dev shape)'', CASE WHEN EXISTS (SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE i.object_id = OBJECT_ID(''dbo.UserRate'') AND i.name = ''IX_UserRate_UserId'' AND c.name = ''Rating'' AND ic.is_included_column = 1) THEN 1 ELSE 0 END),
  (''CK_UserRate_* (3)'', CASE WHEN (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(''dbo.UserRate'') AND name LIKE ''CK_UserRate_%'') = 3 THEN 1 ELSE 0 END),
  (''CK_UserComplaints_Status_Range'', CASE WHEN OBJECT_ID(''dbo.CK_UserComplaints_Status_Range'',''C'') IS NOT NULL THEN 1 ELSE 0 END),
  (''OcppRawMessage'', CASE WHEN OBJECT_ID(''dbo.OcppRawMessage'',''U'') IS NOT NULL THEN 1 ELSE 0 END),
  (''OcppProcessStart'', CASE WHEN OBJECT_ID(''dbo.OcppProcessStart'',''U'') IS NOT NULL THEN 1 ELSE 0 END),
  (''OcppChargePoint (+LocalList, Reliability)'', CASE WHEN COL_LENGTH(''dbo.OcppChargePoint'',''LocalListVersion'') IS NOT NULL AND COL_LENGTH(''dbo.OcppChargePoint'',''ReliabilityPct'') IS NOT NULL THEN 1 ELSE 0 END),
  (''OcppConnector'', CASE WHEN OBJECT_ID(''dbo.OcppConnector'',''U'') IS NOT NULL THEN 1 ELSE 0 END),
  (''OcppTransaction (+StartSource, Cost)'', CASE WHEN COL_LENGTH(''dbo.OcppTransaction'',''StartSource'') IS NOT NULL AND COL_LENGTH(''dbo.OcppTransaction'',''CostFils'') IS NOT NULL THEN 1 ELSE 0 END),
  (''OcppMeterValue'', CASE WHEN OBJECT_ID(''dbo.OcppMeterValue'',''U'') IS NOT NULL THEN 1 ELSE 0 END),
  (''OcppAuthorizedTag'', CASE WHEN OBJECT_ID(''dbo.OcppAuthorizedTag'',''U'') IS NOT NULL THEN 1 ELSE 0 END),
  (''OcppCommand'', CASE WHEN OBJECT_ID(''dbo.OcppCommand'',''U'') IS NOT NULL THEN 1 ELSE 0 END),
  (''OcppAlert (+EscalatedAt)'', CASE WHEN COL_LENGTH(''dbo.OcppAlert'',''EscalatedAt'') IS NOT NULL THEN 1 ELSE 0 END),
  (''OcppUserIdTag'', CASE WHEN OBJECT_ID(''dbo.OcppUserIdTag'',''U'') IS NOT NULL THEN 1 ELSE 0 END),
  (''ChargingPoint.ShareLiveStatus / LiveStatusBlocked'', CASE WHEN COL_LENGTH(''dbo.ChargingPoint'',''ShareLiveStatus'') IS NOT NULL AND COL_LENGTH(''dbo.ChargingPoint'',''LiveStatusBlocked'') IS NOT NULL THEN 1 ELSE 0 END),
  (''ChargingPoint.Ocpp*AlertMin (4)'', CASE WHEN COL_LENGTH(''dbo.ChargingPoint'',''OcppParkedAlertMin'') IS NOT NULL THEN 1 ELSE 0 END),
  (''TouTariff v1 + 4 windows'', CASE WHEN (SELECT COUNT(*) FROM dbo.TouTariffWindow w JOIN dbo.TouTariff t ON t.Id = w.TouTariffId WHERE t.IsActive = 1 AND t.IsDeleted = 0) = 4 THEN 1 ELSE 0 END),
  (''UserPriceAlert'', CASE WHEN OBJECT_ID(''dbo.UserPriceAlert'',''U'') IS NOT NULL THEN 1 ELSE 0 END),
  (''PriceAlertLog'', CASE WHEN OBJECT_ID(''dbo.PriceAlertLog'',''U'') IS NOT NULL THEN 1 ELSE 0 END),
  (''NotificationToken.Language'', CASE WHEN COL_LENGTH(''dbo.NotificationToken'',''Language'') IS NOT NULL THEN 1 ELSE 0 END),
  (''AppSetting PriceAlerts.QuietFrom/To'', CASE WHEN (SELECT COUNT(*) FROM dbo.AppSetting WHERE [Key] IN (''PriceAlerts.QuietFrom'',''PriceAlerts.QuietTo'') AND IsDeleted = 0) = 2 THEN 1 ELSE 0 END),
  (''ProviderManager.Privileges'', CASE WHEN COL_LENGTH(''dbo.ProviderManager'',''Privileges'') IS NOT NULL THEN 1 ELSE 0 END)
) v(Item, Present);';
GO
SET NOEXEC OFF;
GO
-- (the VERIFY result above is the outcome)
GO
