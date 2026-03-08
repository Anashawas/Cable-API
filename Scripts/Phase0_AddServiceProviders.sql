-- ============================================================
-- Phase 0: Service Provider Foundation
-- Database: db_ab1977_cable (Dev)
-- Date: 2026-02-09
-- Description: Creates 5 tables for Service Provider system
-- ============================================================

-- ============================================================
-- 1. ServiceCategory
-- ============================================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ServiceCategory' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE [dbo].[ServiceCategory] (
        [Id]            INT             IDENTITY(1,1) NOT NULL,
        [Name]          NVARCHAR(255)   NOT NULL,
        [NameAr]        NVARCHAR(255)   NULL,
        [Description]   NVARCHAR(500)   NULL,
        [IconUrl]       NVARCHAR(500)   NULL,
        [SortOrder]     INT             NOT NULL DEFAULT 0,
        [IsActive]      BIT             NOT NULL DEFAULT 1,
        [CreatedAt]     DATETIME        NOT NULL,
        [CreatedBy]     INT             NULL,
        [ModifiedAt]    DATETIME        NULL,
        [ModifiedBy]    INT             NULL,
        [IsDeleted]     BIT             NOT NULL DEFAULT 0,
        CONSTRAINT [PK_ServiceCategory] PRIMARY KEY CLUSTERED ([Id])
    );

    CREATE INDEX [IX_ServiceCategory_SortOrder] ON [dbo].[ServiceCategory] ([SortOrder]);
    CREATE INDEX [IX_ServiceCategory_IsActive_IsDeleted] ON [dbo].[ServiceCategory] ([IsActive], [IsDeleted]) WHERE [IsDeleted] = 0;

    PRINT 'Table [ServiceCategory] created successfully.';
END
ELSE
    PRINT 'Table [ServiceCategory] already exists. Skipping.';
GO

-- ============================================================
-- 2. ServiceProvider
-- ============================================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ServiceProvider' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE [dbo].[ServiceProvider] (
        [Id]                    INT             IDENTITY(1,1) NOT NULL,
        [Name]                  NVARCHAR(255)   NOT NULL,
        [OwnerId]               INT             NOT NULL,
        [ServiceCategoryId]     INT             NOT NULL,
        [StatusId]              INT             NOT NULL,
        [Description]           NVARCHAR(1000)  NULL,
        [Phone]                 NVARCHAR(40)    NULL,
        [OwnerPhone]            NVARCHAR(40)    NULL,
        [Address]               NVARCHAR(1000)  NULL,
        [CountryName]           NVARCHAR(200)   NULL,
        [CityName]              NVARCHAR(200)   NULL,
        [Latitude]              FLOAT           NOT NULL,
        [Longitude]             FLOAT           NOT NULL,
        [Price]                 FLOAT           NULL,
        [PriceDescription]      NVARCHAR(500)   NULL,
        [FromTime]              NVARCHAR(16)    NULL,
        [ToTime]                NVARCHAR(16)    NULL,
        [MethodPayment]         NVARCHAR(200)   NULL,
        [VisitorsCount]         INT             NOT NULL DEFAULT 0,
        [IsVerified]            BIT             NOT NULL DEFAULT 0,
        [HasOffer]              BIT             NOT NULL DEFAULT 0,
        [OfferDescription]      NVARCHAR(2000)  NULL,
        [Service]               NVARCHAR(MAX)   NULL,
        [Icon]                  NVARCHAR(MAX)   NULL,
        [Note]                  NVARCHAR(1000)  NULL,
        [WhatsAppNumber]        NVARCHAR(80)    NULL,
        [WebsiteUrl]            NVARCHAR(500)   NULL,
        [CreatedAt]             DATETIME        NOT NULL,
        [CreatedBy]             INT             NULL,
        [ModifiedAt]            DATETIME        NULL,
        [ModifiedBy]            INT             NULL,
        [IsDeleted]             BIT             NOT NULL DEFAULT 0,
        CONSTRAINT [PK_ServiceProvider] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [FK_ServiceProvider_UserAccount] FOREIGN KEY ([OwnerId]) REFERENCES [dbo].[UserAccount]([Id]),
        CONSTRAINT [FK_ServiceProvider_ServiceCategory] FOREIGN KEY ([ServiceCategoryId]) REFERENCES [dbo].[ServiceCategory]([Id]),
        CONSTRAINT [FK_ServiceProvider_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status]([Id])
    );

    CREATE INDEX [IX_ServiceProvider_ServiceCategoryId] ON [dbo].[ServiceProvider] ([ServiceCategoryId]);
    CREATE INDEX [IX_ServiceProvider_OwnerId] ON [dbo].[ServiceProvider] ([OwnerId]);
    CREATE INDEX [IX_ServiceProvider_StatusId] ON [dbo].[ServiceProvider] ([StatusId]);
    CREATE INDEX [IX_ServiceProvider_IsDeleted_IsVerified] ON [dbo].[ServiceProvider] ([IsDeleted], [IsVerified]) WHERE [IsDeleted] = 0;
    CREATE INDEX [IX_ServiceProvider_Lat_Lng] ON [dbo].[ServiceProvider] ([Latitude], [Longitude]);

    PRINT 'Table [ServiceProvider] created successfully.';
