/* =============================================================================
   CABLE — DELTA DEPLOYMENT SCRIPT — 2026-07-31
   =============================================================================
   Moves everything changed on dev since the last production deployment
   (2026-07-29). Verified against a LIVE schema diff of
       dev  = db_ab1977_cable
       prod = db_ab1977_cableproduction
   run on 2026-07-31 — the changes below are EXACTLY what prod is missing.

   Contents:
     1. Announcement action labels            (Part A delta — flat contract)
     2. NotificationType routing map + EN/AR display names (Part B R5 + DECISION-2)
     3. ProviderFavoriteNotification worker-approval workflow (Part B F1/F3)
     4. Auto-approve worker notifications toggle (Part B F4)
     5. NotificationTemplate table + starter template (Part B F5)
     6. Verification report

   HOW TO RUN
     1. BACKUP db_ab1977_cableproduction first.
     2. Run this whole file on db_ab1977_cableproduction (SSMS).
     3. Publish the new API build after (all changes are ADDITIVE — the old
        build keeps working between script and publish; no outage window,
        unlike the ChargerBrand deploy).
     4. Read the verification report at the end of the output.

   Idempotent: safe to re-run. Every statement is guarded.
   NOTE: prod's UserAccount has legacy columns OriginalCity/OriginalCountry
   that dev lacks — intentionally left untouched.
   ============================================================================= */
PRINT 'Running on database: ' + DB_NAME();
IF DB_NAME() NOT LIKE '%cableproduction%'
    PRINT '*** WARNING: this does not look like the production database! ***';
GO

