-- =====================================================================
-- PREPARED SCRIPT — NOT YET EXECUTED
-- Reset loyalty points earned at charging point "محطة يلا تشارج" (Id 59)
-- >>> SCOPED TO THE TOP-2 USERS ONLY: 22452 and 22451 <<<
-- Target: db_ab1977_cableproduction
-- Prepared: 2026-07-08 from live production data
-- =====================================================================
-- WHAT IT DOES
--   Claws back the station-59 points from users 22452 and 22451 only
--   (the two accounts behind 96% of the money/points, incl. the midnight
--   batch of 2026-07-04). Capped at each user's current balance — never
--   goes negative. Adds one compensating AdminAdjust ledger row per user
--   (full audit trail — nothing deleted) and updates balances + totals.
--
-- EXPECTED EFFECT (recomputed from LIVE balances at run time):
--   UserId 22452 (962799285355): -1,582  → balance 0  (earned 1,782; 200 already spent — unrecoverable)
--   UserId 22451 (962780781082): -  687  → balance 0  (earned   887; 200 already spent — unrecoverable)
--   TOTAL removed: ~2,269 points
--   The other 5 station-59 users (103 pts total) are NOT touched.
--
-- WHAT IT DOES NOT DO
--   * Does NOT touch their spend history (Defender car-wash redemptions stay)
--   * Does NOT delete any ledger rows or partner transactions
--   * Does NOT refund commission to the provider wallet
--     (optional block at the bottom, commented out — scoped to these 2 users)
--   * Does NOT fix the 4 pending settlement rollups (see note at bottom)
--
-- SAFETY: transactional, idempotent (marker ReferenceType 'Reversal:Station59'),
--         claw-back computed from LIVE balances at execution time.
-- =====================================================================

SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @AdminUserId INT = 2;              -- <<< the admin performing this (2 = Yahia)
DECLARE @Now DATETIME = GETUTCDATE();
DECLARE @Reason NVARCHAR(400) = N'Reversal of points earned at charging point 59 (محطة يلا تشارج) — suspicious activity, top-2 accounts';

-- Per-user claw-back = MIN(points earned at station 59, current balance),
-- LIMITED to users 22452 and 22451, skipping anyone already processed.
;WITH Earned AS (
    SELECT a.Id AS AccountId, a.UserId, SUM(t.Points) AS EarnedAt59
    FROM LoyaltyPointTransaction t
    JOIN UserLoyaltyAccount a ON a.Id = t.UserLoyaltyAccountId
    WHERE t.IsDeleted = 0
      AND t.ReferenceType = 'ChargingPoint' AND t.ReferenceId = 59
      AND t.Points > 0
      AND a.UserId IN (22452, 22451)                  -- <<< top-2 scope
    GROUP BY a.Id, a.UserId
),
Target AS (
    SELECT e.AccountId, e.UserId, e.EarnedAt59, a.CurrentBalance,
           ClawBack = CASE WHEN a.CurrentBalance < e.EarnedAt59
                           THEN a.CurrentBalance ELSE e.EarnedAt59 END
    FROM Earned e
    JOIN UserLoyaltyAccount a WITH (UPDLOCK, ROWLOCK) ON a.Id = e.AccountId
    WHERE a.IsDeleted = 0
      AND NOT EXISTS (SELECT 1 FROM LoyaltyPointTransaction r
                      WHERE r.UserLoyaltyAccountId = e.AccountId
                        AND r.ReferenceType = 'Reversal:Station59'
                        AND r.IsDeleted = 0)
)
SELECT * INTO #Target FROM Target WHERE ClawBack > 0;

-- 1. Compensating ledger rows (TransactionType 4 = AdminAdjust)
INSERT INTO LoyaltyPointTransaction
    (UserLoyaltyAccountId, LoyaltyPointActionId, LoyaltySeasonId, TransactionType,
     Points, BalanceAfter, ReferenceType, ReferenceId, Note, ExpiresAt,
     CreatedAt, CreatedBy, IsDeleted)
SELECT AccountId, NULL, NULL, 4,
       -ClawBack, CurrentBalance - ClawBack, 'Reversal:Station59', 59, @Reason, NULL,
       @Now, @AdminUserId, 0
FROM #Target;

-- 2. Wallet balances + lifetime earned totals
UPDATE a
SET a.CurrentBalance   = a.CurrentBalance   - t.ClawBack,
    a.TotalPointsEarned = a.TotalPointsEarned - t.ClawBack,
    a.ModifiedAt = @Now, a.ModifiedBy = @AdminUserId
FROM UserLoyaltyAccount a
JOIN #Target t ON t.AccountId = a.Id;

-- 3. Verification — review before the transaction commits
SELECT t.UserId, t.EarnedAt59, t.CurrentBalance AS BalanceBefore,
       t.ClawBack, a.CurrentBalance AS BalanceAfter
FROM #Target t
JOIN UserLoyaltyAccount a ON a.Id = t.AccountId;

SELECT SUM(ClawBack) AS TotalPointsRemoved, COUNT(*) AS UsersAffected FROM #Target;

DROP TABLE #Target;

COMMIT TRANSACTION;   -- <<< change to ROLLBACK TRANSACTION for a dry run

-- =====================================================================
-- OPTIONAL BLOCK A — refund the commission Cable took on THESE 2 USERS'
-- transactions (~106.85 JOD of the 111.09 total) back to the provider
-- wallet. Uncomment ONLY if the business confirmed Cable returns it.
-- =====================================================================
-- BEGIN TRANSACTION;
-- DECLARE @Commission DECIMAL(18,3) =
--     (SELECT SUM(CommissionAmount) FROM PartnerTransaction
--      WHERE ProviderType='ChargingPoint' AND ProviderId=59 AND Status=2 AND IsDeleted=0
--        AND UserId IN (22452, 22451));
-- UPDATE ChargingPoint SET WalletBalance = WalletBalance + @Commission WHERE Id = 59;
-- INSERT INTO ProviderWalletTransaction
--     (ProviderType, ProviderId, TransactionType, Amount, BalanceAfter,
--      ReferenceType, ReferenceId, Note, RecordedByUserId, CreatedAt, CreatedBy, IsDeleted)
-- SELECT 'ChargingPoint', 59, 3 /*Refund*/, @Commission, WalletBalance,
--        'Reversal:Station59', 59, N'Commission refund — station 59 top-2 reversal', 2, GETUTCDATE(), 2, 0
-- FROM ChargingPoint WHERE Id = 59;
-- COMMIT TRANSACTION;

-- =====================================================================
-- NOTE — settlements: the 4 PENDING settlements for station 59 keep their
-- accumulated totals (recompute is not implemented). Recommended: mark them
-- Disputed via the admin portal with a note referencing this reversal, or
-- leave Pending and never mark Paid.
--
-- NOTE — follow-up options via existing admin endpoints (work today):
--   * Block users 22452 / 22451 from loyalty: POST /api/loyalty/admin/BlockUser
--   * Block station 59 from loyalty:          POST /api/loyalty/admin/BlockProvider
-- =====================================================================

-- =====================================================================
-- ALTERNATIVE (after the new build is on production): per-transaction,
-- API-audited reversal — POST /api/loyalty/admin/ReverseTransaction with
-- {"activityType":"Partner","transactionId":<id>,"reason":"station 59 refund"}
-- User 22452: 28, 31, 32, 37, 40, 42, 44, 47
-- User 22451: 29, 33, 36, 41, 43, 46
-- Expect the final call(s) per user to fail with "insufficient balance" —
-- that is the 200 already-spent points each, by design.
-- =====================================================================
