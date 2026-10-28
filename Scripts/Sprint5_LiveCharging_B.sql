-- Sprint 5 — live charging (B), Docs/Sprint5-LiveCharging-B.md:
--   * CHG- plug sticker codes (dbo.OcppConnectorCode) + a code for every existing plug (ConnectorId > 0)
--   * start method on each session (App | Sticker | Card | Operator) + the plug's parked "Sticker"
--   * session receipt number (CHS-YYYY-NNNNNN)
--   * "plug didn't work" reports (dbo.OcppConnectorReport)
--   * "problem with this session" on the existing complaints (UserComplaints.Kind / OcppTransactionId / Reason / ImageFileName / Reply)
--   * notification types and the shared-link type for https://cable-app.com/c/CHG-XXXXXX
-- Requires OcppConnect_LiveAndLimits.sql. Idempotent: safe to run again. Apply to dev first; deploy the API and
-- Cable.Ocpp builds of this change only after the script (both read the new columns).

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- 1. Sticker codes ---------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.OcppConnectorCode', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OcppConnectorCode
    (
        Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OcppConnectorCode PRIMARY KEY,
        OcppConnectorId INT            NOT NULL,
        Code            NVARCHAR(20)   NOT NULL,   -- CHG-7K2M9Q (upper case, alphabet without 0 O 1 I L)
        RevokedAt       DATETIME2(0)   NULL,       -- NULL = active; revoked codes stay so a scan answers "revoked"
        RevokedBy       INT            NULL,
        CreatedBy       INT            NULL,
        CreatedAt       DATETIME       NOT NULL,
        ModifiedBy      INT            NULL,
        ModifiedAt      DATETIME       NULL,
        IsDeleted       BIT            NOT NULL CONSTRAINT DF_OcppConnectorCode_IsDeleted DEFAULT 0,
        CONSTRAINT FK_OcppConnectorCode_OcppConnector FOREIGN KEY (OcppConnectorId) REFERENCES dbo.OcppConnector (Id) ON DELETE CASCADE
    );
    PRINT 'Created dbo.OcppConnectorCode';
END
ELSE PRINT 'SKIP dbo.OcppConnectorCode exists';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_OcppConnectorCode_Code' AND object_id = OBJECT_ID('dbo.OcppConnectorCode'))
BEGIN
    CREATE UNIQUE INDEX UX_OcppConnectorCode_Code ON dbo.OcppConnectorCode (Code);
    PRINT 'Created UX_OcppConnectorCode_Code';
END
ELSE PRINT 'SKIP UX_OcppConnectorCode_Code exists';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_OcppConnectorCode_Connector_Active' AND object_id = OBJECT_ID('dbo.OcppConnectorCode'))
BEGIN
    CREATE UNIQUE INDEX UX_OcppConnectorCode_Connector_Active ON dbo.OcppConnectorCode (OcppConnectorId) WHERE RevokedAt IS NULL AND IsDeleted = 0;
    PRINT 'Created UX_OcppConnectorCode_Connector_Active';
END
ELSE PRINT 'SKIP UX_OcppConnectorCode_Connector_Active exists';
GO

-- Backfill: one active code for every plug (ConnectorId > 0) of a charger that is not deleted.
DECLARE @alphabet CHAR(31) = '23456789ABCDEFGHJKMNPQRSTUVWXYZ';
DECLARE @connectorId INT, @code NVARCHAR(20), @bytes VARBINARY(6), @i INT, @issued INT = 0;
DECLARE plugs CURSOR LOCAL FAST_FORWARD FOR
    SELECT k.Id
    FROM dbo.OcppConnector k
    JOIN dbo.OcppChargePoint cp ON cp.Id = k.OcppChargePointId
    WHERE k.ConnectorId > 0 AND cp.IsDeleted = 0
      AND NOT EXISTS (SELECT 1 FROM dbo.OcppConnectorCode x WHERE x.OcppConnectorId = k.Id AND x.RevokedAt IS NULL AND x.IsDeleted = 0)
    ORDER BY k.OcppChargePointId, k.ConnectorId;
OPEN plugs;
FETCH NEXT FROM plugs INTO @connectorId;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @code = NULL;
    WHILE @code IS NULL OR EXISTS (SELECT 1 FROM dbo.OcppConnectorCode WHERE Code = @code)
    BEGIN
        SET @bytes = CRYPT_GEN_RANDOM(6);
        SET @code = 'CHG-';
        SET @i = 1;
        WHILE @i <= 6
        BEGIN
            SET @code = @code + SUBSTRING(@alphabet, (CAST(SUBSTRING(@bytes, @i, 1) AS INT) % 31) + 1, 1);
            SET @i += 1;
        END
    END
    INSERT INTO dbo.OcppConnectorCode (OcppConnectorId, Code, CreatedAt, IsDeleted) VALUES (@connectorId, @code, GETUTCDATE(), 0);
    SET @issued += 1;
    FETCH NEXT FROM plugs INTO @connectorId;