/* ---------------------------------------------------------------------------
   1. Announcement — optional CTA labels (flat contract, Part A)
   --------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Announcement', 'ActionLabelEn') IS NULL
BEGIN
    ALTER TABLE [dbo].[Announcement] ADD [ActionLabelEn] NVARCHAR(100) NULL, [ActionLabelAr] NVARCHAR(100) NULL;
    PRINT 'Added columns: Announcement.ActionLabelEn, ActionLabelAr';
END
GO

/* ---------------------------------------------------------------------------
   2. NotificationType — routing map (R5) + display names (DECISION-2)
   --------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.NotificationType', 'DeepLinksTo') IS NULL
BEGIN
    ALTER TABLE [dbo].[NotificationType] ADD [DeepLinksTo] NVARCHAR(30) NULL;
    PRINT 'Added column: NotificationType.DeepLinksTo';
END
GO

IF COL_LENGTH('dbo.NotificationType', 'NameEn') IS NULL
BEGIN
    ALTER TABLE [dbo].[NotificationType] ADD [NameEn] NVARCHAR(100) NULL, [NameAr] NVARCHAR(100) NULL;
    PRINT 'Added columns: NotificationType.NameEn, NameAr';
END
GO

-- Routing map (client-locked list; id 3 = favorite_removed is DROPPED from the
-- contract — row kept for referential integrity, never emitted)
UPDATE dbo.NotificationType SET DeepLinksTo = CASE Id
    WHEN 1 THEN 'none'
    WHEN 2 THEN 'charging-point'
    WHEN 3 THEN 'charging-point'
    WHEN 4 THEN 'charging-point'
    WHEN 5 THEN 'charging-point'
    WHEN 6 THEN 'charging-point'
    WHEN 7 THEN 'complaint'
    WHEN 8 THEN 'charging-point'
    WHEN 9 THEN 'charging-point'
    WHEN 10 THEN 'charging-point'
    WHEN 11 THEN 'charging-point'
    WHEN 12 THEN 'charging-point'
    WHEN 13 THEN 'provider'
    ELSE 'none' END
WHERE DeepLinksTo IS NULL;
GO

-- Display names (client-supplied, verbatim). NameAr drives the partner
-- auto-title: "{NameAr} من {stationName}".
UPDATE dbo.NotificationType SET
  NameEn = CASE Id
    WHEN 1 THEN N'System Announcement' WHEN 2 THEN N'New Follower'
    WHEN 4 THEN N'Station Status Update' WHEN 5 THEN N'New Offer'
    WHEN 6 THEN N'New Rating' WHEN 7 THEN N'Complaint Update'
    WHEN 8 THEN N'Charging Started' WHEN 9 THEN N'Charging Complete'
    WHEN 10 THEN N'New Station Nearby' WHEN 11 THEN N'New Update Request'
    WHEN 12 THEN N'Update Request Decision' WHEN 13 THEN N'Announcement'
    ELSE NameEn END,
  NameAr = CASE Id
    WHEN 1 THEN N'إعلان عام' WHEN 2 THEN N'متابِع جديد'
    WHEN 4 THEN N'تحديث حالة المحطة' WHEN 5 THEN N'عرض جديد'
    WHEN 6 THEN N'تقييم جديد' WHEN 7 THEN N'تحديث الشكوى'
    WHEN 8 THEN N'بدء الشحن' WHEN 9 THEN N'انتهاء الشحن'
    WHEN 10 THEN N'محطة جديدة قريبة' WHEN 11 THEN N'طلب تعديل جديد'
    WHEN 12 THEN N'قرار طلب التعديل' WHEN 13 THEN N'إعلان'
    ELSE NameAr END
WHERE Id IN (1,2,4,5,6,7,8,9,10,11,12,13) AND (NameEn IS NULL OR NameAr IS NULL);
GO

/* ---------------------------------------------------------------------------
   3. ProviderFavoriteNotification — worker approval workflow (F3) + counts (F1)
      Existing rows become Status='sent' via the default (they were all real sends).
   --------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.ProviderFavoriteNotification', 'Status') IS NULL
BEGIN
    ALTER TABLE [dbo].[ProviderFavoriteNotification] ADD
        [Status] NVARCHAR(10) NOT NULL CONSTRAINT DF_ProviderFavoriteNotification_Status DEFAULT 'sent',
        [NotificationTypeId] INT NULL,
        [BatchId] UNIQUEIDENTIFIER NULL,
        [DeliveredCount] INT NULL,
        [DecidedByUserId] INT NULL,
        [DecidedAt] DATETIME NULL,
        [SentAt] DATETIME NULL;
    PRINT 'Added ProviderFavoriteNotification workflow columns';
END
GO

/* ---------------------------------------------------------------------------
   4. Auto-approve worker notifications (F4) — default OFF
   --------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.ChargingPoint', 'AutoApproveWorkerNotifications') IS NULL
BEGIN
    ALTER TABLE [dbo].[ChargingPoint] ADD [AutoApproveWorkerNotifications] BIT NOT NULL
        CONSTRAINT DF_ChargingPoint_AutoApproveWorkerNotifications DEFAULT 0;
    PRINT 'Added column: ChargingPoint.AutoApproveWorkerNotifications';
END
GO

IF COL_LENGTH('dbo.ServiceProvider', 'AutoApproveWorkerNotifications') IS NULL
BEGIN
    ALTER TABLE [dbo].[ServiceProvider] ADD [AutoApproveWorkerNotifications] BIT NOT NULL
        CONSTRAINT DF_ServiceProvider_AutoApproveWorkerNotifications DEFAULT 0;
    PRINT 'Added column: ServiceProvider.AutoApproveWorkerNotifications';
END
GO

/* ---------------------------------------------------------------------------
   5. NotificationTemplate — admin-managed body suggestions (F5)
   --------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'NotificationTemplate')
BEGIN
    CREATE TABLE [dbo].[NotificationTemplate] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [NotificationTypeId] INT NOT NULL,
        [Body] NVARCHAR(1000) NOT NULL,
        [CreatedAt] DATETIME NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy] INT NULL, [ModifiedAt] DATETIME NULL, [ModifiedBy] INT NULL,
        [IsDeleted] BIT NOT NULL DEFAULT 0,
        CONSTRAINT [PK_NotificationTemplate] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_NotificationTemplate_NotificationType] FOREIGN KEY ([NotificationTypeId])
            REFERENCES [dbo].[NotificationType] ([Id])
    );
    CREATE NONCLUSTERED INDEX [IX_NotificationTemplate_NotificationTypeId]
        ON [dbo].[NotificationTemplate] ([NotificationTypeId]);
    PRINT 'Created table: NotificationTemplate';
END
GO

-- Starter template for provider announcements (admins manage the rest from the portal)
IF NOT EXISTS (SELECT 1 FROM dbo.NotificationTemplate WHERE IsDeleted = 0)
BEGIN
    INSERT INTO dbo.NotificationTemplate (NotificationTypeId, Body, CreatedAt, IsDeleted)
    SELECT Id, N'لدينا عرض جديد اليوم — تفضلوا بزيارتنا!', GETUTCDATE(), 0
    FROM dbo.NotificationType WHERE Name = 'provider_announcement';
    IF @@ROWCOUNT > 0 PRINT 'Seeded starter notification template (provider_announcement)';
END
GO

/* =============================================================================
   6. Verification report (read this after the run)
   ============================================================================= */
