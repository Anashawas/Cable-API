-- Worker tracker, step 1 — worker login (Docs/WorkerTracker-Plan.md):
--   * dbo.PartnerCredential: the worker's partner-app password (temporary password, forced change, lockout).
--     One UserAccount per phone keeps everything; this is the second credential the partner app's phone login uses.
--   * dbo.TrustedDevice: devices that passed an OTP and skip it next time.
--   * NotificationType worker_first_login (owner push).
--   * Existing workers: a PartnerCredential copying their current password hash, so they log in by phone with the
--     password they already have — no temporary password, no forced change (decision 29).
-- Idempotent: safe to run again. Apply to dev first; deploy the API build of this change only after the script.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- 1. PartnerCredential ---------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.PartnerCredential', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PartnerCredential
    (
        Id                    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PartnerCredential PRIMARY KEY,
        UserId                INT            NOT NULL,
        PasswordHash          NVARCHAR(1000) NOT NULL,
        MustChangePassword    BIT            NOT NULL CONSTRAINT DF_PartnerCredential_MustChangePassword DEFAULT 0,
        TempPasswordExpiresAt DATETIME2(3)   NULL,     -- UTC; set while the password is a temporary one
        FirstLoginAt          DATETIME2(3)   NULL,     -- UTC; null = waiting for the first login
        LastLoginAt           DATETIME2(3)   NULL,
        PasswordChangedAt     DATETIME2(3)   NULL,
        FailedLoginCount      INT            NOT NULL CONSTRAINT DF_PartnerCredential_FailedLoginCount DEFAULT 0,
        LockedUntil           DATETIME2(3)   NULL,     -- UTC; 423 LOCKED until then
        CreatedBy             INT            NULL,
        CreatedAt             DATETIME       NOT NULL,
        ModifiedBy            INT            NULL,
        ModifiedAt            DATETIME       NULL,
        IsDeleted             BIT            NOT NULL CONSTRAINT DF_PartnerCredential_IsDeleted DEFAULT 0,
        CONSTRAINT FK_PartnerCredential_UserAccount FOREIGN KEY (UserId) REFERENCES dbo.UserAccount (Id)
    );
    PRINT 'Created dbo.PartnerCredential';
END
ELSE PRINT 'SKIP dbo.PartnerCredential exists';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PartnerCredential_User' AND object_id = OBJECT_ID('dbo.PartnerCredential'))
BEGIN
    CREATE UNIQUE INDEX UX_PartnerCredential_User ON dbo.PartnerCredential (UserId);
    PRINT 'Created UX_PartnerCredential_User';
END
ELSE PRINT 'SKIP UX_PartnerCredential_User exists';
GO

-- 2. TrustedDevice -------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.TrustedDevice', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TrustedDevice
    (
        Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TrustedDevice PRIMARY KEY,
        UserId      INT           NOT NULL,
        App         NVARCHAR(20)  NOT NULL,   -- provider | provider-web
        DeviceId    NVARCHAR(200) NOT NULL,
        TokenHash   NVARCHAR(64)  NOT NULL,   -- SHA-256 of the token the app holds
        LastUsedAt  DATETIME2(3)  NOT NULL,
        ExpiresAt   DATETIME2(3)  NOT NULL,   -- UTC; the next login after this needs an OTP again
        CreatedBy   INT           NULL,
        CreatedAt   DATETIME      NOT NULL,
        ModifiedBy  INT           NULL,
        ModifiedAt  DATETIME      NULL,
        IsDeleted   BIT           NOT NULL CONSTRAINT DF_TrustedDevice_IsDeleted DEFAULT 0,
        CONSTRAINT FK_TrustedDevice_UserAccount FOREIGN KEY (UserId) REFERENCES dbo.UserAccount (Id)
    );
    PRINT 'Created dbo.TrustedDevice';
END
ELSE PRINT 'SKIP dbo.TrustedDevice exists';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_TrustedDevice_TokenHash' AND object_id = OBJECT_ID('dbo.TrustedDevice'))
BEGIN
    CREATE UNIQUE INDEX UX_TrustedDevice_TokenHash ON dbo.TrustedDevice (TokenHash);
    PRINT 'Created UX_TrustedDevice_TokenHash';
END
ELSE PRINT 'SKIP UX_TrustedDevice_TokenHash exists';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TrustedDevice_User' AND object_id = OBJECT_ID('dbo.TrustedDevice'))
BEGIN
    CREATE INDEX IX_TrustedDevice_User ON dbo.TrustedDevice (UserId) WHERE [IsDeleted] = 0;
    PRINT 'Created IX_TrustedDevice_User';
END
ELSE PRINT 'SKIP IX_TrustedDevice_User exists';
GO

-- 3. Notification type ---------------------------------------------------------------------------------------------
INSERT INTO dbo.NotificationType ([Name], [Description])
SELECT v.Name, v.Description
FROM (VALUES
    ('worker_first_login', 'A worker signed in to the partner app for the first time (sent to the owner)')
) AS v(Name, Description)
WHERE NOT EXISTS (SELECT 1 FROM dbo.NotificationType t WHERE t.Name = v.Name);
PRINT CONCAT('Notification types added: ', @@ROWCOUNT);
GO

-- 4. Existing workers keep their password (decision 29) -------------------------------------------------------------
INSERT INTO dbo.PartnerCredential (UserId, PasswordHash, MustChangePassword, FirstLoginAt, CreatedAt)
SELECT u.Id, u.Password, 0, COALESCE(u.PartnerLastLoginAt, u.LastLoginAt, u.CreatedAt), GETUTCDATE()
FROM dbo.UserAccount u
WHERE u.IsDeleted = 0
  AND u.Password IS NOT NULL
  AND EXISTS (SELECT 1 FROM dbo.ProviderManager pm WHERE pm.UserId = u.Id AND pm.IsDeleted = 0)
  AND NOT EXISTS (SELECT 1 FROM dbo.PartnerCredential c WHERE c.UserId = u.Id);
PRINT CONCAT('Existing workers given a partner credential: ', @@ROWCOUNT);
GO

-- 5. Check --------------------------------------------------------------------------------------------------------
SELECT
    (SELECT COUNT(*) FROM dbo.ProviderManager WHERE IsDeleted = 0) AS Workers,
    (SELECT COUNT(*) FROM dbo.PartnerCredential WHERE IsDeleted = 0) AS Credentials,
    (SELECT COUNT(*) FROM dbo.ProviderManager pm WHERE pm.IsDeleted = 0
        AND NOT EXISTS (SELECT 1 FROM dbo.PartnerCredential c WHERE c.UserId = pm.UserId)) AS WorkersWithoutCredential;
GO
