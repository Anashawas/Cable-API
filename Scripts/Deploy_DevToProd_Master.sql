/* =============================================================================
   CABLE — MASTER DEV -> PRODUCTION DEPLOYMENT SCRIPT
   =============================================================================
   Generated 2026-07-19 from a LIVE schema + data diff of
       dev  = db_ab1977_cable
       prod = db_ab1977_cableproduction

   Applies EVERY pending change, in dependency order:
     schema (11 new tables, new columns, dropped columns, nullability),
     reference data (Worker role, notification types, platforms, sizes, brands),
     and the test/demo records (App Store review provider + worker3 + demo station).

   HOW TO RUN
     1. BACKUP db_ab1977_cableproduction first.
     2. Run this script on db_ab1977_cableproduction (whole file, SSMS).
     3. Deploy the new API build IMMEDIATELY after (Part 6 drops
        ChargingPoint.ChargerBrand, which the OLD build still reads —
        station lists on the old build break until the new build is live).
     4. Check the verification report at the end of the output.

   Idempotent: safe to re-run. Every statement is guarded.
   ============================================================================= */
PRINT 'Running on database: ' + DB_NAME();
IF DB_NAME() NOT LIKE '%cableproduction%'
    PRINT '*** WARNING: this does not look like the production database! ***';
GO


/* ############################################################################
   PART 1  — Worker role + ProviderManager table
   (source: Scripts/Worker_Phase1_CreateProviderManager.sql)
   ############################################################################ */

/* =============================================================================
   Cable — Worker feature Phase 1
   -----------------------------------------------------------------------------
   - Adds the 'Worker' role
   - Creates dbo.ProviderManager (one worker per provider)

   Idempotent. Run on dev first, then production (additive, zero downtime).
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- 1) Worker role -------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.Role WHERE Name = N'Worker')
BEGIN
    INSERT INTO dbo.Role (Name, IsDeleted, CreatedAt) VALUES (N'Worker', 0, GETDATE());
END;

-- 2) ProviderManager table ---------------------------------------------------
IF OBJECT_ID(N'dbo.ProviderManager', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProviderManager (
        Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProviderManager PRIMARY KEY,
        ProviderType NVARCHAR(50) NOT NULL,        -- "ServiceProvider" | "ChargingPoint"
        ProviderId   INT          NOT NULL,
        UserId       INT          NOT NULL,        -- the Worker-role UserAccount
        IsActive     BIT          NOT NULL CONSTRAINT DF_ProviderManager_IsActive  DEFAULT 1,
        IsDeleted    BIT          NOT NULL CONSTRAINT DF_ProviderManager_IsDeleted DEFAULT 0,
        CreatedBy    INT          NULL,
        CreatedAt    DATETIME     NOT NULL CONSTRAINT DF_ProviderManager_CreatedAt DEFAULT GETDATE(),
        ModifiedBy   INT          NULL,
        ModifiedAt   DATETIME     NULL,
        CONSTRAINT FK_ProviderManager_User
            FOREIGN KEY (UserId) REFERENCES dbo.UserAccount(Id),
        CONSTRAINT CK_ProviderManager_ProviderType
            CHECK (ProviderType IN (N'ServiceProvider', N'ChargingPoint'))
    );

    -- one active worker per provider
    CREATE UNIQUE INDEX UX_ProviderManager_OneWorker
        ON dbo.ProviderManager(ProviderType, ProviderId)
        WHERE IsDeleted = 0;

    -- fast "providers this user works for"
    CREATE INDEX IX_ProviderManager_User
        ON dbo.ProviderManager(UserId)
        INCLUDE (ProviderType, ProviderId, IsActive)
        WHERE IsDeleted = 0;
END;

-- 3) Verify ------------------------------------------------------------------
SELECT (SELECT Id FROM dbo.Role WHERE Name = N'Worker') AS WorkerRoleId,
       (SELECT COUNT(*) FROM sys.tables WHERE name = 'ProviderManager') AS ProviderManagerExists;

/* =============================================================================
   ROLLBACK:
       DROP TABLE IF EXISTS dbo.ProviderManager;
       DELETE FROM dbo.Role WHERE Name = N'Worker';   -- only if no Worker users exist
   ============================================================================= */
GO


/* ############################################################################
   PART 2  — Social media catalog + links
   (source: Scripts/SocialMedia_Phase1_CreateTables.sql)
   ############################################################################ */

/* =============================================================================
   Cable — SocialMedia Phase 1: catalog + per-provider links
   -----------------------------------------------------------------------------
   - Creates dbo.SocialMediaPlatform   (catalog with optional icon)
   - Creates dbo.SocialLink            (per-provider URLs, polymorphic)
   - Seeds 9 common platforms (only if the catalog is empty)

   Idempotent: re-running is safe (IF NOT EXISTS guards everywhere).
   Run order: dev DB first → smoke test → production DB.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- 1) Catalog ------------------------------------------------------------------
IF OBJECT_ID(N'dbo.SocialMediaPlatform', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SocialMediaPlatform (
        Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SocialMediaPlatform PRIMARY KEY,
        Name            NVARCHAR(100)     NOT NULL,
        NameAr          NVARCHAR(100)     NULL,
        IconFileName    NVARCHAR(255)     NULL,
        IconExtension   NVARCHAR(50)      NULL,
        IconContentType NVARCHAR(50)      NULL,
        IconFileSize    BIGINT            NULL,
        DisplayOrder    INT               NOT NULL CONSTRAINT DF_SocialMediaPlatform_Order DEFAULT 0,
        IsActive        BIT               NOT NULL CONSTRAINT DF_SocialMediaPlatform_IsActive DEFAULT 1,
        IsDeleted       BIT               NOT NULL CONSTRAINT DF_SocialMediaPlatform_IsDeleted DEFAULT 0,
        CreatedBy       INT               NULL,
        CreatedAt       DATETIME          NOT NULL CONSTRAINT DF_SocialMediaPlatform_CreatedAt DEFAULT GETDATE(),
        ModifiedBy      INT               NULL,
        ModifiedAt      DATETIME          NULL,
        CONSTRAINT UQ_SocialMediaPlatform_Name UNIQUE (Name)
    );

    CREATE INDEX IX_SocialMediaPlatform_IsActive
        ON dbo.SocialMediaPlatform(IsActive)
        INCLUDE (DisplayOrder, Name)
        WHERE IsDeleted = 0;
END;

-- Idempotent: add NameAr if an earlier version of the table is already present.
IF OBJECT_ID(N'dbo.SocialMediaPlatform', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns
                   WHERE Name = N'NameAr' AND Object_ID = Object_ID(N'dbo.SocialMediaPlatform'))
BEGIN
    ALTER TABLE dbo.SocialMediaPlatform ADD NameAr NVARCHAR(100) NULL;
END;

-- 2) Per-provider links -------------------------------------------------------
IF OBJECT_ID(N'dbo.SocialLink', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SocialLink (
        Id                     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SocialLink PRIMARY KEY,
        ProviderType           NVARCHAR(50)      NOT NULL,
        ProviderId             INT               NOT NULL,
        SocialMediaPlatformId  INT               NOT NULL,
        Url                    NVARCHAR(1000)    NOT NULL,
        DisplayOrder           INT               NOT NULL CONSTRAINT DF_SocialLink_Order DEFAULT 0,
        IsDeleted              BIT               NOT NULL CONSTRAINT DF_SocialLink_IsDeleted DEFAULT 0,
        CreatedBy              INT               NULL,
        CreatedAt              DATETIME          NOT NULL CONSTRAINT DF_SocialLink_CreatedAt DEFAULT GETDATE(),
        ModifiedBy             INT               NULL,
        ModifiedAt             DATETIME          NULL,
        CONSTRAINT FK_SocialLink_Platform
            FOREIGN KEY (SocialMediaPlatformId) REFERENCES dbo.SocialMediaPlatform(Id),
        CONSTRAINT CK_SocialLink_ProviderType
            CHECK (ProviderType IN (N'ServiceProvider', N'ChargingPoint'))
    );

    CREATE INDEX IX_SocialLink_Provider
        ON dbo.SocialLink(ProviderType, ProviderId)
        INCLUDE (SocialMediaPlatformId, Url, DisplayOrder, IsDeleted)
        WHERE IsDeleted = 0;
END;

-- 3) Seed common platforms (only if catalog is empty) ------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.SocialMediaPlatform)
BEGIN
    INSERT INTO dbo.SocialMediaPlatform (Name, NameAr, DisplayOrder) VALUES
        (N'Facebook',  N'فيسبوك',   1),
        (N'Instagram', N'إنستغرام', 2),
        (N'TikTok',    N'تيك توك',  3),
        (N'X',         N'إكس',      4),
        (N'YouTube',   N'يوتيوب',   5),
        (N'WhatsApp',  N'واتساب',   6),
        (N'Telegram',  N'تيليجرام', 7),
        (N'Snapchat',  N'سناب شات', 8),
        (N'LinkedIn',  N'لينكدإن',  9);
END;

-- 4) Verification -------------------------------------------------------------
SELECT 'SocialMediaPlatform' AS T, COUNT(*) AS Rows FROM dbo.SocialMediaPlatform
UNION ALL
SELECT 'SocialLink',          COUNT(*) FROM dbo.SocialLink;

/* =============================================================================
   ROLLBACK (when truly needed):

       DROP TABLE IF EXISTS dbo.SocialLink;
       DROP TABLE IF EXISTS dbo.SocialMediaPlatform;

   Existing ServiceProvider / ChargingPoint rows are untouched by this script.
   ============================================================================= */
