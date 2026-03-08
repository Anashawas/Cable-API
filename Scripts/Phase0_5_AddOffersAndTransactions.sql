-- =====================================================
-- Phase 0.5: Offers & Transactions Tables
-- Cable Platform - Offers, Transactions, Settlements
-- Execute on: db_ab1977_cable (Dev)
-- =====================================================

-- 1. PointsConversionRate
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PointsConversionRate')
BEGIN
    CREATE TABLE [dbo].[PointsConversionRate] (
        [Id]            INT IDENTITY(1,1) NOT NULL,
        [Name]          NVARCHAR(255)     NOT NULL,
        [CurrencyCode]  NVARCHAR(10)      NOT NULL,
        [PointsPerUnit] FLOAT             NOT NULL,
        [IsDefault]     BIT               NOT NULL DEFAULT 0,
        [IsActive]      BIT               NOT NULL DEFAULT 1,
        [CreatedAt]     DATETIME          NOT NULL DEFAULT GETDATE(),
        [CreatedBy]     INT               NULL,
        [ModifiedAt]    DATETIME          NULL,
        [ModifiedBy]    INT               NULL,
        [IsDeleted]     BIT               NOT NULL DEFAULT 0,
        CONSTRAINT [PK_PointsConversionRate] PRIMARY KEY CLUSTERED ([Id])
    );

    CREATE INDEX [IX_PointsConversionRate_IsDefault]
        ON [dbo].[PointsConversionRate] ([IsDefault]);

    CREATE INDEX [IX_PointsConversionRate_IsActive_IsDeleted]
        ON [dbo].[PointsConversionRate] ([IsActive], [IsDeleted]);

    PRINT 'Created table: PointsConversionRate';
END
ELSE
    PRINT 'Table already exists: PointsConversionRate';
GO

-- 2. ProviderOffer
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ProviderOffer')
BEGIN
    CREATE TABLE [dbo].[ProviderOffer] (
        [Id]                        INT IDENTITY(1,1) NOT NULL,
        [Title]                     NVARCHAR(255)     NOT NULL,
        [TitleAr]                   NVARCHAR(255)     NULL,
        [Description]               NVARCHAR(1000)    NULL,
        [DescriptionAr]             NVARCHAR(1000)    NULL,
        [ProviderType]              NVARCHAR(50)      NOT NULL,
        [ProviderId]                INT               NOT NULL,
        [ProposedByUserId]          INT               NOT NULL,
        [ApprovalStatus]            INT               NOT NULL DEFAULT 1,
        [ApprovedByUserId]          INT               NULL,
        [ApprovalNote]              NVARCHAR(500)     NULL,
        [ApprovedAt]                DATETIME          NULL,
        [CommissionPercentage]      FLOAT             NOT NULL,
        [PointsRewardPercentage]    FLOAT             NOT NULL,
        [PointsConversionRateId]    INT               NULL,
        [MinTransactionAmount]      FLOAT             NULL,
        [MaxTransactionAmount]      FLOAT             NULL,
        [MaxUsesPerUser]            INT               NULL,
        [MaxTotalUses]              INT               NULL,
        [CurrentTotalUses]          INT               NOT NULL DEFAULT 0,
        [OfferCodeExpiryMinutes]    INT               NOT NULL DEFAULT 30,
        [ImageUrl]                  NVARCHAR(500)     NULL,
        [ValidFrom]                 DATETIME          NOT NULL,
        [ValidTo]                   DATETIME          NULL,
        [IsActive]                  BIT               NOT NULL DEFAULT 1,
        [CreatedAt]                 DATETIME          NOT NULL DEFAULT GETDATE(),
        [CreatedBy]                 INT               NULL,
        [ModifiedAt]                DATETIME          NULL,
        [ModifiedBy]                INT               NULL,
        [IsDeleted]                 BIT               NOT NULL DEFAULT 0,
        CONSTRAINT [PK_ProviderOffer] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [FK_ProviderOffer_ProposedByUser] FOREIGN KEY ([ProposedByUserId])
            REFERENCES [dbo].[UserAccount]([Id]),
        CONSTRAINT [FK_ProviderOffer_ApprovedByUser] FOREIGN KEY ([ApprovedByUserId])
            REFERENCES [dbo].[UserAccount]([Id]),
        CONSTRAINT [FK_ProviderOffer_PointsConversionRate] FOREIGN KEY ([PointsConversionRateId])
            REFERENCES [dbo].[PointsConversionRate]([Id])
    );

    CREATE INDEX [IX_ProviderOffer_ProviderType_ProviderId]
        ON [dbo].[ProviderOffer] ([ProviderType], [ProviderId]);

    CREATE INDEX [IX_ProviderOffer_ApprovalStatus]
        ON [dbo].[ProviderOffer] ([ApprovalStatus]);

    CREATE INDEX [IX_ProviderOffer_IsActive_ValidFrom_ValidTo]
        ON [dbo].[ProviderOffer] ([IsActive], [ValidFrom], [ValidTo]);

    CREATE INDEX [IX_ProviderOffer_ProposedByUserId]
        ON [dbo].[ProviderOffer] ([ProposedByUserId]);

    CREATE INDEX [IX_ProviderOffer_IsDeleted]
        ON [dbo].[ProviderOffer] ([IsDeleted]);

    PRINT 'Created table: ProviderOffer';
