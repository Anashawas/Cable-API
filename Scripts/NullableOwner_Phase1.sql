-- =============================================
-- Nullable Owner Phase 1: unassignable providers
-- Cable EV Charging Station Management
-- =============================================
-- Makes three columns nullable so stations/providers can be "unassigned":
--   ChargingPoint.OwnerId                       (null = unassigned station)
--   ServiceProvider.OwnerId                     (null = unassigned provider)
--   ProviderWalletTransaction.RecordedByUserId  (null = system entry on unassigned provider)
-- FKs and indexes are dropped and recreated around each ALTER.
--
-- >>> RUN AGAINST THE DEV DATABASE ONLY — NOT PRODUCTION (until release) <<<
-- Idempotent: safe to re-run.
-- =============================================

-- 1. ChargingPoint.OwnerId -> NULL
IF COLUMNPROPERTY(OBJECT_ID('dbo.ChargingPoint'), 'OwnerId', 'AllowsNull') = 0
BEGIN
    ALTER TABLE [dbo].[ChargingPoint] DROP CONSTRAINT [FK_ChargingPoint_UserAccount];
    ALTER TABLE [dbo].[ChargingPoint] ALTER COLUMN [OwnerId] INT NULL;
    ALTER TABLE [dbo].[ChargingPoint]
        ADD CONSTRAINT [FK_ChargingPoint_UserAccount]
        FOREIGN KEY ([OwnerId]) REFERENCES [dbo].[UserAccount] ([Id]);
    PRINT 'ChargingPoint.OwnerId is now nullable';
END
GO

-- 2. ServiceProvider.OwnerId -> NULL
IF COLUMNPROPERTY(OBJECT_ID('dbo.ServiceProvider'), 'OwnerId', 'AllowsNull') = 0
BEGIN
    ALTER TABLE [dbo].[ServiceProvider] DROP CONSTRAINT [FK_ServiceProvider_UserAccount];
    DROP INDEX [IX_ServiceProvider_OwnerId] ON [dbo].[ServiceProvider];
    ALTER TABLE [dbo].[ServiceProvider] ALTER COLUMN [OwnerId] INT NULL;
    CREATE NONCLUSTERED INDEX [IX_ServiceProvider_OwnerId] ON [dbo].[ServiceProvider] ([OwnerId]);
    ALTER TABLE [dbo].[ServiceProvider]
        ADD CONSTRAINT [FK_ServiceProvider_UserAccount]
        FOREIGN KEY ([OwnerId]) REFERENCES [dbo].[UserAccount] ([Id]);
    PRINT 'ServiceProvider.OwnerId is now nullable';
END
GO

-- 3. ProviderWalletTransaction.RecordedByUserId -> NULL
IF COLUMNPROPERTY(OBJECT_ID('dbo.ProviderWalletTransaction'), 'RecordedByUserId', 'AllowsNull') = 0
BEGIN
    ALTER TABLE [dbo].[ProviderWalletTransaction] DROP CONSTRAINT [FK_ProviderWalletTransaction_RecordedByUser];
    DROP INDEX [IX_ProviderWalletTransaction_RecordedByUser] ON [dbo].[ProviderWalletTransaction];
    ALTER TABLE [dbo].[ProviderWalletTransaction] ALTER COLUMN [RecordedByUserId] INT NULL;
    CREATE NONCLUSTERED INDEX [IX_ProviderWalletTransaction_RecordedByUser]
        ON [dbo].[ProviderWalletTransaction] ([RecordedByUserId]);
    ALTER TABLE [dbo].[ProviderWalletTransaction]
        ADD CONSTRAINT [FK_ProviderWalletTransaction_RecordedByUser]
        FOREIGN KEY ([RecordedByUserId]) REFERENCES [dbo].[UserAccount] ([Id]);
    PRINT 'ProviderWalletTransaction.RecordedByUserId is now nullable';
END
GO
