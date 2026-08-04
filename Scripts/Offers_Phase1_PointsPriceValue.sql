-- =============================================
-- Offers Phase 1: pointsPriceValue (B1, BE notes 2026-07-07)
-- Cable EV Charging Station Management
-- =============================================
-- Adds ProviderOffer.PointsPriceValue — the cash value of the offer's points
-- (pointsCost ÷ conversion rate), distinct from MonetaryValue (Cable's payout
-- to the provider). Backfills existing offers from the active conversion rate
-- for the offer's currency (default rate preferred).
--
-- >>> RUN AGAINST THE DEV DATABASE ONLY — NOT PRODUCTION (until release) <<<
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
