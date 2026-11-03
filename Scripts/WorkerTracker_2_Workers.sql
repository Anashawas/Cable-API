-- Worker tracker, step 2 — several workers per station (Docs/WorkerTracker-Plan.md §2):
--   * ProviderManager: the one-worker unique index goes; a plain index on the provider takes its place.
--   * ProviderManager.SuspendedByPlanAt (the plan allows fewer workers than are active — decision 38) and
--     ProviderManager.KeepOnFree (the owner's choice of who stays).
--   * Existing workers with Privileges = NULL get today's keys written out, so the new keys
--     (Attendance, BiometricLogin, ViewAttendance, ApproveLeave, ManageAttendance) stay off until the owner grants them.
--   * The free plan gets the "complaints" feature (WORKER_TRACKER decision 17).
-- Idempotent: safe to run again. Apply to dev first; deploy the API build of this change only after the script.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- 1. Several workers per provider -----------------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ProviderManager_OneWorker' AND object_id = OBJECT_ID('dbo.ProviderManager'))
BEGIN
    DROP INDEX UX_ProviderManager_OneWorker ON dbo.ProviderManager;
    PRINT 'Dropped UX_ProviderManager_OneWorker';
END
ELSE PRINT 'SKIP UX_ProviderManager_OneWorker already gone';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProviderManager_Provider' AND object_id = OBJECT_ID('dbo.ProviderManager'))
BEGIN
    CREATE INDEX IX_ProviderManager_Provider ON dbo.ProviderManager (ProviderType, ProviderId) WHERE [IsDeleted] = 0;
    PRINT 'Created IX_ProviderManager_Provider';
END
ELSE PRINT 'SKIP IX_ProviderManager_Provider exists';
GO

-- 2. Plan suspension and the owner's choice ---------------------------------------------------------------------------
IF COL_LENGTH('dbo.ProviderManager', 'SuspendedByPlanAt') IS NULL
BEGIN
    ALTER TABLE dbo.ProviderManager ADD SuspendedByPlanAt DATETIME2(3) NULL;
    PRINT 'Added ProviderManager.SuspendedByPlanAt';
END
ELSE PRINT 'SKIP ProviderManager.SuspendedByPlanAt exists';
GO

IF COL_LENGTH('dbo.ProviderManager', 'KeepOnFree') IS NULL
BEGIN
    ALTER TABLE dbo.ProviderManager ADD KeepOnFree BIT NOT NULL CONSTRAINT DF_ProviderManager_KeepOnFree DEFAULT 0;
    PRINT 'Added ProviderManager.KeepOnFree';
END
ELSE PRINT 'SKIP ProviderManager.KeepOnFree exists';
GO

-- 3. Explicit privileges for existing workers (decision 16b) ---------------------------------------------------------
UPDATE dbo.ProviderManager
SET Privileges = N'ManageStation,PointOfSale,ManageOffers,ViewWallet,SendAnnouncements,ViewFeedback,ViewStatistics,ConnectView,ConnectControl',
    ModifiedAt = GETUTCDATE()
WHERE Privileges IS NULL AND IsDeleted = 0;
PRINT CONCAT('Workers given the explicit legacy privilege list: ', @@ROWCOUNT);
GO

-- 4. Free plan: complaints (decision 17) -----------------------------------------------------------------------------
UPDATE dbo.BillingPlan
SET Features = REPLACE(Features, N'"editInfo"', N'"editInfo","complaints"'),
    ModifiedAt = GETUTCDATE()
WHERE [Key] = 'free' AND Features NOT LIKE N'%"complaints"%' AND Features LIKE N'%"editInfo"%';
PRINT CONCAT('Free plan given complaints: ', @@ROWCOUNT);
GO

-- 5. Check --------------------------------------------------------------------------------------------------------
SELECT
    (SELECT COUNT(*) FROM dbo.ProviderManager WHERE IsDeleted = 0 AND Privileges IS NULL) AS WorkersWithNullPrivileges,
    (SELECT Features FROM dbo.BillingPlan WHERE [Key] = 'free') AS FreePlanFeatures;
GO
