/* =============================================================================
   Cable Connect - repair RH4 sessions broken by offline replay (production, Oct 15-19)
   Target: db_ab1977_cableproduction. Run AFTER publishing the Cable.Ocpp build of Oct 19
   (replay matching + "first Stop is final"), so the charger cannot re-close them meanwhile.

   What happened (RH4, hjl fw 1.0.26.0410):
   - Sessions run while the station had no internet were replayed on reconnect as
     StartTransaction (we issued #273 / #275 / #278) followed by a StopTransaction quoting
     the charger's OWN local id -> stored as orphans #274 / #276 (+#277 duplicate) / #279,
     and our sessions stayed open.
   - Oct 19 06:25 UTC the charger closed #273 itself, quoting 273 with that day's meter
     (4390951) -> #273 got #278's 16.4 kWh.
   - Oct 15: rejected session #160 (idTag "password", before that card was added) was closed
     by orphan #162.

   This script puts each stop back on its session (values taken from the orphan rows),
   removes the orphans it consumed (#162, #274, #276, #279; duplicate #277 stays an orphan),
   and clears the price of every repaired session so the price-ocpp-sessions job (every
   minute) prices it again from the TOU tariff. #160 stays rejected (excluded from counts).

   Every step checks the rows are still exactly as found on Oct 19; if not, it stops and
   changes nothing. Safe to run twice (the second run finds nothing to do).
   ============================================================================= */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE @db SYSNAME = DB_NAME();
IF @db NOT IN (N'db_ab1977_cableproduction')
BEGIN
    RAISERROR('Wrong database: %s. Nothing was changed.', 16, 1, @db) WITH NOWAIT;
    SET NOEXEC ON;
END
GO

DECLARE @rh4 INT = (SELECT Id FROM dbo.OcppChargePoint WHERE ChargePointId = N'RH4' AND IsDeleted = 0);
IF @rh4 IS NULL
BEGIN
    RAISERROR('RH4 not found - stopped.', 16, 1) WITH NOWAIT;
    SET NOEXEC ON;
END
GO

-- Already repaired? (all four orphans gone) -> nothing to do.
IF NOT EXISTS (SELECT 1 FROM dbo.OcppTransaction WHERE Id IN (162, 274, 276, 279))
BEGIN
    PRINT 'Already repaired - nothing to do.';
    SET NOEXEC ON;
END
GO

DECLARE @rh4 INT = (SELECT Id FROM dbo.OcppChargePoint WHERE ChargePointId = N'RH4' AND IsDeleted = 0);

-- Pairs: session -> the orphan holding its real stop.
DECLARE @pairs TABLE (SessionId INT PRIMARY KEY, OrphanId INT NOT NULL, ExpectMeterStart BIGINT NOT NULL, ExpectStop BIGINT NOT NULL);
INSERT INTO @pairs VALUES
    (160, 162, 4115390, 4127206),   -- rejected Oct 15 session (stays rejected)
    (273, 274, 4374540, 4374569),   -- 0.029 kWh (closed wrongly on Oct 19 with 4390951)
    (275, 276, 4374569, 4374569),   -- 0.000 kWh
    (278, 279, 4374569, 4390951);   -- 16.382 kWh

-- Guard: every session and orphan still looks exactly as on Oct 19.
IF EXISTS (
    SELECT 1 FROM @pairs p
    LEFT JOIN dbo.OcppTransaction s ON s.Id = p.SessionId
    LEFT JOIN dbo.OcppTransaction o ON o.Id = p.OrphanId
    WHERE s.Id IS NULL OR o.Id IS NULL
       OR s.OcppChargePointId <> @rh4 OR o.OcppChargePointId <> @rh4
       OR s.IsOrphan = 1 OR o.IsOrphan = 0
       OR UPPER(s.IdTag) <> N'PASSWORD' OR UPPER(o.IdTag) <> N'PASSWORD'
       OR s.MeterStartWh <> p.ExpectMeterStart OR o.MeterStopWh <> p.ExpectStop
       OR o.StoppedAt < s.StartedAt
       OR (p.SessionId IN (160, 275, 278) AND s.IsOpen = 0)                      -- still open
       OR (p.SessionId = 273 AND NOT (s.IsOpen = 0 AND s.MeterStopWh = 4390951)) -- the wrong Oct 19 close
)
BEGIN
    RAISERROR('Rows changed since Oct 19 (or a session was already closed) - stopped, nothing changed. Send Claude the current rows.', 16, 1) WITH NOWAIT;
    SET NOEXEC ON;
END
GO

DECLARE @pairs TABLE (SessionId INT PRIMARY KEY, OrphanId INT NOT NULL);
INSERT INTO @pairs VALUES (160, 162), (273, 274), (275, 276), (278, 279);

BEGIN TRAN;

UPDATE s SET
    s.MeterStopWh    = o.MeterStopWh,
    s.StoppedAt      = o.StoppedAt,
    s.ReceivedStopAt = o.ReceivedStartAt,     -- when the stop actually reached us
    s.StopReason     = o.StopReason,
    s.IsOpen         = 0,
    s.EnergyKwh      = CASE WHEN o.MeterStopWh >= s.MeterStartWh THEN (o.MeterStopWh - s.MeterStartWh) / 1000.0 END,
    s.CostFils = NULL, s.TariffVersion = NULL, s.CostBreakdownJson = NULL, s.PricedAt = NULL   -- re-priced by the job
FROM dbo.OcppTransaction s
JOIN @pairs p ON p.SessionId = s.Id
JOIN dbo.OcppTransaction o ON o.Id = p.OrphanId;
PRINT CONCAT('Sessions repaired: ', @@ROWCOUNT);

DELETE o FROM dbo.OcppTransaction o
JOIN @pairs p ON p.OrphanId = o.Id
WHERE o.IsOrphan = 1
  AND NOT EXISTS (SELECT 1 FROM dbo.OcppMeterValue m WHERE m.OcppTransactionId = o.Id);
PRINT CONCAT('Orphans removed: ', @@ROWCOUNT);

COMMIT;
GO

SELECT t.Id, t.ConnectorId, t.IdTag, t.IsOpen, t.WasRejected, t.MeterStartWh, t.MeterStopWh, t.EnergyKwh,
       CONVERT(varchar(16), DATEADD(HOUR, 3, t.StartedAt), 120) AS StartedJordan,
       CONVERT(varchar(16), DATEADD(HOUR, 3, t.StoppedAt), 120) AS StoppedJordan,
       t.StopReason, t.CostFils
FROM dbo.OcppTransaction t
WHERE t.Id IN (160, 162, 273, 274, 275, 276, 277, 278, 279)
ORDER BY t.Id;
-- Expected: 160 / 273 / 275 / 278 closed (11.816 / 0.029 / 0.000 / 16.382 kWh), 277 still an orphan,
-- 162 / 274 / 276 / 279 gone. CostFils fills in within a minute (price-ocpp-sessions job).
GO
SET NOEXEC OFF;
GO
