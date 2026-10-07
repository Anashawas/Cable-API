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