END
ELSE
    PRINT 'Table already exists: ProviderOffer';
GO

-- 3. OfferTransaction
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'OfferTransaction')
BEGIN
    CREATE TABLE [dbo].[OfferTransaction] (
        [Id]                        INT IDENTITY(1,1)   NOT NULL,
        [ProviderOfferId]           INT                 NOT NULL,
        [UserId]                    INT                 NOT NULL,
        [OfferCode]                 NVARCHAR(20)        NOT NULL,
        [Status]                    INT                 NOT NULL DEFAULT 1,
        [TransactionAmount]         DECIMAL(18,3)       NULL,
        [CurrencyCode]              NVARCHAR(10)        NULL,
        [CommissionPercentage]      FLOAT               NOT NULL,
        [CommissionAmount]          DECIMAL(18,3)       NULL,
        [PointsRewardPercentage]    FLOAT               NOT NULL,
        [PointsConversionRate]      FLOAT               NOT NULL,
        [PointsEligibleAmount]      DECIMAL(18,3)       NULL,
        [PointsAwarded]             INT                 NULL,
        [ProviderType]              NVARCHAR(50)        NOT NULL,
        [ProviderId]                INT                 NOT NULL,
        [ConfirmedByUserId]         INT                 NULL,
        [CodeExpiresAt]             DATETIME            NOT NULL,
        [CompletedAt]               DATETIME            NULL,
        [CreatedAt]                 DATETIME            NOT NULL DEFAULT GETDATE(),
        [CreatedBy]                 INT                 NULL,
        [ModifiedAt]                DATETIME            NULL,
        [ModifiedBy]                INT                 NULL,
        [IsDeleted]                 BIT                 NOT NULL DEFAULT 0,
        CONSTRAINT [PK_OfferTransaction] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [FK_OfferTransaction_ProviderOffer] FOREIGN KEY ([ProviderOfferId])
            REFERENCES [dbo].[ProviderOffer]([Id]),
        CONSTRAINT [FK_OfferTransaction_User] FOREIGN KEY ([UserId])
            REFERENCES [dbo].[UserAccount]([Id]),
        CONSTRAINT [FK_OfferTransaction_ConfirmedByUser] FOREIGN KEY ([ConfirmedByUserId])
            REFERENCES [dbo].[UserAccount]([Id])
    );

    CREATE UNIQUE INDEX [IX_OfferTransaction_OfferCode_Unique]
        ON [dbo].[OfferTransaction] ([OfferCode]);

    CREATE INDEX [IX_OfferTransaction_ProviderOfferId]
        ON [dbo].[OfferTransaction] ([ProviderOfferId]);

    CREATE INDEX [IX_OfferTransaction_UserId]
        ON [dbo].[OfferTransaction] ([UserId]);

    CREATE INDEX [IX_OfferTransaction_Status]
        ON [dbo].[OfferTransaction] ([Status]);

    CREATE INDEX [IX_OfferTransaction_ProviderType_ProviderId]
        ON [dbo].[OfferTransaction] ([ProviderType], [ProviderId]);

    CREATE INDEX [IX_OfferTransaction_CodeExpiresAt]
        ON [dbo].[OfferTransaction] ([CodeExpiresAt]);

    CREATE INDEX [IX_OfferTransaction_CompletedAt]
        ON [dbo].[OfferTransaction] ([CompletedAt]);

    PRINT 'Created table: OfferTransaction';
