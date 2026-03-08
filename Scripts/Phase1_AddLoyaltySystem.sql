-- =============================================
-- Phase 1: Loyalty System
-- Cable EV Charging Station Management
-- =============================================
-- This script creates 8 tables for the loyalty system
-- and seeds LoyaltyPointAction (12 actions) + LoyaltyTier (4 tiers)
-- =============================================

-- 1. LoyaltyPointAction — Defines what actions earn points
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'LoyaltyPointAction')
BEGIN
    CREATE TABLE [dbo].[LoyaltyPointAction] (
        [Id]              INT IDENTITY(1,1) NOT NULL,
        [ActionCode]      NVARCHAR(100)     NOT NULL,
        [Name]            NVARCHAR(255)     NOT NULL,
        [Description]     NVARCHAR(500)     NULL,
        [Points]          INT               NOT NULL,
        [MaxPerDay]       INT               NULL,
        [MaxPerLifetime]  INT               NULL,
        [IsActive]        BIT               NOT NULL DEFAULT 1,
        [CreatedAt]       DATETIME          NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy]       INT               NULL,
        [ModifiedAt]      DATETIME          NULL,
        [ModifiedBy]      INT               NULL,
        [IsDeleted]       BIT               NOT NULL DEFAULT 0,
        CONSTRAINT [PK_LoyaltyPointAction] PRIMARY KEY CLUSTERED ([Id] ASC)
    );

    CREATE UNIQUE NONCLUSTERED INDEX [IX_LoyaltyPointAction_ActionCode_Unique]
        ON [dbo].[LoyaltyPointAction] ([ActionCode]);

    PRINT 'Created table: LoyaltyPointAction';
END
GO

-- 2. LoyaltyTier — Static tier definitions (no audit columns)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'LoyaltyTier')
BEGIN
    CREATE TABLE [dbo].[LoyaltyTier] (
        [Id]          INT IDENTITY(1,1) NOT NULL,
        [Name]        NVARCHAR(100)     NOT NULL,
        [MinPoints]   INT               NOT NULL,
        [Multiplier]  FLOAT             NOT NULL,
        [BonusPoints] INT               NOT NULL DEFAULT 0,
        [IconUrl]     NVARCHAR(500)     NULL,
        [IsActive]    BIT               NOT NULL DEFAULT 1,
        CONSTRAINT [PK_LoyaltyTier] PRIMARY KEY CLUSTERED ([Id] ASC)
    );

    PRINT 'Created table: LoyaltyTier';
END
GO

-- 3. LoyaltySeason — Season definitions
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'LoyaltySeason')
BEGIN
    CREATE TABLE [dbo].[LoyaltySeason] (
        [Id]          INT IDENTITY(1,1) NOT NULL,
        [Name]        NVARCHAR(255)     NOT NULL,
        [Description] NVARCHAR(500)     NULL,
        [StartDate]   DATETIME          NOT NULL,
        [EndDate]     DATETIME          NOT NULL,
        [IsActive]    BIT               NOT NULL DEFAULT 0,
        [CreatedAt]   DATETIME          NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy]   INT               NULL,
        [ModifiedAt]  DATETIME          NULL,
        [ModifiedBy]  INT               NULL,
        [IsDeleted]   BIT               NOT NULL DEFAULT 0,
        CONSTRAINT [PK_LoyaltySeason] PRIMARY KEY CLUSTERED ([Id] ASC)
    );

    CREATE NONCLUSTERED INDEX [IX_LoyaltySeason_IsActive]
        ON [dbo].[LoyaltySeason] ([IsActive]);

    CREATE NONCLUSTERED INDEX [IX_LoyaltySeason_StartDate_EndDate]
        ON [dbo].[LoyaltySeason] ([StartDate], [EndDate]);

    PRINT 'Created table: LoyaltySeason';
END
GO

-- 4. UserLoyaltyAccount — Permanent wallet (one per user)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserLoyaltyAccount')
BEGIN
    CREATE TABLE [dbo].[UserLoyaltyAccount] (
        [Id]                  INT IDENTITY(1,1) NOT NULL,
        [UserId]              INT               NOT NULL,
        [TotalPointsEarned]   INT               NOT NULL DEFAULT 0,
        [TotalPointsRedeemed] INT               NOT NULL DEFAULT 0,
        [CurrentBalance]      INT               NOT NULL DEFAULT 0,
        [CreatedAt]           DATETIME          NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy]           INT               NULL,
        [ModifiedAt]          DATETIME          NULL,
        [ModifiedBy]          INT               NULL,
        [IsDeleted]           BIT               NOT NULL DEFAULT 0,
        CONSTRAINT [PK_UserLoyaltyAccount] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_UserLoyaltyAccount_UserAccount] FOREIGN KEY ([UserId])
            REFERENCES [dbo].[UserAccount] ([Id])
    );

    CREATE UNIQUE NONCLUSTERED INDEX [IX_UserLoyaltyAccount_UserId_Unique]
        ON [dbo].[UserLoyaltyAccount] ([UserId]);

    CREATE NONCLUSTERED INDEX [IX_UserLoyaltyAccount_CurrentBalance]
        ON [dbo].[UserLoyaltyAccount] ([CurrentBalance]);

    PRINT 'Created table: UserLoyaltyAccount';
