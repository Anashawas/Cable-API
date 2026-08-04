-- =============================================
-- Premium Phase 1: Premium station payment & expiry dates
-- Cable EV Charging Station Management
-- =============================================
-- 1. Adds PremiumPaymentDate / PremiumExpiresAt to ChargingPoint
--    (denormalized latest values for fast reads)
-- 2. Creates StationPremiumSubscription — one row per payment/renewal
--    (immutable payment history)
--
-- Premium station = ChargingPoint.StationTypeId = 2 (StationType 'Premium').
-- NOTE: No auto-expiry job yet — dates are informational for now.
--
-- >>> RUN AGAINST THE DEV DATABASE ONLY — NOT PRODUCTION <<<
-- Idempotent: safe to re-run.
-- =============================================

-- 1. Denormalized premium dates on ChargingPoint
IF NOT EXISTS (SELECT * FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.ChargingPoint') AND name = 'PremiumPaymentDate')
BEGIN
    ALTER TABLE [dbo].[ChargingPoint] ADD [PremiumPaymentDate] DATETIME NULL;
    PRINT 'Added column: ChargingPoint.PremiumPaymentDate';
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.ChargingPoint') AND name = 'PremiumExpiresAt')
BEGIN
    ALTER TABLE [dbo].[ChargingPoint] ADD [PremiumExpiresAt] DATETIME NULL;
    PRINT 'Added column: ChargingPoint.PremiumExpiresAt';
END
GO

-- 2. StationPremiumSubscription — payment history
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'StationPremiumSubscription')
BEGIN
    CREATE TABLE [dbo].[StationPremiumSubscription] (
        [Id]              INT IDENTITY(1,1) NOT NULL,
        [ChargingPointId] INT               NOT NULL,
        [PaymentDate]     DATETIME          NOT NULL,
        [ExpiresAt]       DATETIME          NOT NULL,
        [Amount]          DECIMAL(18,3)     NULL,
        [Note]            NVARCHAR(500)     NULL,
        [CreatedAt]       DATETIME          NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy]       INT               NULL,
        [ModifiedAt]      DATETIME          NULL,
        [ModifiedBy]      INT               NULL,
        [IsDeleted]       BIT               NOT NULL DEFAULT 0,
        CONSTRAINT [PK_StationPremiumSubscription] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_StationPremiumSubscription_ChargingPoint]
            FOREIGN KEY ([ChargingPointId]) REFERENCES [dbo].[ChargingPoint] ([Id])
    );

    CREATE NONCLUSTERED INDEX [IX_StationPremiumSubscription_ChargingPoint_PaymentDate]
        ON [dbo].[StationPremiumSubscription] ([ChargingPointId], [PaymentDate]);

    PRINT 'Created table: StationPremiumSubscription';
END
GO