GO


/* ############################################################################
   PART 3  — IsTest flags (hide demo/QA records)
   (source: Scripts/IsTest_Flag_And_DemoAccount.sql)
   ############################################################################ */

/* =============================================================================
   Cable — IsTest flag (demo/QA records hidden from public discovery)
   -----------------------------------------------------------------------------
   Adds dbo.ChargingPoint.IsTest and dbo.ServiceProvider.IsTest.

   Records with IsTest = 1 are excluded from the PUBLIC discovery queries
   (GetAllChargingPoints, GetChargingPointsPaged) but remain fully visible to
   their owner (GetMyChargingPoints) and when fetched directly by Id.

   Requires the matching backend build to be deployed — without it the filter
   is not applied and test rows WILL show publicly.

   Idempotent.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE Name = N'IsTest' AND Object_ID = Object_ID(N'dbo.ChargingPoint'))
BEGIN
    ALTER TABLE dbo.ChargingPoint
        ADD IsTest BIT NOT NULL CONSTRAINT DF_ChargingPoint_IsTest DEFAULT 0;
END;

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE Name = N'IsTest' AND Object_ID = Object_ID(N'dbo.ServiceProvider'))
BEGIN
    ALTER TABLE dbo.ServiceProvider
        ADD IsTest BIT NOT NULL CONSTRAINT DF_ServiceProvider_IsTest DEFAULT 0;
END;

SELECT
 (SELECT COUNT(*) FROM sys.columns WHERE Name=N'IsTest' AND Object_ID=Object_ID(N'dbo.ChargingPoint'))   AS CP_IsTest,
 (SELECT COUNT(*) FROM sys.columns WHERE Name=N'IsTest' AND Object_ID=Object_ID(N'dbo.ServiceProvider')) AS SP_IsTest;

-- (demo account + station are created in PART 11 of this master script)
GO


/* ############################################################################
   PART 4  — Analytics engine tables
   (source: Scripts/Analytics_Phase1_CreateTables.sql)
   ############################################################################ */

-- =============================================
-- Analytics Phase 1: Provider & Banner analytics engine
-- Cable EV Charging Station Management
-- =============================================
-- Creates 2 tables:
--   AnalyticsEvent       — append-only raw event log (source of truth)
--   AnalyticsDailyRollup — pre-aggregated daily counters (fast dashboards)
--
-- Polymorphic via (EntityType, EntityId):
--   EntityType in ('ChargingPoint', 'ServiceProvider', 'Banner')
-- EventType maps to Cable.Core.Emuns.AnalyticsEventType:
--   1 FullView, 2 HalfView, 3 CallButtonClick, 4 MapClick,
--   20 BannerView, 21 BannerClick
--
-- Idempotent: safe to re-run.
-- =============================================

-- 1. AnalyticsEvent — append-only event log
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AnalyticsEvent')
BEGIN
    CREATE TABLE [dbo].[AnalyticsEvent] (
        [Id]          INT IDENTITY(1,1) NOT NULL,
        [EntityType]  NVARCHAR(50)      NOT NULL,
        [EntityId]    INT               NOT NULL,
        [EventType]   INT               NOT NULL,
        [UserId]      INT               NULL,
        [AnonymousId] NVARCHAR(100)     NULL,
        [Source]      NVARCHAR(20)      NULL,
        [OccurredAt]  DATETIME          NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_AnalyticsEvent] PRIMARY KEY CLUSTERED ([Id] ASC)
    );

    CREATE NONCLUSTERED INDEX [IX_AnalyticsEvent_Entity_Event_OccurredAt]
        ON [dbo].[AnalyticsEvent] ([EntityType], [EntityId], [EventType], [OccurredAt]);

    CREATE NONCLUSTERED INDEX [IX_AnalyticsEvent_OccurredAt]
        ON [dbo].[AnalyticsEvent] ([OccurredAt]);

    CREATE NONCLUSTERED INDEX [IX_AnalyticsEvent_UserId]
        ON [dbo].[AnalyticsEvent] ([UserId]);

    PRINT 'Created table: AnalyticsEvent';
END
GO

-- 2. AnalyticsDailyRollup — pre-aggregated daily counters
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AnalyticsDailyRollup')
BEGIN
    CREATE TABLE [dbo].[AnalyticsDailyRollup] (
        [Id]          INT IDENTITY(1,1) NOT NULL,
        [EntityType]  NVARCHAR(50)      NOT NULL,
        [EntityId]    INT               NOT NULL,
        [EventType]   INT               NOT NULL,
        [Day]         DATE              NOT NULL,
        [Count]       INT               NOT NULL DEFAULT 0,
        [LastEventAt] DATETIME          NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_AnalyticsDailyRollup] PRIMARY KEY CLUSTERED ([Id] ASC)
    );

    CREATE UNIQUE NONCLUSTERED INDEX [UX_AnalyticsDailyRollup_Entity_Event_Day]
        ON [dbo].[AnalyticsDailyRollup] ([EntityType], [EntityId], [EventType], [Day]);

    PRINT 'Created table: AnalyticsDailyRollup';
END
GO
GO


/* ############################################################################
   PART 5  — Station premium (dates + history)
   (source: Scripts/Premium_Phase1_AddStationPremium.sql)
   ############################################################################ */

-- =============================================
-- Premium Phase 1: Premium station payment & expiry dates
-- Cable EV Charging Station Management
-- =============================================
-- 1. Adds PremiumPaymentDate / PremiumExpiresAt to ChargingPoint
--    (denormalized latest values for fast reads)
-- 2. Creates StationPremiumSubscription — one row per payment/renewal
--    (immutable payment history)
--
-- Premium station = ChargingPoint.StationTypeId = 2 (StationType 'Premium').
-- NOTE: No auto-expiry job yet — dates are informational for now.
--
-- Idempotent: safe to re-run.
-- =============================================

-- 1. Denormalized premium dates on ChargingPoint
IF NOT EXISTS (SELECT * FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.ChargingPoint') AND name = 'PremiumPaymentDate')
BEGIN
    ALTER TABLE [dbo].[ChargingPoint] ADD [PremiumPaymentDate] DATETIME NULL;
    PRINT 'Added column: ChargingPoint.PremiumPaymentDate';
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.ChargingPoint') AND name = 'PremiumExpiresAt')
BEGIN
    ALTER TABLE [dbo].[ChargingPoint] ADD [PremiumExpiresAt] DATETIME NULL;
    PRINT 'Added column: ChargingPoint.PremiumExpiresAt';
END
GO

-- 2. StationPremiumSubscription — payment history
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'StationPremiumSubscription')
BEGIN
    CREATE TABLE [dbo].[StationPremiumSubscription] (
        [Id]              INT IDENTITY(1,1) NOT NULL,
        [ChargingPointId] INT               NOT NULL,
        [PaymentDate]     DATETIME          NOT NULL,
        [ExpiresAt]       DATETIME          NOT NULL,
        [Amount]          DECIMAL(18,3)     NULL,
        [Note]            NVARCHAR(500)     NULL,
        [CreatedAt]       DATETIME          NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy]       INT               NULL,
        [ModifiedAt]      DATETIME          NULL,
        [ModifiedBy]      INT               NULL,
        [IsDeleted]       BIT               NOT NULL DEFAULT 0,
        CONSTRAINT [PK_StationPremiumSubscription] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_StationPremiumSubscription_ChargingPoint]
            FOREIGN KEY ([ChargingPointId]) REFERENCES [dbo].[ChargingPoint] ([Id])
    );

    CREATE NONCLUSTERED INDEX [IX_StationPremiumSubscription_ChargingPoint_PaymentDate]
        ON [dbo].[StationPremiumSubscription] ([ChargingPointId], [PaymentDate]);

    PRINT 'Created table: StationPremiumSubscription';
