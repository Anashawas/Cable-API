-- =============================================
-- Favorites Notify Phase 1: provider announcements to favorited users
-- Cable EV Charging Station Management
-- =============================================
-- 1. ProviderFavoriteNotification — immutable audit of every announcement a
--    provider sends to its fans (also the per-provider rate-limit source)
-- 2. NotificationType seed 'provider_announcement' (resolved by name)
--
-- Run on db_ab1977_cableproduction BEFORE deploying the build that contains
-- the favorites-notify endpoint. Idempotent: safe to re-run.
-- =============================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ProviderFavoriteNotification')
BEGIN
    CREATE TABLE [dbo].[ProviderFavoriteNotification] (
        [Id]             INT IDENTITY(1,1) NOT NULL,
        [ProviderType]   NVARCHAR(50)   NOT NULL,
        [ProviderId]     INT            NOT NULL,
        [SentByUserId]   INT            NOT NULL,
        [Title]          NVARCHAR(256)  NOT NULL,
        [Body]           NVARCHAR(1000) NOT NULL,
        [RecipientCount] INT            NOT NULL,
        [CreatedAt]      DATETIME       NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy]      INT            NULL,
        [ModifiedAt]     DATETIME       NULL,
        [ModifiedBy]     INT            NULL,
        [IsDeleted]      BIT            NOT NULL DEFAULT 0,
        CONSTRAINT [PK_ProviderFavoriteNotification] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_ProviderFavoriteNotification_UserAccount] FOREIGN KEY ([SentByUserId]) REFERENCES [dbo].[UserAccount] ([Id])
    );

    CREATE NONCLUSTERED INDEX [IX_ProviderFavoriteNotification_Provider_CreatedAt]
        ON [dbo].[ProviderFavoriteNotification] ([ProviderType], [ProviderId], [CreatedAt]);

    PRINT 'Created table: ProviderFavoriteNotification';
END
GO

INSERT INTO [dbo].[NotificationType] ([Name], [Description])
SELECT 'provider_announcement', 'A provider sent an announcement to users who favorited it'
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[NotificationType] t WHERE t.Name = 'provider_announcement');
GO
