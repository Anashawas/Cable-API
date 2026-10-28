-- Sprint 5 — leftovers (Docs/Sprint5-Leftovers.md):
--   * the driver's Cable Connect settings: email receipts (dbo.UserOcppSetting, off by default — no row = off)
--   * one receipt email per session, sent or failed (dbo.OcppReceiptEmail)
-- Nothing else in the leftovers needs schema (alerts, month filter, pending count, promo lines in settlements,
-- boost caps, model compare and the landing links are code only).
-- Idempotent: safe to run again. Apply to dev first; deploy the API build of this change only after the script
-- (the receipt email job and GET/PUT /api/users/me/ocpp-settings read these tables).

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- 1. Driver settings ---------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.UserOcppSetting', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserOcppSetting
    (
        Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserOcppSetting PRIMARY KEY,
        UserId        INT      NOT NULL,
        EmailReceipts BIT      NOT NULL CONSTRAINT DF_UserOcppSetting_EmailReceipts DEFAULT 0,   -- email the receipt PDF when a session ends
        CreatedBy     INT      NULL,
        CreatedAt     DATETIME NOT NULL,
        ModifiedBy    INT      NULL,
        ModifiedAt    DATETIME NULL,
        IsDeleted     BIT      NOT NULL CONSTRAINT DF_UserOcppSetting_IsDeleted DEFAULT 0,
        CONSTRAINT FK_UserOcppSetting_UserAccount FOREIGN KEY (UserId) REFERENCES dbo.UserAccount (Id)
    );
    PRINT 'Created dbo.UserOcppSetting';
END
ELSE PRINT 'SKIP dbo.UserOcppSetting exists';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_UserOcppSetting_UserId' AND object_id = OBJECT_ID('dbo.UserOcppSetting'))
BEGIN
    CREATE UNIQUE INDEX UX_UserOcppSetting_UserId ON dbo.UserOcppSetting (UserId);
    PRINT 'Created UX_UserOcppSetting_UserId';
END
ELSE PRINT 'SKIP UX_UserOcppSetting_UserId exists';
GO

-- 2. Receipt emails (one per session) ----------------------------------------------------------------------------
IF OBJECT_ID('dbo.OcppReceiptEmail', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OcppReceiptEmail
    (
        Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OcppReceiptEmail PRIMARY KEY,
        OcppTransactionId INT            NOT NULL,   -- the session (no FK: sessions are never hard-deleted, and a purge must not be blocked)
        UserId            INT            NOT NULL,
        Email             NVARCHAR(256)  NOT NULL,
        Status            NVARCHAR(16)   NOT NULL,   -- Sent | Failed
        Error             NVARCHAR(500)  NULL,
        CreatedAt         DATETIME2(0)   NOT NULL
    );
    PRINT 'Created dbo.OcppReceiptEmail';
END
ELSE PRINT 'SKIP dbo.OcppReceiptEmail exists';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_OcppReceiptEmail_OcppTransactionId' AND object_id = OBJECT_ID('dbo.OcppReceiptEmail'))
BEGIN
    CREATE UNIQUE INDEX UX_OcppReceiptEmail_OcppTransactionId ON dbo.OcppReceiptEmail (OcppTransactionId);
    PRINT 'Created UX_OcppReceiptEmail_OcppTransactionId';
END
ELSE PRINT 'SKIP UX_OcppReceiptEmail_OcppTransactionId exists';
GO

PRINT 'Sprint5_Leftovers.sql done';
GO
