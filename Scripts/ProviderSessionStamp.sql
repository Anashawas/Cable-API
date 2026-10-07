/* =============================================================================
   Per-app session stamps — adds UserAccount.ProviderSecurityStamp
   -----------------------------------------------------------------------------
   WHY
     Sessions were enforced with a single UserAccount.SecurityStamp column. Both
     the Cable consumer app and the Provider/Worker app validated against it, so
     signing into one app rotated the stamp and silently signed the user out of
     the other. Providers and workers who also use the consumer app were being
     kicked out of the provider app.

     This adds a second stamp used exclusively by the provider app, so each app
     holds an independent session. Single-device enforcement is preserved WITHIN
     each app (a second consumer login still ends the first).

   SAFETY
     - Additive only: one NULLABLE column. No data is modified or backfilled.
     - Idempotent: guarded, safe to re-run.
     - Backward compatible: existing tokens carry no "app" claim and continue to
       validate against SecurityStamp exactly as before.

   DEPLOY ORDER  (important)
     1. Run THIS script.
     2. Then publish the WebApi.
     Running the script first is safe — the column is simply unused until the
     new build is live. Publishing first would break every UserAccount query.

   ROLLBACK
     The column is unused by the previous build, so rolling the API back needs
     no DB change. To remove it entirely:
        ALTER TABLE dbo.UserAccount DROP COLUMN ProviderSecurityStamp;
============================================================================= */

SET NOCOUNT ON;

PRINT '== Per-app session stamps =====================================';

IF COL_LENGTH('dbo.UserAccount', 'ProviderSecurityStamp') IS NULL
BEGIN
    ALTER TABLE dbo.UserAccount ADD ProviderSecurityStamp NVARCHAR(50) NULL;
    PRINT '  [ADDED]   dbo.UserAccount.ProviderSecurityStamp NVARCHAR(50) NULL';
END
ELSE
BEGIN
    PRINT '  [SKIPPED] dbo.UserAccount.ProviderSecurityStamp already exists';
END
GO

-- Partner WEB portal session slot (X-Client-App: provider-web), so the web
-- portal and the provider MOBILE app can be signed in simultaneously.
IF COL_LENGTH('dbo.UserAccount', 'ProviderWebSecurityStamp') IS NULL
BEGIN
    ALTER TABLE dbo.UserAccount ADD ProviderWebSecurityStamp NVARCHAR(50) NULL;
    PRINT '  [ADDED]   dbo.UserAccount.ProviderWebSecurityStamp NVARCHAR(50) NULL';
END
ELSE
BEGIN
    PRINT '  [SKIPPED] dbo.UserAccount.ProviderWebSecurityStamp already exists';
END
GO

/* ----------------------------- verification ------------------------------ */
PRINT '';
PRINT '== Verification ===============================================';

SELECT
    c.name                                          AS ColumnName,
    t.name                                          AS DataType,
    c.max_length / 2                                AS MaxChars,
    CASE WHEN c.is_nullable = 1 THEN 'YES' ELSE 'NO' END AS IsNullable,
    CASE WHEN c.name IS NOT NULL THEN 'OK' ELSE 'MISSING' END AS Status
FROM sys.columns c
JOIN sys.types   t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('dbo.UserAccount')
  AND c.name IN ('SecurityStamp', 'ProviderSecurityStamp', 'ProviderWebSecurityStamp')
ORDER BY c.name;

IF COL_LENGTH('dbo.UserAccount', 'ProviderSecurityStamp') IS NOT NULL
   AND COL_LENGTH('dbo.UserAccount', 'ProviderWebSecurityStamp') IS NOT NULL
    PRINT '  RESULT: OK - columns present, ready for the API publish.';
ELSE
    PRINT '  RESULT: FAILED - column missing. Do NOT publish the API.';

PRINT '===============================================================';
GO
