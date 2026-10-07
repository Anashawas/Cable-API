-- Cable Connect (OCPP 1.6J) — Phase B1: the business tables.
-- Requires OcppConnect_Phase0.sql (OcppRawMessage, OcppProcessStart). Idempotent.
-- Apply to dev first, then production before the first real session is recorded.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.OcppChargePoint', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OcppChargePoint
    (
        Id                    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OcppChargePoint PRIMARY KEY,
        ChargingPointId       INT            NOT NULL,
        ChargePointId         NVARCHAR(40)   NOT NULL,   -- the id in the WebSocket URL
        PasswordHash          NVARCHAR(500)  NULL,       -- NULL = no Basic auth (Security Profile 0)
        DisplayName           NVARCHAR(100)  NULL,
        Vendor                NVARCHAR(100)  NULL,       -- from BootNotification
        Model                 NVARCHAR(100)  NULL,
        FirmwareVersion       NVARCHAR(100)  NULL,
        SerialNumber          NVARCHAR(100)  NULL,
        ChargeBoxSerialNumber NVARCHAR(100)  NULL,
        Iccid                 NVARCHAR(40)   NULL,
        Imsi                  NVARCHAR(40)   NULL,
        MeterSerialNumber     NVARCHAR(100)  NULL,
        HeartbeatInterval     INT            NOT NULL CONSTRAINT DF_OcppChargePoint_HeartbeatInterval DEFAULT 60,
        IsEnabled             BIT            NOT NULL CONSTRAINT DF_OcppChargePoint_IsEnabled DEFAULT 1,
        IsConnected           BIT            NOT NULL CONSTRAINT DF_OcppChargePoint_IsConnected DEFAULT 0,
        ConnectedAt           DATETIME2(3)   NULL,
        DisconnectedAt        DATETIME2(3)   NULL,
        LastBootAt            DATETIME2(3)   NULL,
        LastMessageAt         DATETIME2(3)   NULL,
        LastRemoteIp          NVARCHAR(64)   NULL,
        FailedAuthCount       INT            NOT NULL CONSTRAINT DF_OcppChargePoint_FailedAuthCount DEFAULT 0,
        LockedUntil           DATETIME2(0)   NULL,
        CreatedBy             INT            NULL,
        CreatedAt             DATETIME       NOT NULL,
        ModifiedBy            INT            NULL,
        ModifiedAt            DATETIME       NULL,
        IsDeleted             BIT            NOT NULL CONSTRAINT DF_OcppChargePoint_IsDeleted DEFAULT 0,
        CONSTRAINT FK_OcppChargePoint_ChargingPoint FOREIGN KEY (ChargingPointId) REFERENCES dbo.ChargingPoint (Id)
    );
    CREATE UNIQUE INDEX UX_OcppChargePoint_ChargePointId ON dbo.OcppChargePoint (ChargePointId) WHERE [IsDeleted] = 0;
    CREATE INDEX IX_OcppChargePoint_ChargingPoint ON dbo.OcppChargePoint (ChargingPointId);
    PRINT 'Created dbo.OcppChargePoint';
END
ELSE PRINT 'SKIP dbo.OcppChargePoint exists';
GO

