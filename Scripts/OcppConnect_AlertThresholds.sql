-- Cable Connect (OCPP 1.6J) — per-station alert thresholds.
-- NULL = the platform default (OcppLimits: offline 15 min, plug faulted 15 min,
-- session open 360 min, car parked after charging 20 min). Set by the admin on the
-- station's Cable Connect tab. Requires OcppConnect_Phase2.sql. Idempotent.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('dbo.ChargingPoint', 'OcppOfflineAlertMin') IS NULL
BEGIN
    ALTER TABLE dbo.ChargingPoint ADD
        OcppOfflineAlertMin     INT NULL,   -- charger unreachable / silent this long → alert
        OcppFaultedAlertMin     INT NULL,   -- plug Faulted this long → escalation alert
        OcppLongSessionAlertMin INT NULL,   -- session open this long → alert
        OcppParkedAlertMin      INT NULL;   -- Finishing / SuspendedEV this long → driver (then station)
    PRINT 'Added ChargingPoint.Ocpp*AlertMin';
END
ELSE PRINT 'SKIP ChargingPoint.Ocpp*AlertMin exist';
GO