END
GO
GO


/* ############################################################################
   PART 6  — Lookups: brands M2M (+drop free text), CarType.Icon, CarModelSize, Rate.Comment
   (source: Scripts/Lookups_Phase1_BrandSizeIconReviews.sql)
   ############################################################################ */

-- =============================================
-- Lookups Phase 1: ChargerBrand lookup, CarType icon,
--                  CarModelSize lookup, station reviews
-- Cable EV Charging Station Management
-- =============================================
-- 1. ChargerBrand lookup table (replaces free-text ChargingPoint.ChargerBrand)
--    + ChargingPointChargerBrand junction (M2M with per-brand Count),
--    seeded/backfilled from existing data, then the free-text column is DROPPED
-- 2. CarType.Icon column (logo icon)
-- 3. CarModelSize lookup (SUV, Hatchback, ...) + CarModel.SizeId FK
-- 4. Rate.Comment column (station reviews)
--
-- Idempotent: safe to re-run.
-- =============================================

-- 1a. ChargerBrand lookup table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ChargerBrand')
BEGIN
    CREATE TABLE [dbo].[ChargerBrand] (
        [Id]   INT IDENTITY(1,1) NOT NULL,
        [Name] NVARCHAR(100)     NOT NULL,
        CONSTRAINT [PK_ChargerBrand] PRIMARY KEY CLUSTERED ([Id] ASC)
    );

    CREATE UNIQUE NONCLUSTERED INDEX [UX_ChargerBrand_Name]
        ON [dbo].[ChargerBrand] ([Name]);

    PRINT 'Created table: ChargerBrand';
END
GO

-- 1b. Seed brands from existing distinct free-text values
--     (dynamic SQL: the free-text column is dropped in 1e, so a re-run must not
--      reference it directly or the batch would fail to compile)
IF COL_LENGTH('dbo.ChargingPoint', 'ChargerBrand') IS NOT NULL
EXEC sp_executesql N'
INSERT INTO [dbo].[ChargerBrand] ([Name])
SELECT DISTINCT LTRIM(RTRIM(CP.ChargerBrand))
FROM [dbo].[ChargingPoint] CP
WHERE CP.ChargerBrand IS NOT NULL
  AND LTRIM(RTRIM(CP.ChargerBrand)) <> ''''
  AND NOT EXISTS (SELECT 1 FROM [dbo].[ChargerBrand] B
                  WHERE B.Name = LTRIM(RTRIM(CP.ChargerBrand)));';
GO

-- 1c. Junction table: ChargingPoint <-> ChargerBrand (many-to-many with a
--     per-brand charger Count, e.g. 4x ABB + 3x Teison at one station)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ChargingPointChargerBrand')
BEGIN
    CREATE TABLE [dbo].[ChargingPointChargerBrand] (
        [Id]              INT IDENTITY(1,1) NOT NULL,
        [ChargingPointId] INT NOT NULL,
        [ChargerBrandId]  INT NOT NULL,
        [Count]           INT NOT NULL DEFAULT 1,
        CONSTRAINT [PK_ChargingPointChargerBrand] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_ChargingPointChargerBrand_ChargingPoint]
            FOREIGN KEY ([ChargingPointId]) REFERENCES [dbo].[ChargingPoint] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ChargingPointChargerBrand_ChargerBrand]
            FOREIGN KEY ([ChargerBrandId]) REFERENCES [dbo].[ChargerBrand] ([Id])
    );
    CREATE UNIQUE NONCLUSTERED INDEX [UX_ChargingPointChargerBrand_Point_Brand]
        ON [dbo].[ChargingPointChargerBrand] ([ChargingPointId], [ChargerBrandId]);
    PRINT 'Created table: ChargingPointChargerBrand';
END
GO

-- 1d. Migrate legacy free-text brands into the junction (count = station's
--     ChargersCount, since a single-brand station's chargers are all that brand)
IF COL_LENGTH('dbo.ChargingPoint', 'ChargerBrand') IS NOT NULL
EXEC sp_executesql N'
INSERT INTO [dbo].[ChargingPointChargerBrand] ([ChargingPointId], [ChargerBrandId], [Count])
SELECT CP.Id, B.Id, COALESCE(NULLIF(CP.ChargersCount, 0), 1)
FROM [dbo].[ChargingPoint] CP
JOIN [dbo].[ChargerBrand] B ON B.Name = LTRIM(RTRIM(CP.ChargerBrand))
WHERE CP.ChargerBrand IS NOT NULL
  AND LTRIM(RTRIM(CP.ChargerBrand)) <> ''''
  AND NOT EXISTS (SELECT 1 FROM [dbo].[ChargingPointChargerBrand] J
                  WHERE J.ChargingPointId = CP.Id AND J.ChargerBrandId = B.Id);';
GO

-- 1e. Drop the legacy free-text column — the junction is now the only brand
--     storage. Runs AFTER 1b/1d have preserved its data. (The API no longer
--     reads or writes ChargingPoint.ChargerBrand.)
IF COL_LENGTH('dbo.ChargingPoint', 'ChargerBrand') IS NOT NULL
BEGIN
    ALTER TABLE [dbo].[ChargingPoint] DROP COLUMN [ChargerBrand];
    PRINT 'Dropped column: ChargingPoint.ChargerBrand (junction is source of truth)';
END
GO

