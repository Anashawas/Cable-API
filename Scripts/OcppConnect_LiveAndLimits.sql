-- Cable Connect (OCPP 1.6J) — client review, Oct 2026:
--   * battery % at start / end of each session (stored, so lists stay fast)
--   * charge limit: stop automatically at an amount (JOD), energy (kWh) or battery %
--     (set on a running session, or chosen with a remote start and waiting on the plug).
-- Requires OcppConnect_Phase3.sql. Idempotent. Apply to dev first, then production with the matching builds.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('dbo.OcppTransaction', 'SocStartPercent') IS NULL
BEGIN
    ALTER TABLE dbo.OcppTransaction ADD
        SocStartPercent      INT           NULL,   -- battery % at the first reading that carried one
        SocEndPercent        INT           NULL,   -- latest / final battery %
        ChargeLimitKind      NVARCHAR(10)  NULL,   -- Jod | Kwh | Soc
        ChargeLimitValue     DECIMAL(10,3) NULL,
        ChargeLimitSetBy     INT           NULL,
        ChargeLimitSetAt     DATETIME2(0)  NULL,
        ChargeLimitReachedAt DATETIME2(0)  NULL;   -- the automatic stop was sent
    PRINT 'Added OcppTransaction.Soc* / ChargeLimit*';
END
ELSE PRINT 'SKIP OcppTransaction.Soc* / ChargeLimit* exist';
GO

IF COL_LENGTH('dbo.OcppConnector', 'PendingLimitKind') IS NULL
BEGIN
    ALTER TABLE dbo.OcppConnector ADD
        PendingLimitKind  NVARCHAR(10)  NULL,
        PendingLimitValue DECIMAL(10,3) NULL,
        PendingLimitSetBy INT           NULL,
        PendingLimitSetAt DATETIME2(0)  NULL;
    PRINT 'Added OcppConnector.PendingLimit*';
END
ELSE PRINT 'SKIP OcppConnector.PendingLimit* exist';
GO

-- Backfill battery % for sessions recorded so far (first / last reading with a SoC above 0).
UPDATE t SET
    t.SocStartPercent = s.FirstSoc,
    t.SocEndPercent   = s.LastSoc
FROM dbo.OcppTransaction t
CROSS APPLY (
    SELECT
        (SELECT TOP 1 m.SocPercent FROM dbo.OcppMeterValue m
          WHERE m.OcppTransactionId = t.Id AND m.SocPercent > 0 ORDER BY m.MeasuredAt, m.Id) AS FirstSoc,
        (SELECT TOP 1 m.SocPercent FROM dbo.OcppMeterValue m
          WHERE m.OcppTransactionId = t.Id AND m.SocPercent > 0 ORDER BY m.MeasuredAt DESC, m.Id DESC) AS LastSoc
) s
WHERE t.SocStartPercent IS NULL AND s.FirstSoc IS NOT NULL;
PRINT CONCAT('Backfilled battery % on ', @@ROWCOUNT, ' session(s)');
GO
