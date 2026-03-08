-- =============================================
-- Phase 2: Partner Transactions System
-- Cable EV Charging Station Management
-- =============================================

-- 1. PartnerAgreement table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PartnerAgreement')
BEGIN
    CREATE TABLE [dbo].[PartnerAgreement] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [ProviderType] NVARCHAR(50) NOT NULL,
        [ProviderId] INT NOT NULL,
        [CommissionPercentage] FLOAT NOT NULL,
        [PointsRewardPercentage] FLOAT NOT NULL,
        [PointsConversionRateId] INT NULL,
        [CodeExpiryMinutes] INT NOT NULL DEFAULT 30,
        [IsActive] BIT NOT NULL DEFAULT 1,
        [Note] NVARCHAR(500) NULL,
        [IsDeleted] BIT NOT NULL DEFAULT 0,
        [CreatedAt] DATETIME NULL,
        [CreatedBy] INT NULL,
        [ModifiedAt] DATETIME NULL,
        [ModifiedBy] INT NULL,
        CONSTRAINT [PK_PartnerAgreement] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PartnerAgreement_PointsConversionRate] FOREIGN KEY ([PointsConversionRateId])
            REFERENCES [dbo].[PointsConversionRate]([Id])
    );

    CREATE INDEX [IX_PartnerAgreement_ProviderType_ProviderId] ON [dbo].[PartnerAgreement] ([ProviderType], [ProviderId]);
    CREATE INDEX [IX_PartnerAgreement_IsActive] ON [dbo].[PartnerAgreement] ([IsActive]);
    CREATE INDEX [IX_PartnerAgreement_IsDeleted] ON [dbo].[PartnerAgreement] ([IsDeleted]);

    PRINT 'Created PartnerAgreement table';
END
GO

-- 2. PartnerTransaction table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PartnerTransaction')
BEGIN
    CREATE TABLE [dbo].[PartnerTransaction] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [PartnerAgreementId] INT NOT NULL,
        [UserId] INT NOT NULL,
        [TransactionCode] NVARCHAR(20) NOT NULL,
        [Status] INT NOT NULL,
        [ProviderType] NVARCHAR(50) NOT NULL,
        [ProviderId] INT NOT NULL,
        [TransactionAmount] DECIMAL(18,3) NULL,
        [CurrencyCode] NVARCHAR(10) NULL,
        [CommissionPercentage] FLOAT NOT NULL,
        [CommissionAmount] DECIMAL(18,3) NULL,
        [PointsRewardPercentage] FLOAT NOT NULL,
        [PointsConversionRate] FLOAT NOT NULL,
        [PointsEligibleAmount] DECIMAL(18,3) NULL,
        [PointsAwarded] INT NULL,
        [ConfirmedByUserId] INT NULL,
        [CodeExpiresAt] DATETIME NOT NULL,
        [CompletedAt] DATETIME NULL,
        [IsDeleted] BIT NOT NULL DEFAULT 0,
        [CreatedAt] DATETIME NULL,
        [CreatedBy] INT NULL,
        [ModifiedAt] DATETIME NULL,
        [ModifiedBy] INT NULL,
        CONSTRAINT [PK_PartnerTransaction] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PartnerTransaction_PartnerAgreement] FOREIGN KEY ([PartnerAgreementId])
            REFERENCES [dbo].[PartnerAgreement]([Id]),
        CONSTRAINT [FK_PartnerTransaction_User] FOREIGN KEY ([UserId])
            REFERENCES [dbo].[UserAccount]([Id]),
        CONSTRAINT [FK_PartnerTransaction_ConfirmedByUser] FOREIGN KEY ([ConfirmedByUserId])
            REFERENCES [dbo].[UserAccount]([Id])
    );

    CREATE UNIQUE INDEX [IX_PartnerTransaction_TransactionCode_Unique] ON [dbo].[PartnerTransaction] ([TransactionCode]);
    CREATE INDEX [IX_PartnerTransaction_PartnerAgreementId] ON [dbo].[PartnerTransaction] ([PartnerAgreementId]);
    CREATE INDEX [IX_PartnerTransaction_UserId] ON [dbo].[PartnerTransaction] ([UserId]);
    CREATE INDEX [IX_PartnerTransaction_Status] ON [dbo].[PartnerTransaction] ([Status]);
    CREATE INDEX [IX_PartnerTransaction_ProviderType_ProviderId] ON [dbo].[PartnerTransaction] ([ProviderType], [ProviderId]);
    CREATE INDEX [IX_PartnerTransaction_CodeExpiresAt] ON [dbo].[PartnerTransaction] ([CodeExpiresAt]);
    CREATE INDEX [IX_PartnerTransaction_CompletedAt] ON [dbo].[PartnerTransaction] ([CompletedAt]);

    PRINT 'Created PartnerTransaction table';
END
GO

PRINT 'Phase 2: Partner Transactions System - Complete';
GO