-- 2. CarType.Icon
IF NOT EXISTS (SELECT * FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.CarType') AND name = 'Icon')
BEGIN
    ALTER TABLE [dbo].[CarType] ADD [Icon] NVARCHAR(500) NULL;
    PRINT 'Added column: CarType.Icon';
END
GO

-- 3a. CarModelSize lookup table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CarModelSize')
BEGIN
    CREATE TABLE [dbo].[CarModelSize] (
        [Id]   INT IDENTITY(1,1) NOT NULL,
        [Name] NVARCHAR(50)      NOT NULL,
        CONSTRAINT [PK_CarModelSize] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
    PRINT 'Created table: CarModelSize';
END
GO

-- 3b. Seed sizes
INSERT INTO [dbo].[CarModelSize] ([Name])
SELECT V.Name
FROM (VALUES ('SUV'), ('Hatchback'), ('Sedan'), ('Crossover'), ('Coupe'), ('Pickup'), ('Van')) AS V(Name)
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[CarModelSize] S WHERE S.Name = V.Name);
GO

-- 3c. CarModel.SizeId FK
IF NOT EXISTS (SELECT * FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.CarModel') AND name = 'SizeId')
BEGIN
    ALTER TABLE [dbo].[CarModel] ADD [SizeId] INT NULL;
    PRINT 'Added column: CarModel.SizeId';
END
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_CarModel_CarModelSize')
BEGIN
    ALTER TABLE [dbo].[CarModel]
        ADD CONSTRAINT [FK_CarModel_CarModelSize]
        FOREIGN KEY ([SizeId]) REFERENCES [dbo].[CarModelSize] ([Id]);
    PRINT 'Added FK: FK_CarModel_CarModelSize';
END
GO

-- 4. Rate.Comment (station reviews)
IF NOT EXISTS (SELECT * FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.Rate') AND name = 'Comment')
BEGIN
    ALTER TABLE [dbo].[Rate] ADD [Comment] NVARCHAR(1000) NULL;
    PRINT 'Added column: Rate.Comment';
END
GO
GO


/* ############################################################################
   PART 7  — Nullable owners + wallet RecordedBy
   (source: Scripts/NullableOwner_Phase1.sql)
   ############################################################################ */

-- =============================================
-- Nullable Owner Phase 1: unassignable providers
-- Cable EV Charging Station Management
-- =============================================
-- Makes three columns nullable so stations/providers can be "unassigned":
--   ChargingPoint.OwnerId                       (null = unassigned station)
--   ServiceProvider.OwnerId                     (null = unassigned provider)
--   ProviderWalletTransaction.RecordedByUserId  (null = system entry on unassigned provider)
-- FKs and indexes are dropped and recreated around each ALTER.
--
-- Idempotent: safe to re-run.
-- =============================================

-- 1. ChargingPoint.OwnerId -> NULL
IF COLUMNPROPERTY(OBJECT_ID('dbo.ChargingPoint'), 'OwnerId', 'AllowsNull') = 0
BEGIN
    ALTER TABLE [dbo].[ChargingPoint] DROP CONSTRAINT [FK_ChargingPoint_UserAccount];
    ALTER TABLE [dbo].[ChargingPoint] ALTER COLUMN [OwnerId] INT NULL;
    ALTER TABLE [dbo].[ChargingPoint]
        ADD CONSTRAINT [FK_ChargingPoint_UserAccount]
        FOREIGN KEY ([OwnerId]) REFERENCES [dbo].[UserAccount] ([Id]);
    PRINT 'ChargingPoint.OwnerId is now nullable';
END
GO

-- 2. ServiceProvider.OwnerId -> NULL
IF COLUMNPROPERTY(OBJECT_ID('dbo.ServiceProvider'), 'OwnerId', 'AllowsNull') = 0
BEGIN
    ALTER TABLE [dbo].[ServiceProvider] DROP CONSTRAINT [FK_ServiceProvider_UserAccount];
    DROP INDEX [IX_ServiceProvider_OwnerId] ON [dbo].[ServiceProvider];
    ALTER TABLE [dbo].[ServiceProvider] ALTER COLUMN [OwnerId] INT NULL;
    CREATE NONCLUSTERED INDEX [IX_ServiceProvider_OwnerId] ON [dbo].[ServiceProvider] ([OwnerId]);
    ALTER TABLE [dbo].[ServiceProvider]
        ADD CONSTRAINT [FK_ServiceProvider_UserAccount]
        FOREIGN KEY ([OwnerId]) REFERENCES [dbo].[UserAccount] ([Id]);
    PRINT 'ServiceProvider.OwnerId is now nullable';
END
GO

-- 3. ProviderWalletTransaction.RecordedByUserId -> NULL
IF COLUMNPROPERTY(OBJECT_ID('dbo.ProviderWalletTransaction'), 'RecordedByUserId', 'AllowsNull') = 0
BEGIN
    ALTER TABLE [dbo].[ProviderWalletTransaction] DROP CONSTRAINT [FK_ProviderWalletTransaction_RecordedByUser];
    DROP INDEX [IX_ProviderWalletTransaction_RecordedByUser] ON [dbo].[ProviderWalletTransaction];
    ALTER TABLE [dbo].[ProviderWalletTransaction] ALTER COLUMN [RecordedByUserId] INT NULL;
    CREATE NONCLUSTERED INDEX [IX_ProviderWalletTransaction_RecordedByUser]
        ON [dbo].[ProviderWalletTransaction] ([RecordedByUserId]);
    ALTER TABLE [dbo].[ProviderWalletTransaction]
        ADD CONSTRAINT [FK_ProviderWalletTransaction_RecordedByUser]
        FOREIGN KEY ([RecordedByUserId]) REFERENCES [dbo].[UserAccount] ([Id]);
    PRINT 'ProviderWalletTransaction.RecordedByUserId is now nullable';
END
GO
GO


/* ############################################################################
   PART 8  — Offers PointsPriceValue
   (source: Scripts/Offers_Phase1_PointsPriceValue.sql)
   ############################################################################ */

-- =============================================
-- Offers Phase 1: pointsPriceValue (B1, BE notes 2026-07-07)
-- Cable EV Charging Station Management
-- =============================================
-- Adds ProviderOffer.PointsPriceValue — the cash value of the offer's points
-- (pointsCost ÷ conversion rate), distinct from MonetaryValue (Cable's payout
-- to the provider). Backfills existing offers from the active conversion rate
-- for the offer's currency (default rate preferred).
--
-- Idempotent: safe to re-run.
-- =============================================

IF NOT EXISTS (SELECT * FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.ProviderOffer') AND name = 'PointsPriceValue')
BEGIN
    ALTER TABLE [dbo].[ProviderOffer] ADD [PointsPriceValue] DECIMAL(18,3) NULL;
    PRINT 'Added column: ProviderOffer.PointsPriceValue';
END
GO

-- Backfill from the active conversion rate for each offer's currency
UPDATE O
SET O.PointsPriceValue = ROUND(O.PointsCost / R.PointsPerUnit, 3)
FROM [dbo].[ProviderOffer] O
CROSS APPLY (
    SELECT TOP 1 PointsPerUnit
    FROM [dbo].[PointsConversionRate] CR
    WHERE CR.IsActive = 1 AND CR.IsDeleted = 0 AND CR.CurrencyCode = O.CurrencyCode
          AND CR.PointsPerUnit > 0
    ORDER BY CR.IsDefault DESC
) R
WHERE O.PointsPriceValue IS NULL;
GO
GO


/* ############################################################################
   PART 9  — Edit-station requests: whitelist + snapshot
   (source: Scripts/EditStationRequest_Phase1.sql)
   ############################################################################ */

-- =============================================
-- Edit Station Request Phase 1: whitelist + diff snapshot
-- Cable EV Charging Station Management
-- =============================================
-- Per EDIT_STATION_REQUEST_BE_REQUIREMENTS.md (R1 + R2):
-- 1. ChargingPointUpdateRequest.StatusId       — owner may request open/closed change
-- 2. ChargingPointUpdateRequest.OldValuesJson  — snapshot of the station's values
--    for the changed fields, taken at SUBMIT time (diff baseline for review)
-- 3. DROP ChargerPointTypeId / StationTypeId / HasOffer — removed from the
--    owner-editable whitelist (admin-only via UpdateChargingPoint)
--
-- Run on db_ab1977_cableproduction BEFORE deploying the build that contains
-- the edit-station-request changes. Idempotent: safe to re-run.
-- (Both databases have no historical update-request rows, so the column drops
--  lose no data.)
-- =============================================

-- 1. StatusId
IF NOT EXISTS (SELECT * FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.ChargingPointUpdateRequest') AND name = 'StatusId')
BEGIN
    ALTER TABLE [dbo].[ChargingPointUpdateRequest] ADD [StatusId] INT NULL;
    PRINT 'Added column: ChargingPointUpdateRequest.StatusId';
END
GO

-- 2. OldValuesJson
IF NOT EXISTS (SELECT * FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.ChargingPointUpdateRequest') AND name = 'OldValuesJson')
BEGIN
    ALTER TABLE [dbo].[ChargingPointUpdateRequest] ADD [OldValuesJson] NVARCHAR(MAX) NULL;
    PRINT 'Added column: ChargingPointUpdateRequest.OldValuesJson';
END
GO

-- 3. Drop de-whitelisted columns
IF EXISTS (SELECT * FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.ChargingPointUpdateRequest') AND name = 'ChargerPointTypeId')
BEGIN
    ALTER TABLE [dbo].[ChargingPointUpdateRequest] DROP COLUMN [ChargerPointTypeId];
    PRINT 'Dropped column: ChargingPointUpdateRequest.ChargerPointTypeId';
END
GO

IF EXISTS (SELECT * FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.ChargingPointUpdateRequest') AND name = 'StationTypeId')
BEGIN
    ALTER TABLE [dbo].[ChargingPointUpdateRequest] DROP COLUMN [StationTypeId];
    PRINT 'Dropped column: ChargingPointUpdateRequest.StationTypeId';
END
GO

IF EXISTS (SELECT * FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.ChargingPointUpdateRequest') AND name = 'HasOffer')
BEGIN
    ALTER TABLE [dbo].[ChargingPointUpdateRequest] DROP COLUMN [HasOffer];
    PRINT 'Dropped column: ChargingPointUpdateRequest.HasOffer';
END
GO

-- 4. Notification types for the workflow (admins on submit, owner on decision).
--    Resolved by NAME at runtime, so ids may differ between environments.
INSERT INTO [dbo].[NotificationType] ([Name], [Description])
SELECT v.Name, v.Description
FROM (VALUES
    ('update_request_submitted', 'A provider submitted a station update request (sent to admins)'),
    ('update_request_decided',   'A station update request was approved or rejected (sent to the owner)')
) AS v(Name, Description)
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[NotificationType] t WHERE t.Name = v.Name);
GO
GO


/* ############################################################################
   PART 10 — Terms & conditions (versions + acceptance)
   (source: Scripts/Terms_Phase1.sql)
   ############################################################################ */

-- =============================================
-- Terms & Conditions Phase 1: versioned policies + acceptance audit
-- Cable EV Charging Station Management
-- =============================================
-- 1. TermsVersion            — policy documents in the DB (AR + EN), scoped by
--                              RoleId (null = all roles), SystemVersion display
--                              string, one active per role scope
-- 2. UserTermsAcceptance     — immutable who-accepted-what-when audit trail
-- 3. UserAccount             — denormalized AcceptedTermsVersionId + TermsAcceptedAt
--
-- Run on db_ab1977_cableproduction BEFORE deploying the build that contains
-- the terms feature. Idempotent: safe to re-run. No content is seeded — the
-- admin publishes the first version through the portal.
-- =============================================

-- 1. TermsVersion
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'TermsVersion')
BEGIN
    CREATE TABLE [dbo].[TermsVersion] (
        [Id]            INT IDENTITY(1,1) NOT NULL,
        [SystemVersion] NVARCHAR(20)      NOT NULL,
        [RoleId]        INT               NULL,
        [ContentEn]     NVARCHAR(MAX)     NOT NULL,
        [ContentAr]     NVARCHAR(MAX)     NOT NULL,
        [EffectiveFrom] DATETIME          NOT NULL,
        [IsActive]      BIT               NOT NULL DEFAULT 0,
        [CreatedAt]     DATETIME          NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy]     INT               NULL,
        [ModifiedAt]    DATETIME          NULL,
        [ModifiedBy]    INT               NULL,
        [IsDeleted]     BIT               NOT NULL DEFAULT 0,
        CONSTRAINT [PK_TermsVersion] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_TermsVersion_Role] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[Role] ([Id])
    );

    -- One active version per role scope (single NULL allowed = one general policy)
    CREATE UNIQUE NONCLUSTERED INDEX [UX_TermsVersion_ActivePerRole]
        ON [dbo].[TermsVersion] ([RoleId]) WHERE [IsActive] = 1 AND [IsDeleted] = 0;

    PRINT 'Created table: TermsVersion';
END
GO

-- 2. UserTermsAcceptance
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserTermsAcceptance')
BEGIN
    CREATE TABLE [dbo].[UserTermsAcceptance] (
        [Id]             INT IDENTITY(1,1) NOT NULL,
        [UserId]         INT      NOT NULL,
        [TermsVersionId] INT      NOT NULL,
        [AcceptedAt]     DATETIME NOT NULL,
        CONSTRAINT [PK_UserTermsAcceptance] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_UserTermsAcceptance_UserAccount] FOREIGN KEY ([UserId]) REFERENCES [dbo].[UserAccount] ([Id]),
        CONSTRAINT [FK_UserTermsAcceptance_TermsVersion] FOREIGN KEY ([TermsVersionId]) REFERENCES [dbo].[TermsVersion] ([Id])
    );

    CREATE UNIQUE NONCLUSTERED INDEX [UX_UserTermsAcceptance_User_Version]
        ON [dbo].[UserTermsAcceptance] ([UserId], [TermsVersionId]);
    CREATE NONCLUSTERED INDEX [IX_UserTermsAcceptance_TermsVersionId]
        ON [dbo].[UserTermsAcceptance] ([TermsVersionId]);

    PRINT 'Created table: UserTermsAcceptance';
END
GO

-- 3. UserAccount denormalized columns
IF NOT EXISTS (SELECT * FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.UserAccount') AND name = 'AcceptedTermsVersionId')
BEGIN
    ALTER TABLE [dbo].[UserAccount] ADD [AcceptedTermsVersionId] INT NULL, [TermsAcceptedAt] DATETIME NULL;
    PRINT 'Added columns: UserAccount.AcceptedTermsVersionId, UserAccount.TermsAcceptedAt';
END
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_UserAccount_TermsVersion')
BEGIN
    ALTER TABLE [dbo].[UserAccount]
        ADD CONSTRAINT [FK_UserAccount_TermsVersion]
        FOREIGN KEY ([AcceptedTermsVersionId]) REFERENCES [dbo].[TermsVersion] ([Id]);
    PRINT 'Added FK: FK_UserAccount_TermsVersion';
END
GO
GO


/* ############################################################################
   PART 11 — Provider announcements to favorited users
   (source: Scripts/FavoritesNotify_Phase1.sql)
   ############################################################################ */

-- =============================================
-- Favorites Notify Phase 1: provider announcements to favorited users
-- Cable EV Charging Station Management
-- =============================================
-- 1. ProviderFavoriteNotification — immutable audit of every announcement a
--    provider sends to its fans (also the per-provider rate-limit source)
-- 2. NotificationType seed 'provider_announcement' (resolved by name)
--
-- Run on db_ab1977_cableproduction BEFORE deploying the build that contains
-- the favorites-notify endpoint. Idempotent: safe to re-run.
-- =============================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ProviderFavoriteNotification')
BEGIN
    CREATE TABLE [dbo].[ProviderFavoriteNotification] (
        [Id]             INT IDENTITY(1,1) NOT NULL,
        [ProviderType]   NVARCHAR(50)   NOT NULL,
        [ProviderId]     INT            NOT NULL,
        [SentByUserId]   INT            NOT NULL,
        [Title]          NVARCHAR(256)  NOT NULL,
        [Body]           NVARCHAR(1000) NOT NULL,
        [RecipientCount] INT            NOT NULL,
        [CreatedAt]      DATETIME       NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy]      INT            NULL,
        [ModifiedAt]     DATETIME       NULL,
        [ModifiedBy]     INT            NULL,
        [IsDeleted]      BIT            NOT NULL DEFAULT 0,
        CONSTRAINT [PK_ProviderFavoriteNotification] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_ProviderFavoriteNotification_UserAccount] FOREIGN KEY ([SentByUserId]) REFERENCES [dbo].[UserAccount] ([Id])
    );

    CREATE NONCLUSTERED INDEX [IX_ProviderFavoriteNotification_Provider_CreatedAt]
        ON [dbo].[ProviderFavoriteNotification] ([ProviderType], [ProviderId], [CreatedAt]);

    PRINT 'Created table: ProviderFavoriteNotification';
END
GO

INSERT INTO [dbo].[NotificationType] ([Name], [Description])
SELECT 'provider_announcement', 'A provider sent an announcement to users who favorited it'
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[NotificationType] t WHERE t.Name = 'provider_announcement');
GO
GO


/* ############################################################################
   PART 12 — Home-screen ads (targeting, campaigns, welcome, creatives)
   (source: Scripts/HomeAds_Phase1.sql)
   ############################################################################ */

-- =============================================
-- Home Ads Phase 1: sellable, location-targeted home-screen ad slots
-- Cable EV Charging Station Management
-- =============================================
-- Per BE-home-ads-requirements.md (P0 + P1 + P2):
-- 1. Advertiser / Campaign        — billing links for sold placements
-- 2. AppSetting                   — admin-editable global NearbyRadiusKm (seed 30)
-- 3. Banner targeting columns     — targetType/city/center/radius, linked entity,
--                                   priority, campaign link
-- 4. Announcement (+UserState)    — dynamic bilingual welcome takeover + frequency caps
-- 5. ChargingPoint.ViewImage(+Status) — partner-uploaded, admin-reviewed promo creative
-- 6. AnalyticsEvent City/Lat/Lng  — geo proof on the tracking payload
--
-- Run on db_ab1977_cableproduction BEFORE deploying the build that contains
-- the home-ads endpoints. Idempotent: safe to re-run.
-- =============================================

-- 1a. Advertiser
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Advertiser')
BEGIN
    CREATE TABLE [dbo].[Advertiser] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [Name] NVARCHAR(255) NOT NULL,
        [Contact] NVARCHAR(500) NULL,
        [Notes] NVARCHAR(2000) NULL,
        [CreatedAt] DATETIME NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy] INT NULL, [ModifiedAt] DATETIME NULL, [ModifiedBy] INT NULL,
        [IsDeleted] BIT NOT NULL DEFAULT 0,
        CONSTRAINT [PK_Advertiser] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
    PRINT 'Created table: Advertiser';
END
GO

-- 1b. Campaign
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Campaign')
BEGIN
    CREATE TABLE [dbo].[Campaign] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [AdvertiserId] INT NOT NULL,
        [Type] NVARCHAR(20) NOT NULL,
        [CityArea] NVARCHAR(200) NULL,
        [StartDate] DATETIME NOT NULL,
        [EndDate] DATETIME NULL,
        [Price] DECIMAL(18,2) NOT NULL DEFAULT 0,
        [Status] INT NOT NULL DEFAULT 0,
        [CreatedAt] DATETIME NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy] INT NULL, [ModifiedAt] DATETIME NULL, [ModifiedBy] INT NULL,
        [IsDeleted] BIT NOT NULL DEFAULT 0,
        CONSTRAINT [PK_Campaign] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_Campaign_Advertiser] FOREIGN KEY ([AdvertiserId]) REFERENCES [dbo].[Advertiser] ([Id])
    );
    PRINT 'Created table: Campaign';
