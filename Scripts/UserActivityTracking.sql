/* =============================================================================
   User activity tracking

   Two nullable columns on UserAccount so the app can answer "when did this user
   last sign in" and "how many people actually use the app".

   Additive and nullable: existing rows read as NULL, which is correct — nothing
   was tracked before this, and NULL says exactly that rather than inventing a
   date. No backfill is possible or wanted.

   Safe to run before the code is deployed.

   Guarded and idempotent: safe to run more than once.
   ============================================================================= */

SET NOCOUNT ON;
PRINT '=== User activity tracking ===';

/* LastLoginAt — written on every successful sign-in.
   NOT a usage metric: access tokens are long-lived, so an active user may not
   re-authenticate for months. */
IF COL_LENGTH('dbo.UserAccount', 'LastLoginAt') IS NULL
BEGIN
    ALTER TABLE dbo.UserAccount ADD LastLoginAt DATETIME2(7) NULL;
    PRINT '  [ADDED]   dbo.UserAccount.LastLoginAt';
END
ELSE
    PRINT '  [SKIPPED] dbo.UserAccount.LastLoginAt already exists';

/* LastSeenAt — written on any authenticated request, throttled to one write per
   user per 15 minutes. This is the DAU/WAU/MAU signal. */
IF COL_LENGTH('dbo.UserAccount', 'LastSeenAt') IS NULL
BEGIN
    ALTER TABLE dbo.UserAccount ADD LastSeenAt DATETIME2(7) NULL;
    PRINT '  [ADDED]   dbo.UserAccount.LastSeenAt';
END
ELSE
    PRINT '  [SKIPPED] dbo.UserAccount.LastSeenAt already exists';
GO

/* Every activity report is a range scan on LastSeenAt across the whole table. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_UserAccount_LastSeenAt'
                 AND object_id = OBJECT_ID('dbo.UserAccount'))
BEGIN
    CREATE INDEX IX_UserAccount_LastSeenAt ON dbo.UserAccount (LastSeenAt);
    PRINT '  [ADDED]   IX_UserAccount_LastSeenAt';
END
ELSE
    PRINT '  [SKIPPED] IX_UserAccount_LastSeenAt already exists';
GO

/* -------------------------------------------------------------- verification */
PRINT '';
PRINT '=== Verification ===';

SELECT
    CASE WHEN COL_LENGTH('dbo.UserAccount','LastLoginAt') IS NULL THEN 'MISSING' ELSE 'OK' END AS LastLoginAt,
    CASE WHEN COL_LENGTH('dbo.UserAccount','LastSeenAt')  IS NULL THEN 'MISSING' ELSE 'OK' END AS LastSeenAt,
    (SELECT COUNT(*) FROM dbo.UserAccount WHERE IsDeleted = 0) AS TotalUsers;

PRINT '';
PRINT 'Tracking starts from deploy. Every existing user reads NULL until they';
PRINT 'next use the app, so the first week of numbers will understate reality.';
PRINT '';
PRINT 'Read them at GET /api/users/activity-stats (admin), or directly:';
GO

/* Ad-hoc: the same figures the endpoint returns. */
SELECT
    COUNT(*)                                                                              AS TotalUsers,
    SUM(CASE WHEN LastSeenAt  >= DATEADD(day, -1,  SYSUTCDATETIME()) THEN 1 ELSE 0 END)   AS DailyActive,
    SUM(CASE WHEN LastSeenAt  >= DATEADD(day, -7,  SYSUTCDATETIME()) THEN 1 ELSE 0 END)   AS WeeklyActive,
    SUM(CASE WHEN LastSeenAt  >= DATEADD(day, -30, SYSUTCDATETIME()) THEN 1 ELSE 0 END)   AS MonthlyActive,
    SUM(CASE WHEN LastSeenAt  IS NULL                                THEN 1 ELSE 0 END)   AS NeverSeen,
    SUM(CASE WHEN CreatedAt   >= DATEADD(day, -30, SYSUTCDATETIME()) THEN 1 ELSE 0 END)   AS NewUsers30d,
    SUM(CASE WHEN LastLoginAt >= DATEADD(day, -30, SYSUTCDATETIME()) THEN 1 ELSE 0 END)   AS LoggedIn30d
FROM dbo.UserAccount
WHERE IsDeleted = 0;