END
CLOSE plugs;
DEALLOCATE plugs;
PRINT CONCAT('Issued ', @issued, ' plug sticker code(s)');
GO

-- 2. Start method + receipt number on the session ---------------------------------------------------------------
IF COL_LENGTH('dbo.OcppTransaction', 'StartMethod') IS NULL
BEGIN
    ALTER TABLE dbo.OcppTransaction ADD
        StartMethod     NVARCHAR(10) NULL,   -- App | Sticker | Card | Operator (NULL = read as StartSource)
        ReceiptNo       NVARCHAR(20) NULL,   -- CHS-YYYY-NNNNNN, assigned on the first receipt download
        ReceiptIssuedAt DATETIME2(0) NULL;
    PRINT 'Added OcppTransaction.StartMethod / ReceiptNo / ReceiptIssuedAt';
END
ELSE PRINT 'SKIP OcppTransaction.StartMethod / Receipt* exist';
GO

-- Existing sessions: the method is their source (no sticker starts before this change).
UPDATE dbo.OcppTransaction SET StartMethod = StartSource WHERE StartMethod IS NULL;
PRINT CONCAT('Backfilled StartMethod on ', @@ROWCOUNT, ' session(s)');
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_OcppTransaction_ReceiptNo' AND object_id = OBJECT_ID('dbo.OcppTransaction'))
BEGIN
    CREATE UNIQUE INDEX UX_OcppTransaction_ReceiptNo ON dbo.OcppTransaction (ReceiptNo) WHERE ReceiptNo IS NOT NULL;
    PRINT 'Created UX_OcppTransaction_ReceiptNo';
END
ELSE PRINT 'SKIP UX_OcppTransaction_ReceiptNo exists';
GO

IF COL_LENGTH('dbo.OcppConnector', 'PendingStartMethod') IS NULL
BEGIN
    ALTER TABLE dbo.OcppConnector ADD
        PendingStartMethod   NVARCHAR(10) NULL,   -- "Sticker" parked by the API's start, taken by the next App StartTransaction
        PendingStartMethodAt DATETIME2(0) NULL;
    PRINT 'Added OcppConnector.PendingStartMethod*';
END
ELSE PRINT 'SKIP OcppConnector.PendingStartMethod* exist';
GO