END
GO

-- 2. AppSetting + NearbyRadiusKm seed
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AppSetting')
BEGIN
    CREATE TABLE [dbo].[AppSetting] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [Key] NVARCHAR(100) NOT NULL,
        [Value] NVARCHAR(500) NOT NULL,
        [CreatedAt] DATETIME NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy] INT NULL, [ModifiedAt] DATETIME NULL, [ModifiedBy] INT NULL,
        [IsDeleted] BIT NOT NULL DEFAULT 0,
        CONSTRAINT [PK_AppSetting] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
    CREATE UNIQUE NONCLUSTERED INDEX [UX_AppSetting_Key] ON [dbo].[AppSetting] ([Key]);
    PRINT 'Created table: AppSetting';
END
GO

INSERT INTO [dbo].[AppSetting] ([Key], [Value])
SELECT 'NearbyRadiusKm', '30'
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[AppSetting] WHERE [Key] = 'NearbyRadiusKm');
GO

-- 3. Banner targeting columns
IF COL_LENGTH('dbo.Banner', 'TargetType') IS NULL
BEGIN
    ALTER TABLE [dbo].[Banner] ADD
        [TargetType] NVARCHAR(20) NOT NULL CONSTRAINT DF_Banner_TargetType DEFAULT 'national',
        [TargetCity] NVARCHAR(100) NULL,
        [CenterLat] FLOAT NULL,
        [CenterLng] FLOAT NULL,
        [RadiusKm] FLOAT NULL,
        [LinkedEntityType] NVARCHAR(20) NULL,
        [LinkedEntityId] INT NULL,
        [Priority] INT NULL,
        [CampaignId] INT NULL CONSTRAINT FK_Banner_Campaign REFERENCES [dbo].[Campaign]([Id]);
    PRINT 'Added Banner targeting columns';
