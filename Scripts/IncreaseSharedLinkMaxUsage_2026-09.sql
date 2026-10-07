/* =============================================================================
   Cable — Raise MaxUsage on shared links created from 2026-09-01
   -----------------------------------------------------------------------------
   Scope: dbo.SharedLink rows with CreatedAt >= @FromDate, not soft-deleted.
   CreatedAt is written as UTC by the auditing interceptor, so @FromDate is a
   UTC boundary — a link created 2026-09-01 01:00 Amman time (2026-08-31 22:00
   UTC) is NOT in scope.

   SAFETY
     - @DryRun = 1 by default: reports what WOULD change and rolls back.
       Set @DryRun = 0 to apply.
     - The UPDATE is guarded by "MaxUsage < @NewMaxUsage", so it can only ever
       raise a cap. Re-running is a no-op, and a link already above the target
       (set higher by hand) is left alone rather than being pulled down.
     - QUOTED_IDENTIFIER must be ON: this database carries filtered indexes, and
       sqlcmd connects with it OFF, which fails the write with Msg 1934.

   TARGET: @NewMaxUsage = 2147483647 (int.MaxValue). This is the column's
   maximum, so these links become effectively uncapped. The per-link usage
   limit stops being a safeguard for them — CurrentUsage is still counted and
   still visible, but it will never gate a redemption. IsActive and ExpiresAt
   remain the only controls left on these 28 links, and all 28 currently have
   ExpiresAt = NULL, so IsActive is the sole remaining kill switch.

   Verified against production 2026-09-06: 28 in-scope links, all currently
   MaxUsage = 1000, highest CurrentUsage 72. None at its cap.
   ============================================================================= */

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @NewMaxUsage INT      = 2147483647;            -- int.MaxValue: no practical cap
DECLARE @FromDate    DATETIME = '2026-09-01T00:00:00'; -- UTC
DECLARE @DryRun      BIT      = 1;                     -- <<< 0 to apply

BEGIN TRAN;

PRINT '--- BEFORE ---';
SELECT Id, LinkToken, TargetId, MaxUsage, CurrentUsage,
       CONVERT(varchar(30), CreatedAt, 121) AS CreatedAt
FROM dbo.SharedLink
WHERE IsDeleted = 0
  AND CreatedAt >= @FromDate
  AND MaxUsage  < @NewMaxUsage
ORDER BY CurrentUsage DESC, Id;

UPDATE dbo.SharedLink
SET MaxUsage   = @NewMaxUsage,
    ModifiedAt = GETUTCDATE()
WHERE IsDeleted = 0
  AND CreatedAt >= @FromDate
  AND MaxUsage  < @NewMaxUsage;

DECLARE @Changed INT = @@ROWCOUNT;

PRINT '--- AFTER ---';
SELECT COUNT(*)      AS LinksInScope,
       MIN(MaxUsage) AS MinMaxUsage,
       MAX(MaxUsage) AS MaxMaxUsage,
       MAX(CurrentUsage) AS HighestUsage
FROM dbo.SharedLink
WHERE IsDeleted = 0 AND CreatedAt >= @FromDate;

IF @DryRun = 1
BEGIN
    ROLLBACK TRAN;
    PRINT CONCAT('DRY RUN — rolled back. Would have updated ', @Changed, ' link(s). Set @DryRun = 0 to apply.');
END
ELSE
BEGIN
    COMMIT TRAN;
    PRINT CONCAT('APPLIED — updated ', @Changed, ' link(s) to MaxUsage = ', @NewMaxUsage, '.');
END