-- 3. "Plug didn't work" reports ---------------------------------------------------------------------------------
IF OBJECT_ID('dbo.OcppConnectorReport', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OcppConnectorReport
    (
        Id                  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OcppConnectorReport PRIMARY KEY,
        ChargingPointId     INT            NOT NULL,
        OcppChargePointId   INT            NOT NULL,
        ConnectorId         INT            NOT NULL,
        UserId              INT            NOT NULL,   -- the driver who reported it
        Reason              NVARCHAR(20)   NOT NULL,   -- NoResponse | Rejected | ScreenError | Damaged | Other
        Note                NVARCHAR(500)  NULL,
        OcppCommandId       INT            NULL,       -- the failed RemoteStartTransaction, when the app sent it
        CommandStatus       NVARCHAR(20)   NULL,       -- copied from OcppCommand: Answered | Timeout | NotConnected | ...
        CommandResultStatus NVARCHAR(40)   NULL,       -- the charger's word: Accepted | Rejected | ...
        CommandErrorCode    NVARCHAR(64)   NULL,
        ConnectorStatus     NVARCHAR(30)   NULL,       -- the plug's last status when reported
        ConnectorErrorCode  NVARCHAR(50)   NULL,
        ConnectorStatusAt   DATETIME2(3)   NULL,
        ChargerOnline       BIT            NOT NULL CONSTRAINT DF_OcppConnectorReport_ChargerOnline DEFAULT 0,
        ResolvedAt          DATETIME2(0)   NULL,       -- NULL = open
        ResolvedBy          INT            NULL,
        ResolutionNote      NVARCHAR(500)  NULL,
        CreatedBy           INT            NULL,
        CreatedAt           DATETIME       NOT NULL,
        ModifiedBy          INT            NULL,
        ModifiedAt          DATETIME       NULL,
        IsDeleted           BIT            NOT NULL CONSTRAINT DF_OcppConnectorReport_IsDeleted DEFAULT 0,
        CONSTRAINT FK_OcppConnectorReport_OcppChargePoint FOREIGN KEY (OcppChargePointId) REFERENCES dbo.OcppChargePoint (Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_OcppConnectorReport_Plug_Open ON dbo.OcppConnectorReport (OcppChargePointId, ConnectorId, ResolvedAt);
    CREATE INDEX IX_OcppConnectorReport_Station_CreatedAt ON dbo.OcppConnectorReport (ChargingPointId, CreatedAt);
    PRINT 'Created dbo.OcppConnectorReport';
END
ELSE PRINT 'SKIP dbo.OcppConnectorReport exists';
GO

-- 4. "Problem with this session" on the existing complaints -----------------------------------------------------
IF COL_LENGTH('dbo.UserComplaints', 'Kind') IS NULL
BEGIN
    ALTER TABLE dbo.UserComplaints ADD
        Kind              NVARCHAR(20)   NOT NULL CONSTRAINT DF_UserComplaints_Kind DEFAULT 'General', -- General | ChargingSession
        OcppTransactionId INT            NULL,      -- ChargingSession: the session
        Reason            NVARCHAR(20)   NULL,      -- WrongCost | StoppedEarly | NoEnergy | CableStuck | PlugBroken | Other
        ImageFileName     NVARCHAR(260)  NULL,
        Reply             NVARCHAR(1000) NULL;      -- the partner's / admin's answer, shown to the driver
    PRINT 'Added UserComplaints.Kind / OcppTransactionId / Reason / ImageFileName / Reply';
END
ELSE PRINT 'SKIP UserComplaints session columns exist';
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_UserComplaints_OcppTransaction')
BEGIN
    ALTER TABLE dbo.UserComplaints WITH CHECK ADD CONSTRAINT FK_UserComplaints_OcppTransaction
        FOREIGN KEY (OcppTransactionId) REFERENCES dbo.OcppTransaction (Id);
    PRINT 'Created FK_UserComplaints_OcppTransaction';
END
ELSE PRINT 'SKIP FK_UserComplaints_OcppTransaction exists';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserComplaints_OcppTransactionId' AND object_id = OBJECT_ID('dbo.UserComplaints'))
BEGIN
    CREATE INDEX IX_UserComplaints_OcppTransactionId ON dbo.UserComplaints (OcppTransactionId) WHERE OcppTransactionId IS NOT NULL;
    PRINT 'Created IX_UserComplaints_OcppTransactionId';
END
ELSE PRINT 'SKIP IX_UserComplaints_OcppTransactionId exists';
GO

-- 5. Notification types (resolved by name at runtime) and the sticker link type -------------------------------
INSERT INTO dbo.NotificationType ([Name], [Description])
SELECT v.Name, v.Description
FROM (VALUES
    ('ocpp_connector_report',     'A driver reported that a plug did not start (sent to the station owner and managers)'),
    ('ocpp_session_issue',        'A driver reported a problem with a charging session (sent to the station team and admins)'),
    ('ocpp_session_issue_status', 'The status of a charging-session complaint changed (sent to the driver)')
) AS v(Name, Description)
WHERE NOT EXISTS (SELECT 1 FROM dbo.NotificationType t WHERE t.Name = v.Name);
PRINT CONCAT('Notification types added: ', @@ROWCOUNT);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SharedLinkType WHERE TypeName = 'charger-sticker')
BEGIN
    INSERT INTO dbo.SharedLinkType (TypeName, Description, BaseUrl, IsActive)
    VALUES ('charger-sticker', 'Plug sticker: https://cable-app.com/c/CHG-XXXXXX opens the start sheet (resolve: GET /api/charging-points/connectors/resolve?code=)', 'https://cable-app.com/c', 1);
    PRINT 'Added SharedLinkType charger-sticker';
END
ELSE PRINT 'SKIP SharedLinkType charger-sticker exists';
GO

-- Check
SELECT
    (SELECT COUNT(*) FROM dbo.OcppConnector k JOIN dbo.OcppChargePoint cp ON cp.Id = k.OcppChargePointId WHERE k.ConnectorId > 0 AND cp.IsDeleted = 0) AS Plugs,
    (SELECT COUNT(*) FROM dbo.OcppConnectorCode WHERE RevokedAt IS NULL AND IsDeleted = 0) AS ActiveCodes,
    (SELECT COUNT(*) FROM dbo.OcppTransaction WHERE StartMethod IS NULL) AS SessionsWithoutMethod;
GO