END
GO

-- 4a. Announcement
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Announcement')
BEGIN
    CREATE TABLE [dbo].[Announcement] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [TitleEn] NVARCHAR(200) NOT NULL,
        [TitleAr] NVARCHAR(200) NOT NULL,
        [BodyEn] NVARCHAR(1000) NOT NULL,
        [BodyAr] NVARCHAR(1000) NOT NULL,
        [ImageUrl] NVARCHAR(1000) NULL,
        [ActionType] INT NULL,
        [ActionUrl] NVARCHAR(1000) NULL,
        [ActionLabelEn] NVARCHAR(100) NULL,
        [ActionLabelAr] NVARCHAR(100) NULL,
        [TargetType] NVARCHAR(20) NOT NULL DEFAULT 'national',
        [TargetCity] NVARCHAR(100) NULL,
        [CenterLat] FLOAT NULL,
        [CenterLng] FLOAT NULL,
        [RadiusKm] FLOAT NULL,
        [Audience] NVARCHAR(20) NOT NULL DEFAULT 'all',
        [StartDate] DATETIME NOT NULL,
        [EndDate] DATETIME NULL,
        [MaxPerDay] INT NOT NULL DEFAULT 1,
        [CooldownHours] INT NOT NULL DEFAULT 24,
        [MaxLifetime] INT NOT NULL DEFAULT 3,
        [StopOnDismiss] BIT NOT NULL DEFAULT 1,
        [IsActive] BIT NOT NULL DEFAULT 1,
        [CampaignId] INT NULL,
        [AdvertiserId] INT NULL,
        [CreatedAt] DATETIME NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy] INT NULL, [ModifiedAt] DATETIME NULL, [ModifiedBy] INT NULL,
        [IsDeleted] BIT NOT NULL DEFAULT 0,
        CONSTRAINT [PK_Announcement] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_Announcement_Campaign] FOREIGN KEY ([CampaignId]) REFERENCES [dbo].[Campaign] ([Id]),
        CONSTRAINT [FK_Announcement_Advertiser] FOREIGN KEY ([AdvertiserId]) REFERENCES [dbo].[Advertiser] ([Id])
    );
    PRINT 'Created table: Announcement';
END
GO

-- 4a-bis. Announcement action labels (Part A delta — table may pre-date these columns)
IF COL_LENGTH('dbo.Announcement', 'ActionLabelEn') IS NULL
BEGIN
    ALTER TABLE [dbo].[Announcement] ADD [ActionLabelEn] NVARCHAR(100) NULL, [ActionLabelAr] NVARCHAR(100) NULL;
    PRINT 'Added columns: Announcement.ActionLabelEn, ActionLabelAr';
END
GO

-- 4b. AnnouncementUserState (frequency caps)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AnnouncementUserState')
BEGIN
    CREATE TABLE [dbo].[AnnouncementUserState] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [AnnouncementId] INT NOT NULL,
        [UserId] INT NOT NULL,
        [ShownCount] INT NOT NULL DEFAULT 0,
        [DailyShownCount] INT NOT NULL DEFAULT 0,
        [LastShownAt] DATETIME NULL,
        [DismissedCount] INT NOT NULL DEFAULT 0,
        CONSTRAINT [PK_AnnouncementUserState] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_AnnouncementUserState_Announcement] FOREIGN KEY ([AnnouncementId]) REFERENCES [dbo].[Announcement] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AnnouncementUserState_UserAccount] FOREIGN KEY ([UserId]) REFERENCES [dbo].[UserAccount] ([Id])
    );
    CREATE UNIQUE NONCLUSTERED INDEX [UX_AnnouncementUserState_Announcement_User]
        ON [dbo].[AnnouncementUserState] ([AnnouncementId], [UserId]);
    PRINT 'Created table: AnnouncementUserState';
END
GO

-- 5. ChargingPoint view image (promo creative)
IF COL_LENGTH('dbo.ChargingPoint', 'ViewImage') IS NULL
BEGIN
    ALTER TABLE [dbo].[ChargingPoint] ADD [ViewImage] NVARCHAR(500) NULL, [ViewImageStatus] NVARCHAR(20) NULL;
    PRINT 'Added columns: ChargingPoint.ViewImage, ViewImageStatus';
END
GO

-- 6. AnalyticsEvent geo columns
IF COL_LENGTH('dbo.AnalyticsEvent', 'City') IS NULL
BEGIN
    ALTER TABLE [dbo].[AnalyticsEvent] ADD [City] NVARCHAR(100) NULL, [Lat] FLOAT NULL, [Lng] FLOAT NULL;
    PRINT 'Added columns: AnalyticsEvent.City, Lat, Lng';
END
GO
GO


/* ############################################################################
   PART 13 — Notifications: routing map + worker approval + templates
   (source: Scripts/PartB_Notifications.sql)
   ############################################################################ */

-- ============================================================================
-- Part B — Notifications (routing contract + worker approval + templates)
-- Idempotent. Safe to re-run.
-- ============================================================================

-- 1. NotificationType.DeepLinksTo (R5 routing map)
IF COL_LENGTH('dbo.NotificationType', 'DeepLinksTo') IS NULL
BEGIN
    ALTER TABLE [dbo].[NotificationType] ADD [DeepLinksTo] NVARCHAR(30) NULL;
    PRINT 'Added column: NotificationType.DeepLinksTo';
END
GO

-- Seed the routing behavior for the existing types (only where not yet set)
UPDATE dbo.NotificationType SET DeepLinksTo = CASE Id
    WHEN 1 THEN 'none'            -- system_announcement
    WHEN 2 THEN 'charging-point'  -- favorite_added
    WHEN 3 THEN 'charging-point'  -- favorite_removed
    WHEN 4 THEN 'charging-point'  -- charging_point_status_changed
    WHEN 5 THEN 'charging-point'  -- offer_available (lands on the station with the offer)
    WHEN 6 THEN 'charging-point'  -- rating_received
    WHEN 7 THEN 'complaint'       -- complaint_status_updated
    WHEN 8 THEN 'charging-point'  -- charging_session_started
    WHEN 9 THEN 'charging-point'  -- charging_session_completed
    WHEN 10 THEN 'charging-point' -- new_charging_point_nearby
    WHEN 11 THEN 'charging-point' -- update_request_submitted
    WHEN 12 THEN 'charging-point' -- update_request_decided
    WHEN 13 THEN 'provider'       -- provider_announcement (link decided per send)
    ELSE 'none' END
WHERE DeepLinksTo IS NULL;
GO