END
ELSE
    PRINT 'Table [ServiceProvider] already exists. Skipping.';
GO

-- ============================================================
-- 3. ServiceProviderAttachment
-- ============================================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ServiceProviderAttachment' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE [dbo].[ServiceProviderAttachment] (
        [Id]                    INT             IDENTITY(1,1) NOT NULL,
        [ServiceProviderId]     INT             NOT NULL,
        [FileSize]              BIGINT          NOT NULL,
        [FileExtension]         NVARCHAR(50)    NOT NULL,
        [FileName]              NVARCHAR(255)   NOT NULL,
        [ContentType]           NVARCHAR(50)    NOT NULL,
        [CreatedAt]             DATETIME        NOT NULL,
        [CreatedBy]             INT             NULL,
        [ModifiedAt]            DATETIME        NULL,
        [ModifiedBy]            INT             NULL,
        [IsDeleted]             BIT             NOT NULL DEFAULT 0,
        CONSTRAINT [PK_ServiceProviderAttachment] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [FK_ServiceProviderAttachment_ServiceProvider] FOREIGN KEY ([ServiceProviderId]) REFERENCES [dbo].[ServiceProvider]([Id])
    );

    CREATE INDEX [IX_ServiceProviderAttachment_ServiceProviderId] ON [dbo].[ServiceProviderAttachment] ([ServiceProviderId]);
    CREATE INDEX [IX_ServiceProviderAttachment_IsDeleted] ON [dbo].[ServiceProviderAttachment] ([IsDeleted]) WHERE [IsDeleted] = 0;

    PRINT 'Table [ServiceProviderAttachment] created successfully.';
END
ELSE
    PRINT 'Table [ServiceProviderAttachment] already exists. Skipping.';
GO

-- ============================================================
-- 4. ServiceProviderRate
-- ============================================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ServiceProviderRate' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE [dbo].[ServiceProviderRate] (
        [Id]                    INT             IDENTITY(1,1) NOT NULL,
        [ServiceProviderId]     INT             NOT NULL,
        [UserId]                INT             NOT NULL,
        [Rating]                INT             NOT NULL,
        [AVGRating]             FLOAT           NOT NULL,
        [Comment]               NVARCHAR(1000)  NULL,
        [CreatedAt]             DATETIME        NOT NULL,
        [CreatedBy]             INT             NULL,
        [ModifiedAt]            DATETIME        NULL,
        [ModifiedBy]            INT             NULL,
        [IsDeleted]             BIT             NOT NULL DEFAULT 0,
        CONSTRAINT [PK_ServiceProviderRate] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [FK_ServiceProviderRate_ServiceProvider] FOREIGN KEY ([ServiceProviderId]) REFERENCES [dbo].[ServiceProvider]([Id]),
        CONSTRAINT [FK_ServiceProviderRate_UserAccount] FOREIGN KEY ([UserId]) REFERENCES [dbo].[UserAccount]([Id])
    );

    CREATE INDEX [IX_ServiceProviderRate_ServiceProviderId] ON [dbo].[ServiceProviderRate] ([ServiceProviderId]);
    CREATE INDEX [IX_ServiceProviderRate_UserId] ON [dbo].[ServiceProviderRate] ([UserId]);
    CREATE INDEX [IX_ServiceProviderRate_UserId_ServiceProviderId] ON [dbo].[ServiceProviderRate] ([UserId], [ServiceProviderId]);

    PRINT 'Table [ServiceProviderRate] created successfully.';
