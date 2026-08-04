-- =============================================
-- Home Ads Phase 1: sellable, location-targeted home-screen ad slots
-- Cable EV Charging Station Management
-- =============================================
-- Per BE-home-ads-requirements.md (P0 + P1 + P2):
-- 1. Advertiser / Campaign        — billing links for sold placements
-- 2. AppSetting                   — admin-editable global NearbyRadiusKm (seed 30)
-- 3. Banner targeting columns     — targetType/city/center/radius, linked entity,
--                                   priority, campaign link
-- 4. Announcement (+UserState)    — dynamic bilingual welcome takeover + frequency caps
-- 5. ChargingPoint.ViewImage(+Status) — partner-uploaded, admin-reviewed promo creative
-- 6. AnalyticsEvent City/Lat/Lng  — geo proof on the tracking payload
--
-- Run on db_ab1977_cableproduction BEFORE deploying the build that contains
-- the home-ads endpoints. Idempotent: safe to re-run.
-- =============================================

-- 1a. Advertiser
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Advertiser')
BEGIN
    CREATE TABLE [dbo].[Advertiser] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [Name] NVARCHAR(255) NOT NULL,
        [Contact] NVARCHAR(500) NULL,
        [Notes] NVARCHAR(2000) NULL,
        [CreatedAt] DATETIME NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy] INT NULL, [ModifiedAt] DATETIME NULL, [ModifiedBy] INT NULL,
        [IsDeleted] BIT NOT NULL DEFAULT 0,
        CONSTRAINT [PK_Advertiser] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
    PRINT 'Created table: Advertiser';
END
GO

-- 1b. Campaign
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Campaign')
BEGIN
    CREATE TABLE [dbo].[Campaign] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [AdvertiserId] INT NOT NULL,
        [Type] NVARCHAR(20) NOT NULL,
        [CityArea] NVARCHAR(200) NULL,
        [StartDate] DATETIME NOT NULL,
        [EndDate] DATETIME NULL,
        [Price] DECIMAL(18,2) NOT NULL DEFAULT 0,
        [Status] INT NOT NULL DEFAULT 0,
        [CreatedAt] DATETIME NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy] INT NULL, [ModifiedAt] DATETIME NULL, [ModifiedBy] INT NULL,
        [IsDeleted] BIT NOT NULL DEFAULT 0,
        CONSTRAINT [PK_Campaign] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_Campaign_Advertiser] FOREIGN KEY ([AdvertiserId]) REFERENCES [dbo].[Advertiser] ([Id])
    );
    PRINT 'Created table: Campaign';
END
GO

-- 2. AppSetting + NearbyRadiusKm seed
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AppSetting')
BEGIN
    CREATE TABLE [dbo].[AppSetting] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [Key] NVARCHAR(100) NOT NULL,
        [Value] NVARCHAR(500) NOT NULL,
        [CreatedAt] DATETIME NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy] INT NULL, [ModifiedAt] DATETIME NULL, [ModifiedBy] INT NULL,
        [IsDeleted] BIT NOT NULL DEFAULT 0,
        CONSTRAINT [PK_AppSetting] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
    CREATE UNIQUE NONCLUSTERED INDEX [UX_AppSetting_Key] ON [dbo].[AppSetting] ([Key]);
    PRINT 'Created table: AppSetting';
END
GO

INSERT INTO [dbo].[AppSetting] ([Key], [Value])
SELECT 'NearbyRadiusKm', '30'
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[AppSetting] WHERE [Key] = 'NearbyRadiusKm');
GO

-- 3. Banner targeting columns
IF COL_LENGTH('dbo.Banner', 'TargetType') IS NULL
BEGIN
    ALTER TABLE [dbo].[Banner] ADD
        [TargetType] NVARCHAR(20) NOT NULL CONSTRAINT DF_Banner_TargetType DEFAULT 'national',
        [TargetCity] NVARCHAR(100) NULL,
        [CenterLat] FLOAT NULL,
        [CenterLng] FLOAT NULL,
        [RadiusKm] FLOAT NULL,
        [LinkedEntityType] NVARCHAR(20) NULL,
        [LinkedEntityId] INT NULL,
        [Priority] INT NULL,
        [CampaignId] INT NULL CONSTRAINT FK_Banner_Campaign REFERENCES [dbo].[Campaign]([Id]);
    PRINT 'Added Banner targeting columns';
END
GO

