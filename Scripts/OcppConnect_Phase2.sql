-- Cable Connect (OCPP 1.6J) — Phase 2: remote control.
-- Audit trail of central-system commands (Reset, UnlockConnector, ChangeAvailability,
-- TriggerMessage, GetConfiguration, …). Requires OcppConnect_Phase1.sql. Idempotent.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.OcppCommand', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OcppCommand
    (
        Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OcppCommand PRIMARY KEY,
        OcppChargePointId INT            NOT NULL,
        Action            NVARCHAR(64)   NOT NULL,   -- OCPP action, e.g. Reset
        RequestPayload    NVARCHAR(MAX)  NOT NULL,   -- CALL payload as sent
        Status            NVARCHAR(20)   NOT NULL,   -- Answered | CallError | NotConnected | Timeout | Disconnected | Invalid | Unreachable
        ResultStatus      NVARCHAR(40)   NULL,       -- the unit's status word: Accepted | Rejected | Unlocked | Scheduled | ...
        ResponsePayload   NVARCHAR(MAX)  NULL,       -- CALLRESULT payload when answered
        ErrorCode         NVARCHAR(64)   NULL,
        ErrorDescription  NVARCHAR(500)  NULL,
        DurationMs        INT            NULL,
        CompletedAt       DATETIME2(3)   NULL,
        CreatedBy         INT            NULL,       -- the admin / owner who asked
        CreatedAt         DATETIME       NOT NULL,
        ModifiedBy        INT            NULL,
        ModifiedAt        DATETIME       NULL,
        IsDeleted         BIT            NOT NULL CONSTRAINT DF_OcppCommand_IsDeleted DEFAULT 0,
        CONSTRAINT FK_OcppCommand_OcppChargePoint FOREIGN KEY (OcppChargePointId) REFERENCES dbo.OcppChargePoint (Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_OcppCommand_ChargePoint_CreatedAt ON dbo.OcppCommand (OcppChargePointId, CreatedAt DESC);
    PRINT 'Created dbo.OcppCommand';
END
ELSE PRINT 'SKIP dbo.OcppCommand exists';
GO

-- Local authorization list sync state (SendLocalList) on the charger row.
IF COL_LENGTH('dbo.OcppChargePoint', 'LocalListVersion') IS NULL
BEGIN
    ALTER TABLE dbo.OcppChargePoint ADD
        LocalListVersion  INT          NULL,       -- version pushed last (Unix seconds of the push)
        LocalListSyncedAt DATETIME2(3) NULL,
        LocalListStatus   NVARCHAR(20) NULL;       -- Synced | Pending | Failed | NotSupported; NULL = never attempted
    PRINT 'Added OcppChargePoint.LocalList* columns';
END
ELSE PRINT 'SKIP OcppChargePoint.LocalList* columns exist';
GO

-- Alert rules (charger offline / plug Faulted / session open too long): one open row per target.
IF OBJECT_ID('dbo.OcppAlert', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OcppAlert
    (
        Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OcppAlert PRIMARY KEY,
        OcppChargePointId INT            NOT NULL,
        Type              NVARCHAR(30)   NOT NULL,   -- ChargerOffline | ConnectorFaulted | SessionTooLong
        ConnectorId       INT            NULL,       -- plug for connector/session alerts
        OcppTransactionId INT            NULL,
        ConditionSince    DATETIME2(3)   NOT NULL,   -- when the condition began, not when noticed
        NotifiedAt        DATETIME2(3)   NOT NULL,
        ResolvedAt        DATETIME2(3)   NULL,       -- NULL = still open
        Details           NVARCHAR(300)  NULL,
        Recipients        INT            NOT NULL CONSTRAINT DF_OcppAlert_Recipients DEFAULT 0,
        CreatedBy         INT            NULL,
        CreatedAt         DATETIME       NOT NULL,
        ModifiedBy        INT            NULL,
        ModifiedAt        DATETIME       NULL,
        IsDeleted         BIT            NOT NULL CONSTRAINT DF_OcppAlert_IsDeleted DEFAULT 0,
        CONSTRAINT FK_OcppAlert_OcppChargePoint FOREIGN KEY (OcppChargePointId) REFERENCES dbo.OcppChargePoint (Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_OcppAlert_Target_Open ON dbo.OcppAlert (OcppChargePointId, Type, ConnectorId, ResolvedAt);
    PRINT 'Created dbo.OcppAlert';
END
ELSE PRINT 'SKIP dbo.OcppAlert exists';
GO