IF OBJECT_ID('dbo.OcppConnector', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OcppConnector
    (
        Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OcppConnector PRIMARY KEY,
        OcppChargePointId INT            NOT NULL,
        ConnectorId       INT            NOT NULL,   -- 0 = the charger itself, 1..n = plugs
        Status            NVARCHAR(30)   NOT NULL,   -- Available | Preparing | Charging | SuspendedEVSE | SuspendedEV | Finishing | Reserved | Unavailable | Faulted
        ErrorCode         NVARCHAR(50)   NOT NULL,
        VendorErrorCode   NVARCHAR(50)   NULL,
        Info              NVARCHAR(500)  NULL,
        StatusUpdatedAt   DATETIME2(3)   NULL,       -- charger time (R1)
        StatusReceivedAt  DATETIME2(3)   NOT NULL,   -- arrival time (R7)
        PlugTypeId        INT            NULL,
        PowerKw           DECIMAL(8,2)   NULL,
        CONSTRAINT FK_OcppConnector_OcppChargePoint FOREIGN KEY (OcppChargePointId) REFERENCES dbo.OcppChargePoint (Id) ON DELETE CASCADE,
        CONSTRAINT FK_OcppConnector_PlugType FOREIGN KEY (PlugTypeId) REFERENCES dbo.PlugType (Id) ON DELETE SET NULL
    );
    CREATE UNIQUE INDEX UX_OcppConnector_ChargePoint_Connector ON dbo.OcppConnector (OcppChargePointId, ConnectorId);
    PRINT 'Created dbo.OcppConnector';
END
ELSE PRINT 'SKIP dbo.OcppConnector exists';
GO

IF OBJECT_ID('dbo.OcppTransaction', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OcppTransaction
    (
        Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OcppTransaction PRIMARY KEY,  -- = the OCPP transactionId
        OcppChargePointId INT            NOT NULL,
        ConnectorId       INT            NOT NULL,
        IdTag             NVARCHAR(50)   NOT NULL,
        MeterStartWh      BIGINT         NOT NULL,
        MeterStopWh       BIGINT         NULL,
        StartedAt         DATETIME2(3)   NOT NULL,   -- charger time
        StoppedAt         DATETIME2(3)   NULL,
        ReceivedStartAt   DATETIME2(3)   NOT NULL,
        ReceivedStopAt    DATETIME2(3)   NULL,
        StopReason        NVARCHAR(30)   NULL,
        EnergyKwh         DECIMAL(12,3)  NULL,       -- (stop - start) / 1000
        IsOpen            BIT            NOT NULL CONSTRAINT DF_OcppTransaction_IsOpen DEFAULT 1,
        IsStale           BIT            NOT NULL CONSTRAINT DF_OcppTransaction_IsStale DEFAULT 0,
        IsOrphan          BIT            NOT NULL CONSTRAINT DF_OcppTransaction_IsOrphan DEFAULT 0,
        WasRejected       BIT            NOT NULL CONSTRAINT DF_OcppTransaction_WasRejected DEFAULT 0,
        CONSTRAINT FK_OcppTransaction_OcppChargePoint FOREIGN KEY (OcppChargePointId) REFERENCES dbo.OcppChargePoint (Id)
    );
    CREATE INDEX IX_OcppTransaction_ChargePoint_StartedAt ON dbo.OcppTransaction (OcppChargePointId, StartedAt DESC);
    CREATE INDEX IX_OcppTransaction_Open ON dbo.OcppTransaction (IsOpen) WHERE [IsOpen] = 1;
    PRINT 'Created dbo.OcppTransaction';
END
ELSE PRINT 'SKIP dbo.OcppTransaction exists';
GO

IF OBJECT_ID('dbo.OcppMeterValue', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OcppMeterValue
    (
        Id                BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OcppMeterValue PRIMARY KEY,
        OcppChargePointId INT            NOT NULL,
        ConnectorId       INT            NOT NULL,
        OcppTransactionId INT            NULL,
        MeasuredAt        DATETIME2(3)   NOT NULL,   -- charger time (R1)
        ReceivedAt        DATETIME2(3)   NOT NULL,
        Context           NVARCHAR(30)   NULL,       -- Sample.Periodic | Transaction.Begin | Transaction.End | ...
        EnergyWh          BIGINT         NULL,       -- cumulative register
        PowerW            INT            NULL,
        CurrentA          DECIMAL(9,2)   NULL,
        VoltageV          DECIMAL(9,2)   NULL,
        SocPercent        INT            NULL,
        TemperatureC      DECIMAL(6,1)   NULL,
        CONSTRAINT FK_OcppMeterValue_OcppChargePoint FOREIGN KEY (OcppChargePointId) REFERENCES dbo.OcppChargePoint (Id),
        CONSTRAINT FK_OcppMeterValue_OcppTransaction FOREIGN KEY (OcppTransactionId) REFERENCES dbo.OcppTransaction (Id)
    );
    -- R3: replayed samples after an outage must not duplicate.
    CREATE UNIQUE INDEX UX_OcppMeterValue_ChargePoint_Transaction_MeasuredAt ON dbo.OcppMeterValue (OcppChargePointId, OcppTransactionId, MeasuredAt);
    CREATE INDEX IX_OcppMeterValue_Transaction_MeasuredAt ON dbo.OcppMeterValue (OcppTransactionId, MeasuredAt);
    PRINT 'Created dbo.OcppMeterValue';
END
ELSE PRINT 'SKIP dbo.OcppMeterValue exists';
GO

IF OBJECT_ID('dbo.OcppAuthorizedTag', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OcppAuthorizedTag
    (
        Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OcppAuthorizedTag PRIMARY KEY,
        ChargingPointId INT            NOT NULL,
        IdTag           NVARCHAR(50)   NOT NULL,   -- normalised: trimmed, upper-case
        Label           NVARCHAR(100)  NULL,
        IsEnabled       BIT            NOT NULL CONSTRAINT DF_OcppAuthorizedTag_IsEnabled DEFAULT 1,
        ExpiresAt       DATETIME2(0)   NULL,
        CreatedBy       INT            NULL,
        CreatedAt       DATETIME       NOT NULL,
        ModifiedBy      INT            NULL,
        ModifiedAt      DATETIME       NULL,
        IsDeleted       BIT            NOT NULL CONSTRAINT DF_OcppAuthorizedTag_IsDeleted DEFAULT 0,
        CONSTRAINT FK_OcppAuthorizedTag_ChargingPoint FOREIGN KEY (ChargingPointId) REFERENCES dbo.ChargingPoint (Id) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX UX_OcppAuthorizedTag_Station_Tag ON dbo.OcppAuthorizedTag (ChargingPointId, IdTag) WHERE [IsDeleted] = 0;
    PRINT 'Created dbo.OcppAuthorizedTag';
END
ELSE PRINT 'SKIP dbo.OcppAuthorizedTag exists';
GO
