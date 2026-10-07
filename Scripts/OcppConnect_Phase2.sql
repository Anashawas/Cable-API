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

-- idTag → Cable user (driver pushes, automatic loyalty points, later remote start/stop from the app).
IF OBJECT_ID('dbo.OcppUserIdTag', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OcppUserIdTag
    (
        Id         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OcppUserIdTag PRIMARY KEY,
        UserId     INT            NOT NULL,
        IdTag      NVARCHAR(50)   NOT NULL,   -- normalised: trimmed, upper-case
        Label      NVARCHAR(100)  NULL,
        IsEnabled  BIT            NOT NULL CONSTRAINT DF_OcppUserIdTag_IsEnabled DEFAULT 1,
        CreatedBy  INT            NULL,
        CreatedAt  DATETIME       NOT NULL,
        ModifiedBy INT            NULL,
        ModifiedAt DATETIME       NULL,
        IsDeleted  BIT            NOT NULL CONSTRAINT DF_OcppUserIdTag_IsDeleted DEFAULT 0,
        CONSTRAINT FK_OcppUserIdTag_UserAccount FOREIGN KEY (UserId) REFERENCES dbo.UserAccount (Id)
    );
    CREATE UNIQUE INDEX UX_OcppUserIdTag_IdTag ON dbo.OcppUserIdTag (IdTag) WHERE [IsDeleted] = 0;
    CREATE INDEX IX_OcppUserIdTag_User ON dbo.OcppUserIdTag (UserId);
    PRINT 'Created dbo.OcppUserIdTag';
END
ELSE PRINT 'SKIP dbo.OcppUserIdTag exists';
GO

-- Parked-after-charging alert: two stages (driver, then station).
IF COL_LENGTH('dbo.OcppAlert', 'EscalatedAt') IS NULL
BEGIN
    ALTER TABLE dbo.OcppAlert ADD
        EscalatedAt  DATETIME2(3) NULL,   -- when the station owner / managers were told
        DriverUserId INT          NULL;   -- the driver told first (OcppUserIdTag), when known
    PRINT 'Added OcppAlert.EscalatedAt / DriverUserId';
END
ELSE PRINT 'SKIP OcppAlert.EscalatedAt exists';
GO