END
ELSE
    PRINT 'Table [ServiceProviderRate] already exists. Skipping.';
GO

-- ============================================================
-- 5. UserFavoriteServiceProvider
-- ============================================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserFavoriteServiceProvider' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE [dbo].[UserFavoriteServiceProvider] (
        [Id]                    INT             IDENTITY(1,1) NOT NULL,
        [UserId]                INT             NOT NULL,
        [ServiceProviderId]     INT             NOT NULL,
        [CreatedAt]             DATETIME        NOT NULL,
        [CreatedBy]             INT             NULL,
        [ModifiedAt]            DATETIME        NULL,
        [ModifiedBy]            INT             NULL,
        [IsDeleted]             BIT             NOT NULL DEFAULT 0,
        CONSTRAINT [PK_UserFavoriteServiceProvider] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [FK_UserFavoriteServiceProvider_UserAccount] FOREIGN KEY ([UserId]) REFERENCES [dbo].[UserAccount]([Id]),
        CONSTRAINT [FK_UserFavoriteServiceProvider_ServiceProvider] FOREIGN KEY ([ServiceProviderId]) REFERENCES [dbo].[ServiceProvider]([Id])
    );

    CREATE UNIQUE INDEX [IX_UserFavoriteServiceProvider_Unique] ON [dbo].[UserFavoriteServiceProvider] ([UserId], [ServiceProviderId]) WHERE [IsDeleted] = 0;

    PRINT 'Table [UserFavoriteServiceProvider] created successfully.';
END
ELSE
    PRINT 'Table [UserFavoriteServiceProvider] already exists. Skipping.';
GO

-- ============================================================
-- 6. Seed Data: ServiceCategory
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM [dbo].[ServiceCategory] WHERE [Id] = 1)
BEGIN
    SET IDENTITY_INSERT [dbo].[ServiceCategory] ON;

    INSERT INTO [dbo].[ServiceCategory] ([Id], [Name], [NameAr], [SortOrder], [IsActive], [CreatedAt], [IsDeleted])
    VALUES
        (1, N'Car Wash',        N'غسيل سيارات',   1, 1, GETDATE(), 0),
        (2, N'Tire Service',    N'خدمة إطارات',    2, 1, GETDATE(), 0),
        (3, N'Oil Change',      N'تغيير زيت',      3, 1, GETDATE(), 0),
        (4, N'Towing',          N'سحب سيارات',     4, 1, GETDATE(), 0),
        (5, N'Car Detailing',   N'تنظيف وتلميع',   5, 1, GETDATE(), 0),
        (6, N'Battery Service', N'خدمة بطارية',    6, 1, GETDATE(), 0);

    SET IDENTITY_INSERT [dbo].[ServiceCategory] OFF;

    PRINT 'ServiceCategory seed data inserted successfully.';
END
ELSE
    PRINT 'ServiceCategory seed data already exists. Skipping.';
GO

-- ============================================================
-- Verification
-- ============================================================
PRINT '';
PRINT '=== Phase 0 Migration Summary ===';
PRINT '';

SELECT 'ServiceCategory' AS TableName, COUNT(*) AS RecordCount FROM [dbo].[ServiceCategory]
UNION ALL
SELECT 'ServiceProvider', COUNT(*) FROM [dbo].[ServiceProvider]
UNION ALL
SELECT 'ServiceProviderAttachment', COUNT(*) FROM [dbo].[ServiceProviderAttachment]
UNION ALL
SELECT 'ServiceProviderRate', COUNT(*) FROM [dbo].[ServiceProviderRate]
UNION ALL
SELECT 'UserFavoriteServiceProvider', COUNT(*) FROM [dbo].[UserFavoriteServiceProvider];

PRINT '';
PRINT '=== Phase 0 Migration Complete ===';
GO
