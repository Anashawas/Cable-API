-- Cable Connect (OCPP 1.6J) — Phase 3: start without a card + session price.
-- OcppTransaction learns who started the session and how (Card | App | Operator),
-- and carries the price computed from the time-of-use tariff (PriceAlerts_Phase1.sql).
-- Requires OcppConnect_Phase2.sql and PriceAlerts_Phase1.sql. Idempotent.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('dbo.OcppTransaction', 'StartSource') IS NULL
BEGIN
    ALTER TABLE dbo.OcppTransaction ADD
        StartSource       NVARCHAR(10)  NOT NULL CONSTRAINT DF_OcppTransaction_StartSource DEFAULT 'Card', -- Card | App | Operator
        StartedByUserId   INT           NULL,          -- driver (app, or a card linked in OcppUserIdTag) or the operator who started it
        CostFils          INT           NULL,          -- price from the TOU tariff in force; NULL while open / energy unknown
        TariffVersion     INT           NULL,          -- TouTariff.Version used
        CostBreakdownJson NVARCHAR(MAX) NULL,          -- [{key, kwh, priceFils, fils}]
        PricedAt          DATETIME2(0)  NULL;
    PRINT 'Added OcppTransaction.StartSource / StartedByUserId / Cost*';
END
ELSE PRINT 'SKIP OcppTransaction phase-3 columns exist';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OcppTransaction_StartedBy_StartedAt' AND object_id = OBJECT_ID('dbo.OcppTransaction'))
BEGIN
    CREATE INDEX IX_OcppTransaction_StartedBy_StartedAt ON dbo.OcppTransaction (StartedByUserId, StartedAt DESC) WHERE StartedByUserId IS NOT NULL;
    PRINT 'Created IX_OcppTransaction_StartedBy_StartedAt';
END
GO

-- Backfill the driver link for sessions started with a card that is tied to a user.
UPDATE t SET t.StartedByUserId = u.UserId
FROM dbo.OcppTransaction t
JOIN dbo.OcppUserIdTag u ON u.IdTag = t.IdTag AND u.IsDeleted = 0 AND u.IsEnabled = 1
WHERE t.StartedByUserId IS NULL;
GO