SELECT Item, Result FROM (VALUES
 ('Col Announcement.ActionLabelEn',   CASE WHEN COL_LENGTH('dbo.Announcement','ActionLabelEn') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Col Announcement.ActionLabelAr',   CASE WHEN COL_LENGTH('dbo.Announcement','ActionLabelAr') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Col NotificationType.DeepLinksTo', CASE WHEN COL_LENGTH('dbo.NotificationType','DeepLinksTo') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Col NotificationType.NameEn',      CASE WHEN COL_LENGTH('dbo.NotificationType','NameEn') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Col NotificationType.NameAr',      CASE WHEN COL_LENGTH('dbo.NotificationType','NameAr') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Routing map seeded (13 rows)',     CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.NotificationType WHERE DeepLinksTo IS NULL) THEN 'OK' ELSE 'CHECK' END),
 ('Display names seeded (12 rows)',   CASE WHEN (SELECT COUNT(*) FROM dbo.NotificationType WHERE NameAr IS NOT NULL) >= 12 THEN 'OK' ELSE 'CHECK' END),
 ('NameAr of provider_announcement',  ISNULL((SELECT NameAr FROM dbo.NotificationType WHERE Id = 13), 'MISSING')),
 ('Col PFN.Status',                   CASE WHEN COL_LENGTH('dbo.ProviderFavoriteNotification','Status') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Col PFN.BatchId',                  CASE WHEN COL_LENGTH('dbo.ProviderFavoriteNotification','BatchId') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Col PFN.DeliveredCount',           CASE WHEN COL_LENGTH('dbo.ProviderFavoriteNotification','DeliveredCount') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Col PFN.SentAt',                   CASE WHEN COL_LENGTH('dbo.ProviderFavoriteNotification','SentAt') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Legacy PFN rows Status=sent',      CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.ProviderFavoriteNotification WHERE Status IS NULL) THEN 'OK' ELSE 'CHECK' END),
 ('Col ChargingPoint.AutoApproveWorkerNotifications', CASE WHEN COL_LENGTH('dbo.ChargingPoint','AutoApproveWorkerNotifications') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Col ServiceProvider.AutoApproveWorkerNotifications', CASE WHEN COL_LENGTH('dbo.ServiceProvider','AutoApproveWorkerNotifications') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Table NotificationTemplate',       CASE WHEN OBJECT_ID('dbo.NotificationTemplate') IS NOT NULL THEN 'OK' ELSE 'MISSING' END),
 ('Test account App Store Review kept', CASE WHEN EXISTS (SELECT 1 FROM dbo.UserAccount WHERE Email = 'Test.Review@gmail.com' AND IsDeleted = 0) THEN 'OK' ELSE 'MISSING' END),
 ('Demo station (IsTest=1) kept',     CASE WHEN EXISTS (SELECT 1 FROM dbo.ChargingPoint WHERE Name = N'Cable Demo Station (Test)' AND IsTest = 1 AND IsDeleted = 0) THEN 'OK' ELSE 'MISSING' END)
) v(Item, Result);
PRINT '=== Delta deployment script finished — review the verification report above ===';
