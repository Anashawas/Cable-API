/* =============================================================================
   PRODUCTION DEPLOYMENT — db_ab1977_cableproduction
   Generated 2026-08-22

   This is the COMPLETE dev -> production schema delta. It was not written from
   memory: every item below came from diffing INFORMATION_SCHEMA on both
   databases (88 tables, every column, index and foreign key). Five objects
   differed; all five are handled here.

   Run this ONE script. It replaces:
       ProviderSessionStamp.sql   (partially — see item 1)
       UserRating_Phase1.sql
       UserActivityTracking.sql
       LoyaltyBoosts_Phase1.sql
       LoyaltyBoosts_Phase2_WelcomeBonus.sql   (no-op here — see NOTE below)

   -----------------------------------------------------------------------------
   WHAT IT DOES

     1. UserAccount.ProviderWebSecurityStamp   <-- CRITICAL, see below
     2. UserAccount.LastLoginAt / LastSeenAt   + index
     3. UserRate table                         (driver rating, never deployed)
     4. LoyaltyBoost + LoyaltyBoostProvider    (points multiplier campaigns)
     5. PartnerTransaction: 4 columns          + FK + filtered index

   -----------------------------------------------------------------------------
   ITEM 1 IS THE ONE THAT MATTERS MOST

     Production has SecurityStamp and ProviderSecurityStamp but NOT
     ProviderWebSecurityStamp. The current build reads all three on every
     authenticated request. Publish the API without this column and EVERY
     authenticated request returns 500 — the whole app, not just the new
     features.

     That is why this script must run BEFORE the publish.

   -----------------------------------------------------------------------------
   SAFETY

     - Additive only. Nothing is dropped, altered or backfilled.
     - Every new column is NULLable or has a DEFAULT, so existing rows are valid
       as they stand and no table is rewritten.
     - Guarded and idempotent: safe to run twice, safe to re-run after a partial
       failure.
     - Safe to run BEFORE the publish: the new objects are simply unused until
       the new build is live. Running it first is the intended order.

   DEPLOY ORDER
     1. Run this script.
     2. Verify the output at the bottom shows all OK.
     3. Publish the WebApi.

   ROLLBACK
     Rolling the API back needs no database change — the previous build ignores
     every object added here.

   NOTE ON LoyaltyBoosts_Phase2_WelcomeBonus.sql
     Do NOT run it on production. It migrates an older shape of LoyaltyBoost
     that only ever existed on dev. This script creates the table in its final
     form, so Phase 2 has nothing to do here.

   NOTE ON THE WELCOME BONUS
     Nothing to seed. Double points on a customer's first ever charge is
     intrinsic to the app and live the moment the code deploys. To retune or
     disable it afterwards use PUT /api/settings/welcome-bonus (1 = off).
   ============================================================================= */

/* REQUIRED — do not remove.

   sqlcmd connects with QUOTED_IDENTIFIER OFF, and CREATE INDEX on a table that
   carries filtered indexes (UserAccount, PartnerTransaction and UserRate all
   do) is rejected outright under that setting:

     Msg 1934 ... CREATE INDEX failed because the following SET options have
     incorrect settings: 'QUOTED_IDENTIFIER'.

   SSMS defaults these ON, which is why the failure only appears from the
   command line. Setting them here makes the script behave identically however
   it is run. They persist for the connection, so one declaration covers every
   batch below. */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

SET NOCOUNT ON;
SET XACT_ABORT ON;

PRINT '===========================================================';
PRINT ' Cable — production deployment 2026-08-22';
PRINT '===========================================================';
PRINT '';

/* Refuse to run anywhere unexpected. The delta below was computed against the
   production database specifically; applying it blindly elsewhere is not safe. */
DECLARE @db SYSNAME = DB_NAME();

IF @db <> 'db_ab1977_cableproduction'
BEGIN
    -- RAISERROR will not accept DB_NAME() directly as a substitution argument;
    -- it has to come from a variable.
    RAISERROR('WRONG DATABASE: this script targets db_ab1977_cableproduction but is connected to "%s". Aborting.', 16, 1, @db);
    SET NOEXEC ON;
END
GO

/* =========================================================================
   1. UserAccount.ProviderWebSecurityStamp   ** BLOCKS THE PUBLISH **
   -------------------------------------------------------------------------
   Per-app session stamps. Each client app owns its own stamp so signing into
   one does not evict the others: SecurityStamp = consumer app,
   ProviderSecurityStamp = provider mobile, ProviderWebSecurityStamp = the
   partner web portal.

   NULL is the correct starting value. A NULL stamp means "no active partner-web
   session", and existing consumer/provider sessions are untouched.
   ========================================================================= */