END
GO

-- 5. UserSeasonProgress — User's progress within a season
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserSeasonProgress')
BEGIN
    CREATE TABLE [dbo].[UserSeasonProgress] (
        [Id]                 INT IDENTITY(1,1) NOT NULL,
        [UserId]             INT               NOT NULL,
        [LoyaltySeasonId]    INT               NOT NULL,
        [SeasonPointsEarned] INT               NOT NULL DEFAULT 0,
        [TierLevel]          INT               NOT NULL DEFAULT 1,
        [CreatedAt]          DATETIME          NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy]          INT               NULL,
        [ModifiedAt]         DATETIME          NULL,
        [ModifiedBy]         INT               NULL,
        [IsDeleted]          BIT               NOT NULL DEFAULT 0,
        CONSTRAINT [PK_UserSeasonProgress] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_UserSeasonProgress_UserAccount] FOREIGN KEY ([UserId])
            REFERENCES [dbo].[UserAccount] ([Id]),
        CONSTRAINT [FK_UserSeasonProgress_LoyaltySeason] FOREIGN KEY ([LoyaltySeasonId])
            REFERENCES [dbo].[LoyaltySeason] ([Id]),
        CONSTRAINT [FK_UserSeasonProgress_LoyaltyTier] FOREIGN KEY ([TierLevel])
            REFERENCES [dbo].[LoyaltyTier] ([Id])
    );

    CREATE UNIQUE NONCLUSTERED INDEX [IX_UserSeasonProgress_UserId_SeasonId_Unique]
        ON [dbo].[UserSeasonProgress] ([UserId], [LoyaltySeasonId]);

    CREATE NONCLUSTERED INDEX [IX_UserSeasonProgress_LoyaltySeasonId]
        ON [dbo].[UserSeasonProgress] ([LoyaltySeasonId]);

    CREATE NONCLUSTERED INDEX [IX_UserSeasonProgress_SeasonPointsEarned]
        ON [dbo].[UserSeasonProgress] ([SeasonPointsEarned]);

    PRINT 'Created table: UserSeasonProgress';
END
GO

-- 6. LoyaltyPointTransaction — Full audit trail
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'LoyaltyPointTransaction')
BEGIN
    CREATE TABLE [dbo].[LoyaltyPointTransaction] (
        [Id]                     INT IDENTITY(1,1) NOT NULL,
        [UserLoyaltyAccountId]   INT               NOT NULL,
        [LoyaltyPointActionId]   INT               NULL,
        [LoyaltySeasonId]        INT               NULL,
        [TransactionType]        INT               NOT NULL,
        [Points]                 INT               NOT NULL,
        [BalanceAfter]           INT               NOT NULL,
        [ReferenceType]          NVARCHAR(100)     NULL,
        [ReferenceId]            INT               NULL,
        [Note]                   NVARCHAR(500)     NULL,
        [ExpiresAt]              DATETIME          NULL,
        [CreatedAt]              DATETIME          NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy]              INT               NULL,
        [ModifiedAt]             DATETIME          NULL,
        [ModifiedBy]             INT               NULL,
        [IsDeleted]              BIT               NOT NULL DEFAULT 0,
        CONSTRAINT [PK_LoyaltyPointTransaction] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_LoyaltyPointTransaction_UserLoyaltyAccount] FOREIGN KEY ([UserLoyaltyAccountId])
            REFERENCES [dbo].[UserLoyaltyAccount] ([Id]),
        CONSTRAINT [FK_LoyaltyPointTransaction_LoyaltyPointAction] FOREIGN KEY ([LoyaltyPointActionId])
            REFERENCES [dbo].[LoyaltyPointAction] ([Id]),
        CONSTRAINT [FK_LoyaltyPointTransaction_LoyaltySeason] FOREIGN KEY ([LoyaltySeasonId])
            REFERENCES [dbo].[LoyaltySeason] ([Id])
    );

    CREATE NONCLUSTERED INDEX [IX_LoyaltyPointTransaction_UserLoyaltyAccountId]
        ON [dbo].[LoyaltyPointTransaction] ([UserLoyaltyAccountId]);

    CREATE NONCLUSTERED INDEX [IX_LoyaltyPointTransaction_LoyaltySeasonId]
        ON [dbo].[LoyaltyPointTransaction] ([LoyaltySeasonId]);

    CREATE NONCLUSTERED INDEX [IX_LoyaltyPointTransaction_CreatedAt]
        ON [dbo].[LoyaltyPointTransaction] ([CreatedAt]);

    CREATE NONCLUSTERED INDEX [IX_LoyaltyPointTransaction_TransactionType]
        ON [dbo].[LoyaltyPointTransaction] ([TransactionType]);

    CREATE NONCLUSTERED INDEX [IX_LoyaltyPointTransaction_ReferenceType_ReferenceId]
        ON [dbo].[LoyaltyPointTransaction] ([ReferenceType], [ReferenceId]);

    PRINT 'Created table: LoyaltyPointTransaction';