-- 4a. Announcement
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Announcement')
BEGIN
    CREATE TABLE [dbo].[Announcement] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [TitleEn] NVARCHAR(200) NOT NULL,
        [TitleAr] NVARCHAR(200) NOT NULL,
        [BodyEn] NVARCHAR(1000) NOT NULL,
        [BodyAr] NVARCHAR(1000) NOT NULL,
        [ImageUrl] NVARCHAR(1000) NULL,
        [ActionType] INT NULL,
        [ActionUrl] NVARCHAR(1000) NULL,
        [ActionLabelEn] NVARCHAR(100) NULL,
        [ActionLabelAr] NVARCHAR(100) NULL,
        [TargetType] NVARCHAR(20) NOT NULL DEFAULT 'national',
        [TargetCity] NVARCHAR(100) NULL,
        [CenterLat] FLOAT NULL,
        [CenterLng] FLOAT NULL,
        [RadiusKm] FLOAT NULL,
        [Audience] NVARCHAR(20) NOT NULL DEFAULT 'all',
        [StartDate] DATETIME NOT NULL,
        [EndDate] DATETIME NULL,
        [MaxPerDay] INT NOT NULL DEFAULT 1,
        [CooldownHours] INT NOT NULL DEFAULT 24,
        [MaxLifetime] INT NOT NULL DEFAULT 3,
        [StopOnDismiss] BIT NOT NULL DEFAULT 1,
        [IsActive] BIT NOT NULL DEFAULT 1,
        [CampaignId] INT NULL,
        [AdvertiserId] INT NULL,
        [CreatedAt] DATETIME NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy] INT NULL, [ModifiedAt] DATETIME NULL, [ModifiedBy] INT NULL,
        [IsDeleted] BIT NOT NULL DEFAULT 0,
        CONSTRAINT [PK_Announcement] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_Announcement_Campaign] FOREIGN KEY ([CampaignId]) REFERENCES [dbo].[Campaign] ([Id]),
        CONSTRAINT [FK_Announcement_Advertiser] FOREIGN KEY ([AdvertiserId]) REFERENCES [dbo].[Advertiser] ([Id])
    );
    PRINT 'Created table: Announcement';
END
GO

-- 4a-bis. Announcement action labels (Part A delta — table may pre-date these columns)
IF COL_LENGTH('dbo.Announcement', 'ActionLabelEn') IS NULL
BEGIN
    ALTER TABLE [dbo].[Announcement] ADD [ActionLabelEn] NVARCHAR(100) NULL, [ActionLabelAr] NVARCHAR(100) NULL;
    PRINT 'Added columns: Announcement.ActionLabelEn, ActionLabelAr';
END
GO

-- 4b. AnnouncementUserState (frequency caps)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AnnouncementUserState')
BEGIN
    CREATE TABLE [dbo].[AnnouncementUserState] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [AnnouncementId] INT NOT NULL,
        [UserId] INT NOT NULL,
        [ShownCount] INT NOT NULL DEFAULT 0,
        [DailyShownCount] INT NOT NULL DEFAULT 0,
        [LastShownAt] DATETIME NULL,
        [DismissedCount] INT NOT NULL DEFAULT 0,
        CONSTRAINT [PK_AnnouncementUserState] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_AnnouncementUserState_Announcement] FOREIGN KEY ([AnnouncementId]) REFERENCES [dbo].[Announcement] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AnnouncementUserState_UserAccount] FOREIGN KEY ([UserId]) REFERENCES [dbo].[UserAccount] ([Id])
    );
    CREATE UNIQUE NONCLUSTERED INDEX [UX_AnnouncementUserState_Announcement_User]
        ON [dbo].[AnnouncementUserState] ([AnnouncementId], [UserId]);
    PRINT 'Created table: AnnouncementUserState';
END
GO

-- 5. ChargingPoint view image (promo creative)
IF COL_LENGTH('dbo.ChargingPoint', 'ViewImage') IS NULL
BEGIN
    ALTER TABLE [dbo].[ChargingPoint] ADD [ViewImage] NVARCHAR(500) NULL, [ViewImageStatus] NVARCHAR(20) NULL;
    PRINT 'Added columns: ChargingPoint.ViewImage, ViewImageStatus';
END
GO

-- 6. AnalyticsEvent geo columns
IF COL_LENGTH('dbo.AnalyticsEvent', 'City') IS NULL
BEGIN
    ALTER TABLE [dbo].[AnalyticsEvent] ADD [City] NVARCHAR(100) NULL, [Lat] FLOAT NULL, [Lng] FLOAT NULL;
    PRINT 'Added columns: AnalyticsEvent.City, Lat, Lng';
END
GO
