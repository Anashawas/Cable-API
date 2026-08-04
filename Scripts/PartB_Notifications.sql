-- ============================================================================
-- Part B — Notifications (routing contract + worker approval + templates)
-- Idempotent. Safe to re-run.
-- ============================================================================

-- 1. NotificationType.DeepLinksTo (R5 routing map)
IF COL_LENGTH('dbo.NotificationType', 'DeepLinksTo') IS NULL
BEGIN
    ALTER TABLE [dbo].[NotificationType] ADD [DeepLinksTo] NVARCHAR(30) NULL;
    PRINT 'Added column: NotificationType.DeepLinksTo';
END
GO

-- Seed the routing behavior for the existing types (only where not yet set)
UPDATE dbo.NotificationType SET DeepLinksTo = CASE Id
    WHEN 1 THEN 'none'            -- system_announcement
    WHEN 2 THEN 'charging-point'  -- favorite_added
    WHEN 3 THEN 'charging-point'  -- favorite_removed
    WHEN 4 THEN 'charging-point'  -- charging_point_status_changed
    WHEN 5 THEN 'charging-point'  -- offer_available (lands on the station with the offer)
    WHEN 6 THEN 'charging-point'  -- rating_received
    WHEN 7 THEN 'complaint'       -- complaint_status_updated
    WHEN 8 THEN 'charging-point'  -- charging_session_started
    WHEN 9 THEN 'charging-point'  -- charging_session_completed
    WHEN 10 THEN 'charging-point' -- new_charging_point_nearby
    WHEN 11 THEN 'charging-point' -- update_request_submitted
    WHEN 12 THEN 'charging-point' -- update_request_decided
    WHEN 13 THEN 'provider'       -- provider_announcement (link decided per send)
    ELSE 'none' END
WHERE DeepLinksTo IS NULL;
GO

-- 1b. NotificationType display names (DECISION-2 — client-supplied EN/AR labels)
IF COL_LENGTH('dbo.NotificationType', 'NameEn') IS NULL
BEGIN
    ALTER TABLE [dbo].[NotificationType] ADD [NameEn] NVARCHAR(100) NULL, [NameAr] NVARCHAR(100) NULL;
    PRINT 'Added columns: NotificationType.NameEn, NameAr';
END
GO

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

-- 2. ProviderFavoriteNotification: worker-approval workflow (F3) + counts (F1)
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

-- 3. Auto-approve worker notifications toggle (F4)
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

-- 4. NotificationTemplate — admin-managed body suggestions (F5)
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