END
GO

-- 7. LoyaltyReward — Provider-linked rewards
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'LoyaltyReward')
BEGIN
    CREATE TABLE [dbo].[LoyaltyReward] (
        [Id]                 INT IDENTITY(1,1) NOT NULL,
        [Name]               NVARCHAR(255)     NOT NULL,
        [Description]        NVARCHAR(1000)    NULL,
        [PointsCost]         INT               NOT NULL,
        [RewardType]         INT               NOT NULL,
        [RewardValue]        NVARCHAR(500)     NULL,
        [ProviderType]       NVARCHAR(50)      NULL,
        [ProviderId]         INT               NULL,
        [ServiceCategoryId]  INT               NULL,
        [MaxRedemptions]     INT               NULL,
        [CurrentRedemptions] INT               NOT NULL DEFAULT 0,
        [ImageUrl]           NVARCHAR(500)     NULL,
        [IsActive]           BIT               NOT NULL DEFAULT 1,
        [ValidFrom]          DATETIME          NOT NULL,
        [ValidTo]            DATETIME          NULL,
        [CreatedAt]          DATETIME          NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy]          INT               NULL,
        [ModifiedAt]         DATETIME          NULL,
        [ModifiedBy]         INT               NULL,
        [IsDeleted]          BIT               NOT NULL DEFAULT 0,
        CONSTRAINT [PK_LoyaltyReward] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_LoyaltyReward_ServiceCategory] FOREIGN KEY ([ServiceCategoryId])
            REFERENCES [dbo].[ServiceCategory] ([Id])
    );

    CREATE NONCLUSTERED INDEX [IX_LoyaltyReward_IsActive_ValidFrom_ValidTo]
        ON [dbo].[LoyaltyReward] ([IsActive], [ValidFrom], [ValidTo]);

    CREATE NONCLUSTERED INDEX [IX_LoyaltyReward_RewardType]
        ON [dbo].[LoyaltyReward] ([RewardType]);

    CREATE NONCLUSTERED INDEX [IX_LoyaltyReward_ProviderType_ProviderId]
        ON [dbo].[LoyaltyReward] ([ProviderType], [ProviderId]);

    CREATE NONCLUSTERED INDEX [IX_LoyaltyReward_ServiceCategoryId]
        ON [dbo].[LoyaltyReward] ([ServiceCategoryId]);

    PRINT 'Created table: LoyaltyReward';
END
GO

-- 8. UserRewardRedemption — Tracks each redemption
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserRewardRedemption')
BEGIN
    CREATE TABLE [dbo].[UserRewardRedemption] (
        [Id]                          INT IDENTITY(1,1) NOT NULL,
        [UserId]                      INT               NOT NULL,
        [LoyaltyRewardId]             INT               NOT NULL,
        [LoyaltyPointTransactionId]   INT               NOT NULL,
        [PointsSpent]                 INT               NOT NULL,
        [Status]                      INT               NOT NULL,
        [RedemptionCode]              NVARCHAR(50)      NULL,
        [ProviderType]                NVARCHAR(50)      NULL,
        [ProviderId]                  INT               NULL,
        [RedeemedAt]                  DATETIME          NOT NULL,
        [FulfilledAt]                 DATETIME          NULL,
        [CreatedAt]                   DATETIME          NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy]                   INT               NULL,
        [ModifiedAt]                  DATETIME          NULL,
        [ModifiedBy]                  INT               NULL,
        [IsDeleted]                   BIT               NOT NULL DEFAULT 0,
        CONSTRAINT [PK_UserRewardRedemption] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_UserRewardRedemption_UserAccount] FOREIGN KEY ([UserId])
            REFERENCES [dbo].[UserAccount] ([Id]),
        CONSTRAINT [FK_UserRewardRedemption_LoyaltyReward] FOREIGN KEY ([LoyaltyRewardId])
            REFERENCES [dbo].[LoyaltyReward] ([Id]),
        CONSTRAINT [FK_UserRewardRedemption_LoyaltyPointTransaction] FOREIGN KEY ([LoyaltyPointTransactionId])
            REFERENCES [dbo].[LoyaltyPointTransaction] ([Id])
    );

    CREATE NONCLUSTERED INDEX [IX_UserRewardRedemption_UserId]
        ON [dbo].[UserRewardRedemption] ([UserId]);

    CREATE NONCLUSTERED INDEX [IX_UserRewardRedemption_LoyaltyRewardId]
        ON [dbo].[UserRewardRedemption] ([LoyaltyRewardId]);

    CREATE NONCLUSTERED INDEX [IX_UserRewardRedemption_Status]
        ON [dbo].[UserRewardRedemption] ([Status]);

    CREATE NONCLUSTERED INDEX [IX_UserRewardRedemption_RedemptionCode]
        ON [dbo].[UserRewardRedemption] ([RedemptionCode]);

    CREATE NONCLUSTERED INDEX [IX_UserRewardRedemption_ProviderType_ProviderId]
        ON [dbo].[UserRewardRedemption] ([ProviderType], [ProviderId]);

    PRINT 'Created table: UserRewardRedemption';
