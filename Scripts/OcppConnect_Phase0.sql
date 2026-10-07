-- Cable Connect (OCPP 1.6J) — Phase 0: connection test + host-recycle measurement.
-- Idempotent. Apply to dev first, then to production before the real-charger test.
-- Phase B1 adds OcppChargePoint / OcppConnector / OcppTransaction / OcppMeterValue.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.OcppRawMessage', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OcppRawMessage
    (
        Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OcppRawMessage PRIMARY KEY,
        ChargePointId NVARCHAR(40)   NOT NULL,   -- the id in the URL, whatever the charger sent
        Direction     CHAR(3)        NOT NULL,   -- 'in' | 'out' | 'sys' (connect/disconnect events)
        MessageType   TINYINT        NULL,       -- 2 CALL | 3 CALLRESULT | 4 CALLERROR
        MessageId     NVARCHAR(64)   NULL,
        [Action]      NVARCHAR(64)   NULL,
        Payload       NVARCHAR(MAX)  NULL,       -- the full frame as received / sent
        RemoteIp      NVARCHAR(64)   NULL,
        CreatedAt     DATETIME2(3)   NOT NULL    -- arrival time (UTC); the charger's own timestamp lives inside Payload
    );
    CREATE INDEX IX_OcppRawMessage_ChargePoint_CreatedAt ON dbo.OcppRawMessage (ChargePointId, CreatedAt DESC);
    PRINT 'Created dbo.OcppRawMessage';
END
ELSE PRINT 'SKIP dbo.OcppRawMessage exists';
GO

IF OBJECT_ID('dbo.OcppProcessStart', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OcppProcessStart
    (
        Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OcppProcessStart PRIMARY KEY,
        StartedAt       DATETIME2(0)  NOT NULL,   -- UTC
        MachineName     NVARCHAR(100) NULL,
        ProcessId       INT           NULL,
        EnvironmentName NVARCHAR(30)  NULL,
        Version         NVARCHAR(30)  NULL
    );
    PRINT 'Created dbo.OcppProcessStart';
END
ELSE PRINT 'SKIP dbo.OcppProcessStart exists';
GO

-- Handy while watching the pilot:
-- SELECT TOP 200 * FROM dbo.OcppRawMessage ORDER BY Id DESC;
-- SELECT StartedAt, DATEDIFF(MINUTE, LAG(StartedAt) OVER (ORDER BY StartedAt), StartedAt) AS MinutesSincePrevious FROM dbo.OcppProcessStart ORDER BY StartedAt DESC;
