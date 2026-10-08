-- Price alerts (time-of-use tariff → push before a window starts). Idempotent.
-- Apply to dev first, then production with the API build that contains PricingRoutes.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.TouTariff', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TouTariff
    (
        Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TouTariff PRIMARY KEY,
        Version       INT           NOT NULL,                 -- bumps on every edit; the app reschedules on change
        EffectiveFrom DATETIME2(0)  NOT NULL,                 -- UTC, like every other datetime; the API converts to Asia/Amman on output
        Timezone      NVARCHAR(40)  NOT NULL CONSTRAINT DF_TouTariff_Timezone DEFAULT 'Asia/Amman',
        Currency      NVARCHAR(3)   NOT NULL CONSTRAINT DF_TouTariff_Currency DEFAULT 'JOD',
        Unit          NVARCHAR(20)  NOT NULL CONSTRAINT DF_TouTariff_Unit DEFAULT 'fils/kWh',
        IsActive      BIT           NOT NULL CONSTRAINT DF_TouTariff_IsActive DEFAULT 1,
        Note          NVARCHAR(300) NULL,
        CreatedBy     INT           NULL,
        CreatedAt     DATETIME      NOT NULL,
        ModifiedBy    INT           NULL,
        ModifiedAt    DATETIME      NULL,
        IsDeleted     BIT           NOT NULL CONSTRAINT DF_TouTariff_IsDeleted DEFAULT 0
    );
    PRINT 'Created dbo.TouTariff';
END
ELSE PRINT 'SKIP dbo.TouTariff exists';
GO

IF OBJECT_ID('dbo.TouTariffWindow', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TouTariffWindow
    (
        Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TouTariffWindow PRIMARY KEY,
        TouTariffId INT          NOT NULL,
        [Key]       NVARCHAR(30) NOT NULL,   -- stable id user preferences are stored against; never renamed
        NameEn      NVARCHAR(60) NOT NULL,
        NameAr      NVARCHAR(60) NOT NULL,
        StartMin    INT          NOT NULL,   -- minutes from midnight, Asia/Amman
        EndMin      INT          NOT NULL,   -- > 1440 when the window crosses midnight
        Tier        NVARCHAR(20) NOT NULL,   -- offPeak | partial | peak
        PriceFils   INT          NOT NULL,
        SortOrder   INT          NOT NULL,
        CONSTRAINT FK_TouTariffWindow_TouTariff FOREIGN KEY (TouTariffId) REFERENCES dbo.TouTariff (Id) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX UX_TouTariffWindow_Tariff_Key ON dbo.TouTariffWindow (TouTariffId, [Key]);
    PRINT 'Created dbo.TouTariffWindow';
END
ELSE PRINT 'SKIP dbo.TouTariffWindow exists';
GO

-- Seed: the tariff the app ships today (station boards, incl. commission). Version 1.
IF NOT EXISTS (SELECT 1 FROM dbo.TouTariff WHERE IsDeleted = 0)
BEGIN
    INSERT INTO dbo.TouTariff (Version, EffectiveFrom, Timezone, Currency, Unit, IsActive, Note, CreatedAt)
    VALUES (1, '2026-03-31T21:00:00', 'Asia/Amman', 'JOD', 'fils/kWh', 1, 'Moved from the app (time_of_use_pricing.dart) unchanged', GETUTCDATE());   -- UTC = 2026-04-01 00:00 Asia/Amman; the API presents Jordan time
    DECLARE @t INT = SCOPE_IDENTITY();
    INSERT INTO dbo.TouTariffWindow (TouTariffId, [Key], NameEn, NameAr, StartMin, EndMin, Tier, PriceFils, SortOrder) VALUES
        (@t, 'offPeak', 'Off-peak',     N'خارج الذروة', 300,  840,  'offPeak', 183, 1),
        (@t, 'midday',  'Partial peak', N'ذروة جزئية',  840,  1020, 'partial', 193, 2),
        (@t, 'peak',    'Peak',         N'الذروة',      1020, 1380, 'peak',    213, 3),
        (@t, 'night',   'Partial peak', N'ذروة جزئية',  1380, 1740, 'partial', 193, 4);
    PRINT 'Seeded TouTariff v1 with 4 windows';
END
ELSE PRINT 'SKIP TouTariff already seeded';
GO

IF OBJECT_ID('dbo.UserPriceAlert', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserPriceAlert
    (
        UserAccountId INT           NOT NULL CONSTRAINT PK_UserPriceAlert PRIMARY KEY,
        IsEnabled     BIT           NOT NULL CONSTRAINT DF_UserPriceAlert_IsEnabled DEFAULT 0,
        LeadMinutes   INT           NOT NULL CONSTRAINT DF_UserPriceAlert_LeadMinutes DEFAULT 15,   -- 15 | 30 | 45 | 60
        Windows       NVARCHAR(200) NOT NULL CONSTRAINT DF_UserPriceAlert_Windows DEFAULT '',       -- CSV of window keys
        UpdatedAt     DATETIME2(0)  NOT NULL,
        CONSTRAINT FK_UserPriceAlert_UserAccount FOREIGN KEY (UserAccountId) REFERENCES dbo.UserAccount (Id) ON DELETE CASCADE
    );
    PRINT 'Created dbo.UserPriceAlert';
END
ELSE PRINT 'SKIP dbo.UserPriceAlert exists';
GO

IF OBJECT_ID('dbo.PriceAlertLog', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PriceAlertLog
    (
        Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PriceAlertLog PRIMARY KEY,
        UserAccountId INT          NOT NULL,
        WindowKey     NVARCHAR(30) NOT NULL,
        AlertDate     DATE         NOT NULL,   -- Jordan date of the window start
        SentAt        DATETIME2(0) NOT NULL,
        LeadMinutes   INT          NOT NULL,
        Language      NVARCHAR(5)  NULL
    );
    CREATE UNIQUE INDEX UX_PriceAlertLog_User_Window_Date ON dbo.PriceAlertLog (UserAccountId, WindowKey, AlertDate);
    PRINT 'Created dbo.PriceAlertLog';
END
ELSE PRINT 'SKIP dbo.PriceAlertLog exists';
GO

-- The device's language, so a push sent by a job (no request, no Accept-Language) can be localised.
IF COL_LENGTH('dbo.NotificationToken', 'Language') IS NULL
BEGIN
    ALTER TABLE dbo.NotificationToken ADD Language NVARCHAR(5) NULL;
    PRINT 'Added NotificationToken.Language';
END
ELSE PRINT 'SKIP NotificationToken.Language exists';
GO

-- Quiet hours (Asia/Amman, HH:mm): an alert that falls inside is cancelled, not delayed.
IF NOT EXISTS (SELECT 1 FROM dbo.AppSetting WHERE [Key] = 'PriceAlerts.QuietFrom' AND IsDeleted = 0)
    INSERT INTO dbo.AppSetting ([Key], Value, CreatedAt, IsDeleted) VALUES ('PriceAlerts.QuietFrom', '23:30', GETUTCDATE(), 0);
IF NOT EXISTS (SELECT 1 FROM dbo.AppSetting WHERE [Key] = 'PriceAlerts.QuietTo' AND IsDeleted = 0)
    INSERT INTO dbo.AppSetting ([Key], Value, CreatedAt, IsDeleted) VALUES ('PriceAlerts.QuietTo', '06:30', GETUTCDATE(), 0);
GO
