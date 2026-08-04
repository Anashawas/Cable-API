/* =============================================================================
   Cable — Worker feature Phase 1
   -----------------------------------------------------------------------------
   - Adds the 'Worker' role
   - Creates dbo.ProviderManager (one worker per provider)

   Idempotent. Run on dev first, then production (additive, zero downtime).
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- 1) Worker role -------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.Role WHERE Name = N'Worker')
BEGIN
    INSERT INTO dbo.Role (Name, IsDeleted, CreatedAt) VALUES (N'Worker', 0, GETDATE());
END;

-- 2) ProviderManager table ---------------------------------------------------
IF OBJECT_ID(N'dbo.ProviderManager', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProviderManager (
        Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProviderManager PRIMARY KEY,
        ProviderType NVARCHAR(50) NOT NULL,        -- "ServiceProvider" | "ChargingPoint"
        ProviderId   INT          NOT NULL,
        UserId       INT          NOT NULL,        -- the Worker-role UserAccount
        IsActive     BIT          NOT NULL CONSTRAINT DF_ProviderManager_IsActive  DEFAULT 1,
        IsDeleted    BIT          NOT NULL CONSTRAINT DF_ProviderManager_IsDeleted DEFAULT 0,
        CreatedBy    INT          NULL,
        CreatedAt    DATETIME     NOT NULL CONSTRAINT DF_ProviderManager_CreatedAt DEFAULT GETDATE(),
        ModifiedBy   INT          NULL,
        ModifiedAt   DATETIME     NULL,
        CONSTRAINT FK_ProviderManager_User
            FOREIGN KEY (UserId) REFERENCES dbo.UserAccount(Id),
        CONSTRAINT CK_ProviderManager_ProviderType
            CHECK (ProviderType IN (N'ServiceProvider', N'ChargingPoint'))
    );

    -- one active worker per provider
    CREATE UNIQUE INDEX UX_ProviderManager_OneWorker
        ON dbo.ProviderManager(ProviderType, ProviderId)
        WHERE IsDeleted = 0;

    -- fast "providers this user works for"
    CREATE INDEX IX_ProviderManager_User
        ON dbo.ProviderManager(UserId)
        INCLUDE (ProviderType, ProviderId, IsActive)
        WHERE IsDeleted = 0;
END;

-- 3) Verify ------------------------------------------------------------------
SELECT (SELECT Id FROM dbo.Role WHERE Name = N'Worker') AS WorkerRoleId,
       (SELECT COUNT(*) FROM sys.tables WHERE name = 'ProviderManager') AS ProviderManagerExists;

/* =============================================================================
   ROLLBACK:
       DROP TABLE IF EXISTS dbo.ProviderManager;
       DELETE FROM dbo.Role WHERE Name = N'Worker';   -- only if no Worker users exist
   ============================================================================= */