PRINT '--- 1. Per-app session stamp ---';

IF COL_LENGTH('dbo.UserAccount', 'ProviderWebSecurityStamp') IS NULL
BEGIN
    ALTER TABLE dbo.UserAccount ADD ProviderWebSecurityStamp NVARCHAR(50) NULL;
    PRINT '  [ADDED]   dbo.UserAccount.ProviderWebSecurityStamp';
END
ELSE
    PRINT '  [SKIPPED] dbo.UserAccount.ProviderWebSecurityStamp already exists';
GO

/* =========================================================================
   2. UserAccount — last login / last seen
   -------------------------------------------------------------------------
   LastLoginAt : written on every successful sign-in.
   LastSeenAt  : written on any authenticated request, throttled to one write
                 per user per 15 minutes. This is the DAU/WAU/MAU signal.

   Both stay NULL for every existing user until they next use the app. That is
   correct — nothing was tracked before now, and NULL says so rather than
   inventing a date. Expect "never seen" to start near the full user count and
   fall over the following weeks; it is not a drop in usage.
   ========================================================================= */
PRINT '';
PRINT '--- 2. User activity tracking ---';

IF COL_LENGTH('dbo.UserAccount', 'LastLoginAt') IS NULL
BEGIN
    ALTER TABLE dbo.UserAccount ADD LastLoginAt DATETIME2(7) NULL;
    PRINT '  [ADDED]   dbo.UserAccount.LastLoginAt';
END
ELSE
    PRINT '  [SKIPPED] dbo.UserAccount.LastLoginAt already exists';

IF COL_LENGTH('dbo.UserAccount', 'LastSeenAt') IS NULL
BEGIN
    ALTER TABLE dbo.UserAccount ADD LastSeenAt DATETIME2(7) NULL;
    PRINT '  [ADDED]   dbo.UserAccount.LastSeenAt';
END
ELSE
    PRINT '  [SKIPPED] dbo.UserAccount.LastSeenAt already exists';
GO

/* Separate batch: the column must exist before an index can reference it. */
IF COL_LENGTH('dbo.UserAccount', 'LastSeenAt') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = 'IX_UserAccount_LastSeenAt'
                     AND object_id = OBJECT_ID('dbo.UserAccount'))
BEGIN
    CREATE INDEX IX_UserAccount_LastSeenAt ON dbo.UserAccount (LastSeenAt);
    PRINT '  [ADDED]   IX_UserAccount_LastSeenAt';
END
ELSE
    PRINT '  [SKIPPED] IX_UserAccount_LastSeenAt already exists';
GO

/* =========================================================================
   3. UserRate — driver rating after a charge
   -------------------------------------------------------------------------
   Shipped to dev on 2026-08-17 but never deployed here. POST /api/rate/RateUser
   returns 500 without this table.
   ========================================================================= */
PRINT '';
PRINT '--- 3. UserRate ---';