END
ELSE
    PRINT 'Table already exists: OfferTransaction';
GO

-- 4. ProviderSettlement
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ProviderSettlement')
BEGIN
    CREATE TABLE [dbo].[ProviderSettlement] (
        [Id]                        INT IDENTITY(1,1)   NOT NULL,
        [ProviderType]              NVARCHAR(50)        NOT NULL,
        [ProviderId]                INT                 NOT NULL,
        [ProviderOwnerId]           INT                 NOT NULL,
        [PeriodYear]                INT                 NOT NULL,
        [PeriodMonth]               INT                 NOT NULL,
        [TotalTransactions]         INT                 NOT NULL DEFAULT 0,
        [TotalTransactionAmount]    DECIMAL(18,3)       NOT NULL DEFAULT 0,
        [TotalCommissionAmount]     DECIMAL(18,3)       NOT NULL DEFAULT 0,
        [TotalPointsAwarded]        INT                 NOT NULL DEFAULT 0,
        [SettlementStatus]          INT                 NOT NULL DEFAULT 1,
        [InvoicedAt]                DATETIME            NULL,
        [PaidAt]                    DATETIME            NULL,
        [PaidAmount]                DECIMAL(18,3)       NULL,
        [AdminNote]                 NVARCHAR(1000)      NULL,
        [CreatedAt]                 DATETIME            NOT NULL DEFAULT GETDATE(),
        [CreatedBy]                 INT                 NULL,
        [ModifiedAt]                DATETIME            NULL,
        [ModifiedBy]                INT                 NULL,
        [IsDeleted]                 BIT                 NOT NULL DEFAULT 0,
        CONSTRAINT [PK_ProviderSettlement] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [FK_ProviderSettlement_ProviderOwner] FOREIGN KEY ([ProviderOwnerId])
            REFERENCES [dbo].[UserAccount]([Id])
    );

    CREATE UNIQUE INDEX [IX_ProviderSettlement_Provider_Period]
        ON [dbo].[ProviderSettlement] ([ProviderType], [ProviderId], [PeriodYear], [PeriodMonth]);

    CREATE INDEX [IX_ProviderSettlement_SettlementStatus]
        ON [dbo].[ProviderSettlement] ([SettlementStatus]);

    CREATE INDEX [IX_ProviderSettlement_ProviderOwnerId]
        ON [dbo].[ProviderSettlement] ([ProviderOwnerId]);

    CREATE INDEX [IX_ProviderSettlement_PeriodYear_PeriodMonth]
        ON [dbo].[ProviderSettlement] ([PeriodYear], [PeriodMonth]);

    PRINT 'Created table: ProviderSettlement';
END
ELSE
    PRINT 'Table already exists: ProviderSettlement';
GO

-- =====================================================
-- Seed Data: Default Points Conversion Rate
-- =====================================================
IF NOT EXISTS (SELECT 1 FROM [dbo].[PointsConversionRate] WHERE [IsDefault] = 1)
BEGIN
    SET IDENTITY_INSERT [dbo].[PointsConversionRate] ON;

    INSERT INTO [dbo].[PointsConversionRate]
        ([Id], [Name], [CurrencyCode], [PointsPerUnit], [IsDefault], [IsActive], [CreatedAt], [IsDeleted])
    VALUES
        (1, N'Default Rate', N'KWD', 10.0, 1, 1, GETDATE(), 0);

    SET IDENTITY_INSERT [dbo].[PointsConversionRate] OFF;

    PRINT 'Seeded default PointsConversionRate: 1 KWD = 10 points';
END
ELSE
    PRINT 'Default PointsConversionRate already exists';
GO

PRINT '=== Phase 0.5 SQL script completed ===';
GO
