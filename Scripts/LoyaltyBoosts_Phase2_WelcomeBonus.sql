/* =============================================================================
   Loyalty Boosts — Phase 2: welcome bonus becomes intrinsic

   The once-ever first-charge bonus used to be a LoyaltyBoost row carrying
   FirstTransactionOnly = 1. That was wrong in one important way: it only
   existed if somebody remembered to create it. A welcome gift for new customers
   is a property of the app, not a campaign, so it is now resolved in code with
   a default of 2x and no row anywhere. See AppSettingsProvider.

   This script migrates an environment that already ran the ORIGINAL Phase 1.
   On a database that ran the current Phase 1 it is a no-op — everything below
   is guarded.

   -------------------------------------------------------------------------
   ORDER IS LOAD-BEARING. Read before editing.

   A row with FirstTransactionOnly = 1, AppliesToAllProviders = 1, Multiplier = 2
   and a far-future EndsAt means "double points for first-time customers,
   forever". Drop the column out from under it and the surviving row means
   "double points for EVERY customer at EVERY station, forever" — an unbounded
   giveaway with no end date and no cap.

   So those rows are retired in step 3, BEFORE the column disappears in step 4.
   Do not reorder.
   -------------------------------------------------------------------------

   Guarded and idempotent: safe to run more than once.
   ============================================================================= */

SET NOCOUNT ON;
PRINT '=== Loyalty Boosts Phase 2 — welcome bonus becomes intrinsic ===';

/* --------------------------------------------- 1. attribution for the bonus */
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

/* ------------------------------------------------------ 2. preserve history */
/* Charges already paid by a first-charge boost are re-attributed to the
   welcome bonus, so their AppliedBoostId can be released before the rows it
   points at are retired. The multiplier and BasePoints are left untouched —
   what the customer was actually given does not change. */
IF COL_LENGTH('dbo.LoyaltyBoost', 'FirstTransactionOnly') IS NOT NULL
BEGIN
    DECLARE @rebadged INT;

    UPDATE t
       SET t.IsWelcomeBonus = 1,
           t.AppliedBoostId = NULL
      FROM dbo.PartnerTransaction t
      JOIN dbo.LoyaltyBoost b ON b.Id = t.AppliedBoostId
     WHERE b.FirstTransactionOnly = 1;

    SET @rebadged = @@ROWCOUNT;
    PRINT '  [MOVED]   ' + CAST(@rebadged AS VARCHAR(10))
          + ' transaction(s) re-attributed to the welcome bonus';
END
GO

/* ------------------------------------- 3. retire first-charge campaign rows */
/* MUST happen before the column is dropped — see the header. */
IF COL_LENGTH('dbo.LoyaltyBoost', 'FirstTransactionOnly') IS NOT NULL
BEGIN
    DECLARE @retired INT;

    UPDATE dbo.LoyaltyBoost
       SET IsActive  = 0,
           IsDeleted = 1,
           ModifiedAt = SYSUTCDATETIME()
     WHERE FirstTransactionOnly = 1
       AND IsDeleted = 0;

    SET @retired = @@ROWCOUNT;
    PRINT '  [RETIRED] ' + CAST(@retired AS VARCHAR(10))
          + ' first-charge boost row(s) — superseded by the intrinsic bonus';
END
GO

/* ---------------------------------------------------- 4. drop the dead flag */
IF COL_LENGTH('dbo.LoyaltyBoost', 'FirstTransactionOnly') IS NOT NULL
BEGIN
    /* Look the default constraint up by name rather than assuming
       DF_LoyaltyBoost_FirstOnly: a table created by EF rather than by Phase 1
       would have an auto-generated name. */
    DECLARE @constraint SYSNAME, @sql NVARCHAR(500);

    SELECT @constraint = dc.name
      FROM sys.default_constraints dc
      JOIN sys.columns c
        ON c.object_id = dc.parent_object_id
       AND c.column_id = dc.parent_column_id
     WHERE dc.parent_object_id = OBJECT_ID('dbo.LoyaltyBoost')
       AND c.name = 'FirstTransactionOnly';

    IF @constraint IS NOT NULL
    BEGIN
        SET @sql = N'ALTER TABLE dbo.LoyaltyBoost DROP CONSTRAINT ' + QUOTENAME(@constraint) + N';';
        EXEC sp_executesql @sql;
        PRINT '  [DROPPED] default constraint ' + @constraint;
    END

    ALTER TABLE dbo.LoyaltyBoost DROP COLUMN FirstTransactionOnly;
    PRINT '  [DROPPED] dbo.LoyaltyBoost.FirstTransactionOnly';
END
ELSE
    PRINT '  [SKIPPED] dbo.LoyaltyBoost.FirstTransactionOnly already gone';
GO

/* -------------------------------------------------------------- verification */
PRINT '';
PRINT '=== Verification ===';

SELECT
    CASE WHEN COL_LENGTH('dbo.PartnerTransaction','IsWelcomeBonus') IS NULL
         THEN 'MISSING' ELSE 'OK' END          AS IsWelcomeBonus_Column,
    CASE WHEN COL_LENGTH('dbo.LoyaltyBoost','FirstTransactionOnly') IS NULL
         THEN 'OK (dropped)' ELSE 'STILL PRESENT' END AS FirstTransactionOnly_Flag,
    (SELECT COUNT(*) FROM dbo.LoyaltyBoost WHERE IsDeleted = 0 AND IsActive = 1)
                                              AS LiveCampaigns;

PRINT '';
PRINT 'The welcome bonus is now intrinsic: 2x on a customer''s first ever charge,';
PRINT 'at every provider, with no row required and nothing to seed.';
PRINT '';
PRINT 'To change it, set the AppSetting key WelcomeBonusMultiplier:';
PRINT '  3 = triple points   1 = switch the bonus off   absent = 2 (default)';