IF OBJECT_ID('dbo.UserRate', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserRate
    (
        Id                   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserRate PRIMARY KEY,
        UserId               INT             NOT NULL,   -- who was rated
        RatedByUserId        INT             NOT NULL,   -- the provider/staff rating them
        ProviderType         NVARCHAR(50)    NOT NULL,
        ProviderId           INT             NOT NULL,
        PartnerTransactionId INT             NOT NULL,
        Rating               INT             NOT NULL,
        Comment              NVARCHAR(1000)  NULL,
        IsDeleted            BIT             NOT NULL CONSTRAINT DF_UserRate_IsDeleted DEFAULT(0),
        CreatedBy            INT             NULL,
        CreatedAt            DATETIME        NOT NULL CONSTRAINT DF_UserRate_CreatedAt DEFAULT(GETDATE()),
        ModifiedBy           INT             NULL,
        ModifiedAt           DATETIME        NULL,

        CONSTRAINT FK_UserRate_UserAccount
            FOREIGN KEY (UserId) REFERENCES dbo.UserAccount(Id),
        CONSTRAINT FK_UserRate_RatedByUserAccount
            FOREIGN KEY (RatedByUserId) REFERENCES dbo.UserAccount(Id),
        CONSTRAINT FK_UserRate_PartnerTransaction
            FOREIGN KEY (PartnerTransactionId) REFERENCES dbo.PartnerTransaction(Id)
    );

    -- One rating per transaction. Filtered so a soft-deleted rating does not
    -- block a corrected one.
    CREATE UNIQUE INDEX UX_UserRate_PartnerTransactionId
        ON dbo.UserRate (PartnerTransactionId) WHERE IsDeleted = 0;

    CREATE INDEX IX_UserRate_UserId   ON dbo.UserRate (Rating, UserId)          WHERE IsDeleted = 0;
    CREATE INDEX IX_UserRate_Provider ON dbo.UserRate (ProviderType, ProviderId) WHERE IsDeleted = 0;

    PRINT '  [CREATED] dbo.UserRate (+3 indexes, 3 foreign keys)';
END
ELSE
    PRINT '  [SKIPPED] dbo.UserRate already exists';
GO

/* =========================================================================
   4. LoyaltyBoost + LoyaltyBoostProvider — points multiplier campaigns
   -------------------------------------------------------------------------
   Admin-created, time-limited multipliers at selected providers.

   Two clocks, deliberately:
     StartsAt / EndsAt                  are UTC
     DailyStartMinute / DailyEndMinute  are minutes from midnight JORDAN LOCAL

   Created empty. Campaigns are made through the admin API, and the welcome
   bonus needs no row here at all.
   ========================================================================= */
PRINT '';
PRINT '--- 4. Loyalty boosts ---';

IF OBJECT_ID('dbo.LoyaltyBoost', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LoyaltyBoost
    (
        Id                    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LoyaltyBoost PRIMARY KEY,
        Name                  NVARCHAR(150)  NOT NULL,
        NameAr                NVARCHAR(150)  NULL,
        [Description]         NVARCHAR(500)  NULL,
        Multiplier            FLOAT          NOT NULL,
        StartsAt              DATETIME2(7)   NOT NULL,   -- UTC, inclusive
        EndsAt                DATETIME2(7)   NOT NULL,   -- UTC, exclusive
        DailyStartMinute      INT            NULL,       -- Jordan local, 0-1439
        DailyEndMinute        INT            NULL,       -- Jordan local, 0-1439
        DaysOfWeekMask        INT            NULL,       -- bit 0 = Sunday
        AppliesToAllProviders BIT            NOT NULL CONSTRAINT DF_LoyaltyBoost_AllProviders DEFAULT(0),
        Priority              INT            NOT NULL CONSTRAINT DF_LoyaltyBoost_Priority     DEFAULT(0),
        MaxBonusPointsPerUser INT            NULL,
        MaxTotalBonusPoints   INT            NULL,
        IsActive              BIT            NOT NULL CONSTRAINT DF_LoyaltyBoost_IsActive     DEFAULT(1),
        CreatedBy             INT            NULL,
        CreatedAt             DATETIME2(7)   NOT NULL CONSTRAINT DF_LoyaltyBoost_CreatedAt    DEFAULT(SYSUTCDATETIME()),
        ModifiedBy            INT            NULL,
        ModifiedAt            DATETIME2(7)   NULL,
        IsDeleted             BIT            NOT NULL CONSTRAINT DF_LoyaltyBoost_IsDeleted    DEFAULT(0)
    );

    CREATE INDEX IX_LoyaltyBoost_Window ON dbo.LoyaltyBoost (IsActive, StartsAt, EndsAt);

    PRINT '  [CREATED] dbo.LoyaltyBoost';
END
ELSE
    PRINT '  [SKIPPED] dbo.LoyaltyBoost already exists';
GO

IF OBJECT_ID('dbo.LoyaltyBoostProvider', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LoyaltyBoostProvider
    (
        Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LoyaltyBoostProvider PRIMARY KEY,
        LoyaltyBoostId INT           NOT NULL,
        ProviderType   NVARCHAR(50)  NOT NULL,   -- 'ChargingPoint' | 'ServiceProvider'
        ProviderId     INT           NOT NULL,
        CreatedBy      INT           NULL,
        CreatedAt      DATETIME2(7)  NOT NULL CONSTRAINT DF_LoyaltyBoostProvider_CreatedAt DEFAULT(SYSUTCDATETIME()),
        ModifiedBy     INT           NULL,
        ModifiedAt     DATETIME2(7)  NULL,
        IsDeleted      BIT           NOT NULL CONSTRAINT DF_LoyaltyBoostProvider_IsDeleted DEFAULT(0),

        CONSTRAINT FK_LoyaltyBoostProvider_LoyaltyBoost
            FOREIGN KEY (LoyaltyBoostId) REFERENCES dbo.LoyaltyBoost(Id) ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX UX_LoyaltyBoostProvider_Target
        ON dbo.LoyaltyBoostProvider (LoyaltyBoostId, ProviderType, ProviderId);

    CREATE INDEX IX_LoyaltyBoostProvider_Provider
        ON dbo.LoyaltyBoostProvider (ProviderType, ProviderId);

    PRINT '  [CREATED] dbo.LoyaltyBoostProvider';
END
ELSE
    PRINT '  [SKIPPED] dbo.LoyaltyBoostProvider already exists';
GO

/* =========================================================================
   5. PartnerTransaction — multiplier attribution
   -------------------------------------------------------------------------
   Existing rows read as unboosted, which is correct for every transaction that
   predates the feature. No backfill.

   Bonus cost is always PointsAwarded - BasePoints, derived from the
   transactions themselves, so there is no counter that can drift.
   ========================================================================= */
PRINT '';
PRINT '--- 5. PartnerTransaction attribution ---';

IF COL_LENGTH('dbo.PartnerTransaction', 'BasePoints') IS NULL
BEGIN
    ALTER TABLE dbo.PartnerTransaction ADD BasePoints INT NULL;
    PRINT '  [ADDED]   dbo.PartnerTransaction.BasePoints';
END
ELSE
    PRINT '  [SKIPPED] dbo.PartnerTransaction.BasePoints already exists';

IF COL_LENGTH('dbo.PartnerTransaction', 'BoostMultiplier') IS NULL
BEGIN
    ALTER TABLE dbo.PartnerTransaction ADD BoostMultiplier FLOAT NULL;
    PRINT '  [ADDED]   dbo.PartnerTransaction.BoostMultiplier';
END
ELSE
    PRINT '  [SKIPPED] dbo.PartnerTransaction.BoostMultiplier already exists';

IF COL_LENGTH('dbo.PartnerTransaction', 'AppliedBoostId') IS NULL
BEGIN
    ALTER TABLE dbo.PartnerTransaction ADD AppliedBoostId INT NULL;
    PRINT '  [ADDED]   dbo.PartnerTransaction.AppliedBoostId';
END
ELSE
    PRINT '  [SKIPPED] dbo.PartnerTransaction.AppliedBoostId already exists';

/* The welcome bonus has no campaign row, so AppliedBoostId stays NULL for it.
   Flagged explicitly rather than inferred from that NULL, so "what has the
   welcome bonus cost us" stays a direct query. */
IF COL_LENGTH('dbo.PartnerTransaction', 'IsWelcomeBonus') IS NULL
BEGIN
    ALTER TABLE dbo.PartnerTransaction
        ADD IsWelcomeBonus BIT NOT NULL
        CONSTRAINT DF_PartnerTransaction_IsWelcomeBonus DEFAULT(0);
    PRINT '  [ADDED]   dbo.PartnerTransaction.IsWelcomeBonus';
END
ELSE
    PRINT '  [SKIPPED] dbo.PartnerTransaction.IsWelcomeBonus already exists';
GO

/* Separate batch: both the column and dbo.LoyaltyBoost must exist first.
   NO ACTION on delete is deliberate — removing a campaign must not take the
   record of the transactions it paid out on with it. */
IF OBJECT_ID('dbo.FK_PartnerTransaction_LoyaltyBoost', 'F') IS NULL
   AND COL_LENGTH('dbo.PartnerTransaction', 'AppliedBoostId') IS NOT NULL
   AND OBJECT_ID('dbo.LoyaltyBoost', 'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.PartnerTransaction
        ADD CONSTRAINT FK_PartnerTransaction_LoyaltyBoost
        FOREIGN KEY (AppliedBoostId) REFERENCES dbo.LoyaltyBoost(Id);
    PRINT '  [ADDED]   FK_PartnerTransaction_LoyaltyBoost';
END
ELSE
    PRINT '  [SKIPPED] FK_PartnerTransaction_LoyaltyBoost already exists';
GO

IF COL_LENGTH('dbo.PartnerTransaction', 'AppliedBoostId') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = 'IX_PartnerTransaction_AppliedBoostId'
                     AND object_id = OBJECT_ID('dbo.PartnerTransaction'))
BEGIN
    CREATE INDEX IX_PartnerTransaction_AppliedBoostId
        ON dbo.PartnerTransaction (AppliedBoostId)
        WHERE AppliedBoostId IS NOT NULL;
    PRINT '  [ADDED]   IX_PartnerTransaction_AppliedBoostId';
END
ELSE
    PRINT '  [SKIPPED] IX_PartnerTransaction_AppliedBoostId already exists';
GO

/* =========================================================================
   VERIFICATION — every row must read OK before you publish
   ========================================================================= */
PRINT '';
PRINT '===========================================================';
PRINT ' VERIFICATION';
PRINT '===========================================================';

SELECT Item, Result FROM (
    SELECT 1 AS Ord, '1. UserAccount.ProviderWebSecurityStamp  ** blocks publish **' AS Item,
           CASE WHEN COL_LENGTH('dbo.UserAccount','ProviderWebSecurityStamp') IS NULL THEN 'MISSING' ELSE 'OK' END AS Result
    UNION ALL SELECT 2, '2. UserAccount.LastLoginAt',
           CASE WHEN COL_LENGTH('dbo.UserAccount','LastLoginAt') IS NULL THEN 'MISSING' ELSE 'OK' END
    UNION ALL SELECT 3, '2. UserAccount.LastSeenAt',
           CASE WHEN COL_LENGTH('dbo.UserAccount','LastSeenAt') IS NULL THEN 'MISSING' ELSE 'OK' END
    UNION ALL SELECT 4, '2. IX_UserAccount_LastSeenAt',
           CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_UserAccount_LastSeenAt' AND object_id=OBJECT_ID('dbo.UserAccount')) THEN 'OK' ELSE 'MISSING' END
    UNION ALL SELECT 5, '3. dbo.UserRate',
           CASE WHEN OBJECT_ID('dbo.UserRate','U') IS NULL THEN 'MISSING' ELSE 'OK' END
    UNION ALL SELECT 6, '4. dbo.LoyaltyBoost',
           CASE WHEN OBJECT_ID('dbo.LoyaltyBoost','U') IS NULL THEN 'MISSING' ELSE 'OK' END
    UNION ALL SELECT 7, '4. dbo.LoyaltyBoostProvider',
           CASE WHEN OBJECT_ID('dbo.LoyaltyBoostProvider','U') IS NULL THEN 'MISSING' ELSE 'OK' END
    UNION ALL SELECT 8, '5. PartnerTransaction.BasePoints',
           CASE WHEN COL_LENGTH('dbo.PartnerTransaction','BasePoints') IS NULL THEN 'MISSING' ELSE 'OK' END
    UNION ALL SELECT 9, '5. PartnerTransaction.BoostMultiplier',
           CASE WHEN COL_LENGTH('dbo.PartnerTransaction','BoostMultiplier') IS NULL THEN 'MISSING' ELSE 'OK' END
    UNION ALL SELECT 10, '5. PartnerTransaction.AppliedBoostId',
           CASE WHEN COL_LENGTH('dbo.PartnerTransaction','AppliedBoostId') IS NULL THEN 'MISSING' ELSE 'OK' END
    UNION ALL SELECT 11, '5. PartnerTransaction.IsWelcomeBonus',
           CASE WHEN COL_LENGTH('dbo.PartnerTransaction','IsWelcomeBonus') IS NULL THEN 'MISSING' ELSE 'OK' END
    UNION ALL SELECT 12, '5. FK_PartnerTransaction_LoyaltyBoost',
           CASE WHEN OBJECT_ID('dbo.FK_PartnerTransaction_LoyaltyBoost','F') IS NULL THEN 'MISSING' ELSE 'OK' END
    UNION ALL SELECT 13, '5. IX_PartnerTransaction_AppliedBoostId',
           CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_PartnerTransaction_AppliedBoostId' AND object_id=OBJECT_ID('dbo.PartnerTransaction')) THEN 'OK' ELSE 'MISSING' END
) v ORDER BY Ord;

/* Column counts must now match dev exactly. */
SELECT
    (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='PartnerTransaction') AS PartnerTransaction_Cols_Expect_28,
    (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='LoyaltyBoost')       AS LoyaltyBoost_Cols_Expect_20,
    (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='UserRate')           AS UserRate_Cols_Expect_13,
    (SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES  WHERE TABLE_TYPE='BASE TABLE')         AS Tables_Expect_88;

/* UserAccount is expected to be 28, not 26 like dev: production carries two
   extra legacy columns, OriginalCity and OriginalCountry. Both are nullable and
   unmapped, so the application ignores them. Nothing to do — this note exists
   so the mismatch is not mistaken for a missing migration later. */
SELECT COUNT(*) AS UserAccount_Cols_Expect_28
FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='UserAccount';

PRINT '';
PRINT 'If every Result above reads OK and the counts match, publish the WebApi.';
PRINT 'If anything reads MISSING, DO NOT PUBLISH — re-run this script and check';
PRINT 'the messages above for the failure.';
PRINT '';
GO

SET NOEXEC OFF;
