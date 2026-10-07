/* =============================================================================
   Loyalty Boosts — Phase 1

   Time-limited points multipliers at selected providers.

   The once-ever welcome bonus is NOT here and needs nothing from this script:
   it is intrinsic to the app, defaults to 2x, and is resolved in code. See
   AppSettingsProvider and Docs/Loyalty-Boosts-Spec.md section 7.

   Additive only: two new tables and three nullable columns. Existing rows read
   as AppliedBoostId NULL — "no boost" — which is correct for every transaction
   that predates the feature, so no backfill is needed and this is safe to run
   before the code is deployed.

   Guarded and idempotent: safe to run more than once.
   ============================================================================= */

SET NOCOUNT ON;
PRINT '=== Loyalty Boosts Phase 1 ===';

/* ---------------------------------------------------------------- LoyaltyBoost */
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

    CREATE INDEX IX_LoyaltyBoost_Window
        ON dbo.LoyaltyBoost (IsActive, StartsAt, EndsAt);

    PRINT '  [CREATED] dbo.LoyaltyBoost';
END
ELSE
    PRINT '  [SKIPPED] dbo.LoyaltyBoost already exists';

/* -------------------------------------------------------- LoyaltyBoostProvider */
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

/* ------------------------------------------- PartnerTransaction: attribution */
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
   Flagged explicitly rather than inferred from that NULL. */
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

/* NO ACTION on delete is deliberate: removing a campaign must not take the
   record of the transactions it paid out on with it. */
IF OBJECT_ID('dbo.FK_PartnerTransaction_LoyaltyBoost', 'F') IS NULL
   AND COL_LENGTH('dbo.PartnerTransaction', 'AppliedBoostId') IS NOT NULL
BEGIN
    ALTER TABLE dbo.PartnerTransaction
        ADD CONSTRAINT FK_PartnerTransaction_LoyaltyBoost
        FOREIGN KEY (AppliedBoostId) REFERENCES dbo.LoyaltyBoost(Id);
    PRINT '  [ADDED]   FK_PartnerTransaction_LoyaltyBoost';
END
ELSE
    PRINT '  [SKIPPED] FK_PartnerTransaction_LoyaltyBoost already exists';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
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

/* -------------------------------------------------------------- verification */
PRINT '';
PRINT '=== Verification ===';

SELECT
    CASE WHEN OBJECT_ID('dbo.LoyaltyBoost','U')         IS NULL THEN 'MISSING' ELSE 'OK' END AS LoyaltyBoost,
    CASE WHEN OBJECT_ID('dbo.LoyaltyBoostProvider','U') IS NULL THEN 'MISSING' ELSE 'OK' END AS LoyaltyBoostProvider,
    CASE WHEN COL_LENGTH('dbo.PartnerTransaction','BasePoints')      IS NULL THEN 'MISSING' ELSE 'OK' END AS BasePoints,
    CASE WHEN COL_LENGTH('dbo.PartnerTransaction','BoostMultiplier') IS NULL THEN 'MISSING' ELSE 'OK' END AS BoostMultiplier,
    CASE WHEN COL_LENGTH('dbo.PartnerTransaction','AppliedBoostId')  IS NULL THEN 'MISSING' ELSE 'OK' END AS AppliedBoostId,
    CASE WHEN COL_LENGTH('dbo.PartnerTransaction','IsWelcomeBonus')  IS NULL THEN 'MISSING' ELSE 'OK' END AS IsWelcomeBonus;

PRINT '';
PRINT 'Done. No backfill required — existing transactions read as unboosted.';
PRINT '';
PRINT 'The welcome bonus (double points on a first ever charge) needs NOTHING here.';
PRINT 'It is intrinsic and on by default. To retune or disable it, set the';
PRINT 'AppSetting key WelcomeBonusMultiplier (1 = off). Absent = 2.';
