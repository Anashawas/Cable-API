-- =============================================
-- Analytics Phase 1: Provider & Banner analytics engine
-- Cable EV Charging Station Management
-- =============================================
-- Creates 2 tables:
--   AnalyticsEvent       — append-only raw event log (source of truth)
--   AnalyticsDailyRollup — pre-aggregated daily counters (fast dashboards)
--
-- Polymorphic via (EntityType, EntityId):
--   EntityType in ('ChargingPoint', 'ServiceProvider', 'Banner')
-- EventType maps to Cable.Core.Emuns.AnalyticsEventType:
--   1 FullView, 2 HalfView, 3 CallButtonClick, 4 MapClick,
--   20 BannerView, 21 BannerClick
--
-- >>> RUN AGAINST THE DEV DATABASE ONLY — NOT PRODUCTION <<<
-- Idempotent: safe to re-run.
-- =============================================

-- 1. AnalyticsEvent — append-only event log
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AnalyticsEvent')
BEGIN
    CREATE TABLE [dbo].[AnalyticsEvent] (
        [Id]          INT IDENTITY(1,1) NOT NULL,
        [EntityType]  NVARCHAR(50)      NOT NULL,
        [EntityId]    INT               NOT NULL,
        [EventType]   INT               NOT NULL,
        [UserId]      INT               NULL,
        [AnonymousId] NVARCHAR(100)     NULL,
        [Source]      NVARCHAR(20)      NULL,
        [OccurredAt]  DATETIME          NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_AnalyticsEvent] PRIMARY KEY CLUSTERED ([Id] ASC)
    );

    CREATE NONCLUSTERED INDEX [IX_AnalyticsEvent_Entity_Event_OccurredAt]
        ON [dbo].[AnalyticsEvent] ([EntityType], [EntityId], [EventType], [OccurredAt]);

    CREATE NONCLUSTERED INDEX [IX_AnalyticsEvent_OccurredAt]
        ON [dbo].[AnalyticsEvent] ([OccurredAt]);

    CREATE NONCLUSTERED INDEX [IX_AnalyticsEvent_UserId]
        ON [dbo].[AnalyticsEvent] ([UserId]);

    PRINT 'Created table: AnalyticsEvent';
END
GO

-- 2. AnalyticsDailyRollup — pre-aggregated daily counters
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AnalyticsDailyRollup')
BEGIN
    CREATE TABLE [dbo].[AnalyticsDailyRollup] (
        [Id]          INT IDENTITY(1,1) NOT NULL,
        [EntityType]  NVARCHAR(50)      NOT NULL,
        [EntityId]    INT               NOT NULL,
        [EventType]   INT               NOT NULL,
        [Day]         DATE              NOT NULL,
        [Count]       INT               NOT NULL DEFAULT 0,
        [LastEventAt] DATETIME          NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_AnalyticsDailyRollup] PRIMARY KEY CLUSTERED ([Id] ASC)
    );

    CREATE UNIQUE NONCLUSTERED INDEX [UX_AnalyticsDailyRollup_Entity_Event_Day]
        ON [dbo].[AnalyticsDailyRollup] ([EntityType], [EntityId], [EventType], [Day]);

    PRINT 'Created table: AnalyticsDailyRollup';
END
GO
