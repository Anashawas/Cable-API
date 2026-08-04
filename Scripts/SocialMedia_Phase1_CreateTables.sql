/* =============================================================================
   Cable — SocialMedia Phase 1: catalog + per-provider links
   -----------------------------------------------------------------------------
   - Creates dbo.SocialMediaPlatform   (catalog with optional icon)
   - Creates dbo.SocialLink            (per-provider URLs, polymorphic)
   - Seeds 9 common platforms (only if the catalog is empty)

   Idempotent: re-running is safe (IF NOT EXISTS guards everywhere).
   Run order: dev DB first → smoke test → production DB.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- 1) Catalog ------------------------------------------------------------------
IF OBJECT_ID(N'dbo.SocialMediaPlatform', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SocialMediaPlatform (
        Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SocialMediaPlatform PRIMARY KEY,
        Name            NVARCHAR(100)     NOT NULL,
        NameAr          NVARCHAR(100)     NULL,
        IconFileName    NVARCHAR(255)     NULL,
        IconExtension   NVARCHAR(50)      NULL,
        IconContentType NVARCHAR(50)      NULL,
        IconFileSize    BIGINT            NULL,
        DisplayOrder    INT               NOT NULL CONSTRAINT DF_SocialMediaPlatform_Order DEFAULT 0,
        IsActive        BIT               NOT NULL CONSTRAINT DF_SocialMediaPlatform_IsActive DEFAULT 1,
        IsDeleted       BIT               NOT NULL CONSTRAINT DF_SocialMediaPlatform_IsDeleted DEFAULT 0,
        CreatedBy       INT               NULL,
        CreatedAt       DATETIME          NOT NULL CONSTRAINT DF_SocialMediaPlatform_CreatedAt DEFAULT GETDATE(),
        ModifiedBy      INT               NULL,
        ModifiedAt      DATETIME          NULL,
        CONSTRAINT UQ_SocialMediaPlatform_Name UNIQUE (Name)
    );

    CREATE INDEX IX_SocialMediaPlatform_IsActive
        ON dbo.SocialMediaPlatform(IsActive)
        INCLUDE (DisplayOrder, Name)
        WHERE IsDeleted = 0;
END;

-- Idempotent: add NameAr if an earlier version of the table is already present.
IF OBJECT_ID(N'dbo.SocialMediaPlatform', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns
                   WHERE Name = N'NameAr' AND Object_ID = Object_ID(N'dbo.SocialMediaPlatform'))
BEGIN
    ALTER TABLE dbo.SocialMediaPlatform ADD NameAr NVARCHAR(100) NULL;
END;

-- 2) Per-provider links -------------------------------------------------------
IF OBJECT_ID(N'dbo.SocialLink', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SocialLink (
        Id                     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SocialLink PRIMARY KEY,
        ProviderType           NVARCHAR(50)      NOT NULL,
        ProviderId             INT               NOT NULL,
        SocialMediaPlatformId  INT               NOT NULL,
        Url                    NVARCHAR(1000)    NOT NULL,
        DisplayOrder           INT               NOT NULL CONSTRAINT DF_SocialLink_Order DEFAULT 0,
        IsDeleted              BIT               NOT NULL CONSTRAINT DF_SocialLink_IsDeleted DEFAULT 0,
        CreatedBy              INT               NULL,
        CreatedAt              DATETIME          NOT NULL CONSTRAINT DF_SocialLink_CreatedAt DEFAULT GETDATE(),
        ModifiedBy             INT               NULL,
        ModifiedAt             DATETIME          NULL,
        CONSTRAINT FK_SocialLink_Platform
            FOREIGN KEY (SocialMediaPlatformId) REFERENCES dbo.SocialMediaPlatform(Id),
        CONSTRAINT CK_SocialLink_ProviderType
            CHECK (ProviderType IN (N'ServiceProvider', N'ChargingPoint'))
    );

    CREATE INDEX IX_SocialLink_Provider
        ON dbo.SocialLink(ProviderType, ProviderId)
        INCLUDE (SocialMediaPlatformId, Url, DisplayOrder, IsDeleted)
        WHERE IsDeleted = 0;
END;

-- 3) Seed common platforms (only if catalog is empty) ------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.SocialMediaPlatform)
BEGIN
    INSERT INTO dbo.SocialMediaPlatform (Name, NameAr, DisplayOrder) VALUES
        (N'Facebook',  N'فيسبوك',   1),
        (N'Instagram', N'إنستغرام', 2),
        (N'TikTok',    N'تيك توك',  3),
        (N'X',         N'إكس',      4),
        (N'YouTube',   N'يوتيوب',   5),
        (N'WhatsApp',  N'واتساب',   6),
        (N'Telegram',  N'تيليجرام', 7),
        (N'Snapchat',  N'سناب شات', 8),
        (N'LinkedIn',  N'لينكدإن',  9);
END;

-- 4) Verification -------------------------------------------------------------
SELECT 'SocialMediaPlatform' AS T, COUNT(*) AS Rows FROM dbo.SocialMediaPlatform
UNION ALL
SELECT 'SocialLink',          COUNT(*) FROM dbo.SocialLink;

/* =============================================================================
   ROLLBACK (when truly needed):

       DROP TABLE IF EXISTS dbo.SocialLink;
       DROP TABLE IF EXISTS dbo.SocialMediaPlatform;

   Existing ServiceProvider / ChargingPoint rows are untouched by this script.
   ============================================================================= */