-- 1b. NotificationType display names (DECISION-2 — client-supplied EN/AR labels)
IF COL_LENGTH('dbo.NotificationType', 'NameEn') IS NULL
BEGIN
    ALTER TABLE [dbo].[NotificationType] ADD [NameEn] NVARCHAR(100) NULL, [NameAr] NVARCHAR(100) NULL;
    PRINT 'Added columns: NotificationType.NameEn, NameAr';
END
GO

UPDATE dbo.NotificationType SET
  NameEn = CASE Id
    WHEN 1 THEN N'System Announcement' WHEN 2 THEN N'New Follower'
    WHEN 4 THEN N'Station Status Update' WHEN 5 THEN N'New Offer'
    WHEN 6 THEN N'New Rating' WHEN 7 THEN N'Complaint Update'
    WHEN 8 THEN N'Charging Started' WHEN 9 THEN N'Charging Complete'
    WHEN 10 THEN N'New Station Nearby' WHEN 11 THEN N'New Update Request'
    WHEN 12 THEN N'Update Request Decision' WHEN 13 THEN N'Announcement'
    ELSE NameEn END,
  NameAr = CASE Id
    WHEN 1 THEN N'إعلان عام' WHEN 2 THEN N'متابِع جديد'
    WHEN 4 THEN N'تحديث حالة المحطة' WHEN 5 THEN N'عرض جديد'
    WHEN 6 THEN N'تقييم جديد' WHEN 7 THEN N'تحديث الشكوى'
    WHEN 8 THEN N'بدء الشحن' WHEN 9 THEN N'انتهاء الشحن'
    WHEN 10 THEN N'محطة جديدة قريبة' WHEN 11 THEN N'طلب تعديل جديد'
    WHEN 12 THEN N'قرار طلب التعديل' WHEN 13 THEN N'إعلان'
    ELSE NameAr END
WHERE Id IN (1,2,4,5,6,7,8,9,10,11,12,13) AND (NameEn IS NULL OR NameAr IS NULL);
GO

-- 2. ProviderFavoriteNotification: worker-approval workflow (F3) + counts (F1)
IF COL_LENGTH('dbo.ProviderFavoriteNotification', 'Status') IS NULL
BEGIN
    ALTER TABLE [dbo].[ProviderFavoriteNotification] ADD
        [Status] NVARCHAR(10) NOT NULL CONSTRAINT DF_ProviderFavoriteNotification_Status DEFAULT 'sent',
        [NotificationTypeId] INT NULL,
        [BatchId] UNIQUEIDENTIFIER NULL,
        [DeliveredCount] INT NULL,
        [DecidedByUserId] INT NULL,
        [DecidedAt] DATETIME NULL,
        [SentAt] DATETIME NULL;
    PRINT 'Added ProviderFavoriteNotification workflow columns';
END
GO

-- 3. Auto-approve worker notifications toggle (F4)
IF COL_LENGTH('dbo.ChargingPoint', 'AutoApproveWorkerNotifications') IS NULL
BEGIN
    ALTER TABLE [dbo].[ChargingPoint] ADD [AutoApproveWorkerNotifications] BIT NOT NULL
        CONSTRAINT DF_ChargingPoint_AutoApproveWorkerNotifications DEFAULT 0;
    PRINT 'Added column: ChargingPoint.AutoApproveWorkerNotifications';
END
GO

IF COL_LENGTH('dbo.ServiceProvider', 'AutoApproveWorkerNotifications') IS NULL
BEGIN
    ALTER TABLE [dbo].[ServiceProvider] ADD [AutoApproveWorkerNotifications] BIT NOT NULL
        CONSTRAINT DF_ServiceProvider_AutoApproveWorkerNotifications DEFAULT 0;
    PRINT 'Added column: ServiceProvider.AutoApproveWorkerNotifications';
END
GO

-- 4. NotificationTemplate — admin-managed body suggestions (F5)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'NotificationTemplate')
BEGIN
    CREATE TABLE [dbo].[NotificationTemplate] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [NotificationTypeId] INT NOT NULL,
        [Body] NVARCHAR(1000) NOT NULL,
        [CreatedAt] DATETIME NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy] INT NULL, [ModifiedAt] DATETIME NULL, [ModifiedBy] INT NULL,
        [IsDeleted] BIT NOT NULL DEFAULT 0,
        CONSTRAINT [PK_NotificationTemplate] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_NotificationTemplate_NotificationType] FOREIGN KEY ([NotificationTypeId])
            REFERENCES [dbo].[NotificationType] ([Id])
    );
    CREATE NONCLUSTERED INDEX [IX_NotificationTemplate_NotificationTypeId]
        ON [dbo].[NotificationTemplate] ([NotificationTypeId]);
    PRINT 'Created table: NotificationTemplate';
END
GO
GO


/* =============================================================================
   PART 14 — Test / demo records (copied from dev, inserted by natural key)
   -----------------------------------------------------------------------------
   Ids on production will differ from dev; lookups resolve by email / name.
   The station has IsTest = 1 -> hidden from public discovery by the new build,
   still visible to its owner and by direct id (App Store review flow).
   ============================================================================= */

-- 11a. App Store review provider account
IF NOT EXISTS (SELECT 1 FROM dbo.UserAccount WHERE Email = 'Test.Review@gmail.com' AND IsDeleted = 0)
BEGIN
    INSERT INTO dbo.UserAccount
        (Name, Email, Phone, RoleID, Password, SecurityStamp, IsActive, IsPhoneVerified,
         Country, City, HasReadUpdateNotes, IsDeleted, CreatedAt)
    VALUES
        (N'App Store Review (Test)', 'Test.Review@gmail.com', '962790000000', 4,
         'AQAAAAIAAYagAAAAEAFAalJZTW5oqL+qyoDCLwJT/VgDzJUYmKBsCCL1k3ggwXS3ZV0LKgrFfRE9FovRXQ==',
         '76ae1fbe70e442b8b79360ae28597464', 1, 1, N'Jordan', N'Amman', 0, 0, GETUTCDATE());
    PRINT 'Created test account: App Store Review (Test) <Test.Review@gmail.com>';
END
GO

-- 11b. Worker test account (Worker role comes from Part 1)
IF NOT EXISTS (SELECT 1 FROM dbo.Role WHERE Name = N'Worker')
    PRINT '*** WARNING: Worker role missing — worker3 will NOT be created ***';
IF NOT EXISTS (SELECT 1 FROM dbo.UserAccount WHERE Email = 'worker3@gmail.com' AND IsDeleted = 0)
BEGIN
    INSERT INTO dbo.UserAccount
        (Name, Email, Phone, RoleID, Password, SecurityStamp, IsActive, IsPhoneVerified,
         HasReadUpdateNotes, IsDeleted, CreatedAt)
    SELECT N'worker3', 'worker3@gmail.com', '962789764654', R.Id,
         'AQAAAAIAAYagAAAAEHQCigFz7CXKbYKNp+Er/5XZb9/7rDNkzvn3EmL1vI63ZUm6Rkm/qVJrj0vVmNXqSg==',
         '70a1648cd80a4d07bb877c2d85c15204', 1, 1, 0, 0, GETUTCDATE()
    FROM dbo.Role R WHERE R.Name = N'Worker';
    IF @@ROWCOUNT > 0 PRINT 'Created test account: worker3 <worker3@gmail.com> (Worker role)';
END
GO

-- 11c. Demo station (owned by the review account, IsTest = 1)
IF NOT EXISTS (SELECT 1 FROM dbo.ChargingPoint WHERE Name = N'Cable Demo Station (Test)' AND IsDeleted = 0)
BEGIN
    INSERT INTO dbo.ChargingPoint
        (Name, OwnerId, CountryName, CityName, Phone, OwnerPhone, MethodPayment, price,
         FromTime, ToTime, ChargerSpeed, ChargersCount, Latitude, Longitude, VisitorsCount,
         ChargerPointTypeId, StatusId, StationTypeId, IsVerified, HasOffer, Address, Note,
         IsTest, IsLoyaltyBlocked, WalletBalance, IsDeleted, CreatedAt)
    SELECT N'Cable Demo Station (Test)', U.Id, N'Jordan', N'Amman', '962790000000', '962790000000',
           N'Cash', 1.5, '00:00', '23:59', 22, 2, 31.9539, 35.9106, 0,
           2, 1, 1, 1, 0, N'Demo address - App Store review',
           N'Demo station for App Store review. Not a real location.',
           1, 0, 0, 0, GETUTCDATE()
    FROM dbo.UserAccount U
    WHERE U.Email = 'Test.Review@gmail.com' AND U.IsDeleted = 0;
    IF @@ROWCOUNT > 0 PRINT 'Created demo station: Cable Demo Station (Test) (IsTest = 1)';