END
GO

-- =============================================
-- SEED DATA
-- =============================================

-- Seed LoyaltyTier (4 tiers)
IF NOT EXISTS (SELECT 1 FROM [dbo].[LoyaltyTier] WHERE [Name] = 'Bronze')
BEGIN
    SET IDENTITY_INSERT [dbo].[LoyaltyTier] ON;

    INSERT INTO [dbo].[LoyaltyTier] ([Id], [Name], [MinPoints], [Multiplier], [BonusPoints], [IconUrl], [IsActive])
    VALUES
        (1, 'Bronze',   0,    1.0, 0,   NULL, 1),
        (2, 'Silver',   500,  1.5, 50,  NULL, 1),
        (3, 'Gold',     2000, 2.0, 150, NULL, 1),
        (4, 'Platinum', 5000, 3.0, 300, NULL, 1);

    SET IDENTITY_INSERT [dbo].[LoyaltyTier] OFF;

    PRINT 'Seeded LoyaltyTier: Bronze, Silver, Gold, Platinum';
END
GO

-- Seed LoyaltyPointAction (12 actions)
IF NOT EXISTS (SELECT 1 FROM [dbo].[LoyaltyPointAction] WHERE [ActionCode] = 'RATE_STATION')
BEGIN
    INSERT INTO [dbo].[LoyaltyPointAction] ([ActionCode], [Name], [Description], [Points], [MaxPerDay], [MaxPerLifetime], [IsActive], [CreatedAt], [IsDeleted])
    VALUES
        ('RATE_STATION',        'Rate a charging station',     'Earn points by rating a charging station',    10,  5,    NULL, 1, GETUTCDATE(), 0),
        ('RATE_SERVICE',        'Rate a service provider',     'Earn points by rating a service provider',    10,  5,    NULL, 1, GETUTCDATE(), 0),
        ('SHARE_LINK',          'Share a station link',        'Earn points by sharing a station link',       15,  3,    NULL, 1, GETUTCDATE(), 0),
        ('SHARE_SERVICE',       'Share a service link',        'Earn points by sharing a service link',       15,  3,    NULL, 1, GETUTCDATE(), 0),
        ('ADD_FAVORITE',        'Add station to favorites',    'Earn points by adding a station to favorites', 5, 10,   NULL, 1, GETUTCDATE(), 0),
        ('ADD_FAVORITE_SERVICE','Add service to favorites',    'Earn points by adding a service to favorites', 5, 10,   NULL, 1, GETUTCDATE(), 0),
        ('USE_OFFER',           'Use an offer at a provider',  'Points dynamically calculated from transaction amount', 0, NULL, NULL, 1, GETUTCDATE(), 0),
        ('COMPLETE_PROFILE',    'Complete your profile',       'One-time bonus for completing your profile',  50,  NULL, 1,    1, GETUTCDATE(), 0),
        ('VERIFY_PHONE',        'Verify phone number',         'One-time bonus for verifying your phone',     30,  NULL, 1,    1, GETUTCDATE(), 0),
        ('DAILY_LOGIN',         'Daily app login',             'Daily login bonus',                           3,   1,    NULL, 1, GETUTCDATE(), 0),
        ('REFER_FRIEND',        'Refer a new user',            'Earn points by referring a friend',           100, NULL, NULL, 1, GETUTCDATE(), 0),
        ('FIRST_RATE',          'First-ever rating',           'One-time bonus for your first rating',        25,  NULL, 1,    1, GETUTCDATE(), 0);

    PRINT 'Seeded LoyaltyPointAction: 12 actions';
END
GO

PRINT '=============================================';
PRINT 'Phase 1: Loyalty System - Script Complete';
PRINT '8 tables created, 4 tiers seeded, 12 actions seeded';
PRINT '=============================================';
GO