END
GO

/* =============================================================================
   PART 15 — Verification report (read this after the run)
   ============================================================================= */
SELECT Item, Result FROM (VALUES
 ('Table AnalyticsEvent',            CASE WHEN OBJECT_ID('dbo.AnalyticsEvent') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Table AnalyticsDailyRollup',      CASE WHEN OBJECT_ID('dbo.AnalyticsDailyRollup') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Table StationPremiumSubscription',CASE WHEN OBJECT_ID('dbo.StationPremiumSubscription') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Table ChargerBrand',              CASE WHEN OBJECT_ID('dbo.ChargerBrand') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Table ChargingPointChargerBrand', CASE WHEN OBJECT_ID('dbo.ChargingPointChargerBrand') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Table CarModelSize',              CASE WHEN OBJECT_ID('dbo.CarModelSize') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Table ProviderManager',           CASE WHEN OBJECT_ID('dbo.ProviderManager') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Table SocialMediaPlatform',       CASE WHEN OBJECT_ID('dbo.SocialMediaPlatform') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Table SocialLink',                CASE WHEN OBJECT_ID('dbo.SocialLink') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Table TermsVersion',              CASE WHEN OBJECT_ID('dbo.TermsVersion') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Table UserTermsAcceptance',       CASE WHEN OBJECT_ID('dbo.UserTermsAcceptance') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Table ProviderFavoriteNotification', CASE WHEN OBJECT_ID('dbo.ProviderFavoriteNotification') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('NotifType provider_announcement', CASE WHEN EXISTS (SELECT 1 FROM dbo.NotificationType WHERE Name = 'provider_announcement') THEN 'OK' ELSE 'MISSING' END),
 ('Table Advertiser',                CASE WHEN OBJECT_ID('dbo.Advertiser') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Table Campaign',                  CASE WHEN OBJECT_ID('dbo.Campaign') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Table AppSetting + radius seed',  CASE WHEN EXISTS (SELECT 1 FROM dbo.AppSetting WHERE [Key] = 'NearbyRadiusKm') THEN 'OK' ELSE 'MISSING' END),
 ('Table Announcement',              CASE WHEN OBJECT_ID('dbo.Announcement') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Table AnnouncementUserState',     CASE WHEN OBJECT_ID('dbo.AnnouncementUserState') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Col Banner.TargetType',           CASE WHEN COL_LENGTH('dbo.Banner','TargetType') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Col ChargingPoint.ViewImage',     CASE WHEN COL_LENGTH('dbo.ChargingPoint','ViewImage') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Col AnalyticsEvent.City',         CASE WHEN COL_LENGTH('dbo.AnalyticsEvent','City') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Role Worker',                     CASE WHEN EXISTS (SELECT 1 FROM dbo.Role WHERE Name = 'Worker') THEN 'OK' ELSE 'MISSING' END),
 ('NotifType update_request_submitted', CASE WHEN EXISTS (SELECT 1 FROM dbo.NotificationType WHERE Name = 'update_request_submitted') THEN 'OK' ELSE 'MISSING' END),
 ('NotifType update_request_decided',   CASE WHEN EXISTS (SELECT 1 FROM dbo.NotificationType WHERE Name = 'update_request_decided') THEN 'OK' ELSE 'MISSING' END),
 ('SocialMediaPlatform seeded',      CASE WHEN (SELECT COUNT(*) FROM dbo.SocialMediaPlatform) >= 9 THEN 'OK' ELSE 'CHECK' END),
 ('CarModelSize seeded',             CASE WHEN (SELECT COUNT(*) FROM dbo.CarModelSize) >= 7 THEN 'OK' ELSE 'CHECK' END),
 ('Col ChargingPoint.IsTest',        CASE WHEN COL_LENGTH('dbo.ChargingPoint','IsTest') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Col ChargingPoint.Premium dates', CASE WHEN COL_LENGTH('dbo.ChargingPoint','PremiumPaymentDate') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Col ChargingPoint.ChargerBrand DROPPED', CASE WHEN COL_LENGTH('dbo.ChargingPoint','ChargerBrand') IS NULL THEN 'OK' ELSE 'STILL THERE' END),
 ('Col ChargingPoint.OwnerId NULLABLE',     CASE WHEN (SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ChargingPoint') AND name = 'OwnerId') = 1 THEN 'OK' ELSE 'NOT NULLABLE' END),
 ('Col ServiceProvider.IsTest',      CASE WHEN COL_LENGTH('dbo.ServiceProvider','IsTest') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Col ServiceProvider.OwnerId NULLABLE',   CASE WHEN (SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ServiceProvider') AND name = 'OwnerId') = 1 THEN 'OK' ELSE 'NOT NULLABLE' END),
 ('Col ProviderWalletTransaction.RecordedBy NULLABLE', CASE WHEN (SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProviderWalletTransaction') AND name = 'RecordedByUserId') = 1 THEN 'OK' ELSE 'NOT NULLABLE' END),
 ('Col CarType.Icon',                CASE WHEN COL_LENGTH('dbo.CarType','Icon') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Col CarModel.SizeId',             CASE WHEN COL_LENGTH('dbo.CarModel','SizeId') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Col Rate.Comment',                CASE WHEN COL_LENGTH('dbo.Rate','Comment') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Col ProviderOffer.PointsPriceValue', CASE WHEN COL_LENGTH('dbo.ProviderOffer','PointsPriceValue') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Col UpdateRequest.StatusId',      CASE WHEN COL_LENGTH('dbo.ChargingPointUpdateRequest','StatusId') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Col UpdateRequest.OldValuesJson', CASE WHEN COL_LENGTH('dbo.ChargingPointUpdateRequest','OldValuesJson') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Col UpdateRequest old cols DROPPED', CASE WHEN COL_LENGTH('dbo.ChargingPointUpdateRequest','HasOffer') IS NULL AND COL_LENGTH('dbo.ChargingPointUpdateRequest','StationTypeId') IS NULL AND COL_LENGTH('dbo.ChargingPointUpdateRequest','ChargerPointTypeId') IS NULL THEN 'OK' ELSE 'STILL THERE' END),
 ('Col UserAccount terms columns',   CASE WHEN COL_LENGTH('dbo.UserAccount','AcceptedTermsVersionId') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Brand rows migrated from free text', CAST((SELECT COUNT(*) FROM dbo.ChargingPointChargerBrand) AS VARCHAR) + ' junction rows'),
 ('Col Announcement.ActionLabelEn',  CASE WHEN COL_LENGTH('dbo.Announcement','ActionLabelEn') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Col NotificationType.DeepLinksTo', CASE WHEN COL_LENGTH('dbo.NotificationType','DeepLinksTo') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Col ProviderFavoriteNotification.Status', CASE WHEN COL_LENGTH('dbo.ProviderFavoriteNotification','Status') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Col ChargingPoint.AutoApproveWorkerNotifications', CASE WHEN COL_LENGTH('dbo.ChargingPoint','AutoApproveWorkerNotifications') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Col ServiceProvider.AutoApproveWorkerNotifications', CASE WHEN COL_LENGTH('dbo.ServiceProvider','AutoApproveWorkerNotifications') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Table NotificationTemplate',      CASE WHEN OBJECT_ID('dbo.NotificationTemplate') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('NotificationType routing seeded', CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.NotificationType WHERE DeepLinksTo IS NULL) THEN 'OK' ELSE 'CHECK' END),
 ('Test account App Store Review',   CASE WHEN EXISTS (SELECT 1 FROM dbo.UserAccount WHERE Email = 'Test.Review@gmail.com' AND IsDeleted = 0) THEN 'OK' ELSE 'MISSING' END),
 ('Test account worker3',            CASE WHEN EXISTS (SELECT 1 FROM dbo.UserAccount WHERE Email = 'worker3@gmail.com' AND IsDeleted = 0) THEN 'OK' ELSE 'MISSING' END),
 ('Demo station (IsTest=1)',         CASE WHEN EXISTS (SELECT 1 FROM dbo.ChargingPoint WHERE Name = N'Cable Demo Station (Test)' AND IsTest = 1 AND IsDeleted = 0) THEN 'OK' ELSE 'MISSING' END)
) v(Item, Result);
PRINT '=== Master deployment script finished — review the verification report above ===';
