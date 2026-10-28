SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/* =============================================================================
   Sprint 5 — Cable Gifts + promo codes (Docs/Sprint5-GiftsAndPromos.md)

   1. CableGift, GiftOrder                       (gifts bought with points)
   2. PromoCampaign, PromoCode, UserPromo,
      PromoUsage, PromoClaimAttempt              (promo codes)
   3. ProviderSettlement.PromoCableShareAmount   ("discounts paid by Cable")
   4. Seeds: LoyaltyPointAction GIFT_PURCHASE / GIFT_REFUND,
             NotificationType cable_gift_order / promo_assigned / promo_expiring

   Additive and idempotent: every step is guarded, safe to run more than once.
   Nothing existing changes: the new settlement column defaults to 0, so every
   NetBalance stays what it is. Dates are UTC (DATETIME2), like the API expects.
   ============================================================================= */

SET NOCOUNT ON;
PRINT '=== Sprint 5: Cable Gifts + promo codes ===';
GO

/* ------------------------------------------------------------------ 1. CableGift */
IF OBJECT_ID('dbo.CableGift', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CableGift
    (
        Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CableGift PRIMARY KEY,
        NameAr          NVARCHAR(150)  NOT NULL,
        NameEn          NVARCHAR(150)  NOT NULL,
        DescriptionAr   NVARCHAR(2000) NULL,
        DescriptionEn   NVARCHAR(2000) NULL,
        HowToReceiveAr  NVARCHAR(1000) NULL,
        HowToReceiveEn  NVARCHAR(1000) NULL,
        GiftType        INT            NOT NULL,                 -- 1 Physical | 2 ChargeCredit | 3 Voucher | 4 Other
        PointsCost      INT            NOT NULL CONSTRAINT CK_CableGift_PointsCost CHECK (PointsCost > 0),
        ImageUrl        NVARCHAR(500)  NULL,
        Stock           INT            NULL,                     -- NULL = unlimited
        OrdersCount     INT            NOT NULL CONSTRAINT DF_CableGift_OrdersCount DEFAULT 0,   -- pending + fulfilled
        PerUserLimit    INT            NULL,                     -- NULL = unlimited
        StartsAt        DATETIME2      NULL,
        EndsAt          DATETIME2      NULL,
        Cities          NVARCHAR(500)  NULL,                     -- CSV of canonical cities; NULL = every city
        SortOrder       INT            NOT NULL CONSTRAINT DF_CableGift_SortOrder DEFAULT 0,
        IsActive        BIT            NOT NULL CONSTRAINT DF_CableGift_IsActive DEFAULT 1,
        CreatedBy       INT            NULL,
        CreatedAt       DATETIME2      NOT NULL CONSTRAINT DF_CableGift_CreatedAt DEFAULT SYSUTCDATETIME(),
        ModifiedBy      INT            NULL,
        ModifiedAt      DATETIME2      NULL,
        IsDeleted       BIT            NOT NULL CONSTRAINT DF_CableGift_IsDeleted DEFAULT 0
    );
    CREATE INDEX IX_CableGift_Active_Sort ON dbo.CableGift (IsActive, SortOrder);
    PRINT '  [CREATED] dbo.CableGift';
END
ELSE PRINT '  [SKIPPED] dbo.CableGift exists';
GO

/* ------------------------------------------------------------------ 1b. GiftOrder */
IF OBJECT_ID('dbo.GiftOrder', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.GiftOrder
    (
        Id                   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_GiftOrder PRIMARY KEY,
        OrderCode            NVARCHAR(20)   NOT NULL,            -- GFT- + 6 chars without 0 O 1 I L
        CableGiftId          INT            NOT NULL,
        UserId               INT            NOT NULL,
        PointsSpent          INT            NOT NULL,
        Status               INT            NOT NULL,            -- 1 Pending | 2 Fulfilled | 3 Cancelled
        ContactPhone         NVARCHAR(30)   NULL,
        Note                 NVARCHAR(500)  NULL,
        FulfillmentNote      NVARCHAR(1000) NULL,
        FulfilledAt          DATETIME2      NULL,
        FulfilledByUserId    INT            NULL,
        CancelReason         NVARCHAR(500)  NULL,
        CancelledAt          DATETIME2      NULL,
        CancelledByUserId    INT            NULL,
        IdempotencyKey       NVARCHAR(64)   NULL,
        PointsTransactionId  INT            NULL,
        RefundTransactionId  INT            NULL,
        CreatedBy            INT            NULL,
        CreatedAt            DATETIME2      NOT NULL CONSTRAINT DF_GiftOrder_CreatedAt DEFAULT SYSUTCDATETIME(),
        ModifiedBy           INT            NULL,
        ModifiedAt           DATETIME2      NULL,
        IsDeleted            BIT            NOT NULL CONSTRAINT DF_GiftOrder_IsDeleted DEFAULT 0,
        CONSTRAINT FK_GiftOrder_CableGift_CableGiftId FOREIGN KEY (CableGiftId) REFERENCES dbo.CableGift (Id),
        CONSTRAINT FK_GiftOrder_UserAccount_UserId   FOREIGN KEY (UserId)      REFERENCES dbo.UserAccount (Id)
    );
    CREATE UNIQUE INDEX UX_GiftOrder_OrderCode ON dbo.GiftOrder (OrderCode);
    -- A repeated Idempotency-Key of the same user can never create a second order.
    CREATE UNIQUE INDEX UX_GiftOrder_User_IdempotencyKey ON dbo.GiftOrder (UserId, IdempotencyKey) WHERE IdempotencyKey IS NOT NULL;
    CREATE INDEX IX_GiftOrder_Gift_User_Status ON dbo.GiftOrder (CableGiftId, UserId, Status);
    CREATE INDEX IX_GiftOrder_Status_CreatedAt ON dbo.GiftOrder (Status, CreatedAt);
    PRINT '  [CREATED] dbo.GiftOrder';
END
ELSE PRINT '  [SKIPPED] dbo.GiftOrder exists';
GO

/* ------------------------------------------------------------------ 2. PromoCampaign */
IF OBJECT_ID('dbo.PromoCampaign', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PromoCampaign
    (
        Id                     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PromoCampaign PRIMARY KEY,
        TitleAr                NVARCHAR(150)  NOT NULL,
        TitleEn                NVARCHAR(150)  NOT NULL,
        DescriptionAr          NVARCHAR(1000) NULL,
        DescriptionEn          NVARCHAR(1000) NULL,
        ScopeLabelAr           NVARCHAR(200)  NULL,
        ScopeLabelEn           NVARCHAR(200)  NULL,
        CableSharePercent      DECIMAL(5,2)   NOT NULL,          -- 0–100: Cable's part of a discount
        CableBudgetJod         DECIMAL(18,3)  NULL,              -- NULL = no cap
        CableSpentJod          DECIMAL(18,3)  NOT NULL CONSTRAINT DF_PromoCampaign_CableSpentJod DEFAULT 0,
        StationsScope          INT            NOT NULL,          -- 0 None | 1 All | 2 Selected
        StationIds             NVARCHAR(4000) NULL,              -- CSV of ChargingPoint ids
        ServiceProvidersScope  INT            NOT NULL,          -- 0 None | 1 All | 2 Selected
        ServiceProviderIds     NVARCHAR(4000) NULL,              -- CSV of ServiceProvider ids
        EffectType             INT            NOT NULL,          -- 1 DiscountPercent | 2 DiscountFixed | 3 PointsMultiplier | 4 BonusPoints
        EffectValue            DECIMAL(18,3)  NOT NULL,
        MaxDiscountJod         DECIMAL(18,3)  NULL,
        AppliesTo              NVARCHAR(100)  NOT NULL CONSTRAINT DF_PromoCampaign_AppliesTo DEFAULT 'PartnerScan',
        ServiceCategoryIds     NVARCHAR(500)  NULL,
        Cities                 NVARCHAR(500)  NULL,
        MinAmountJod           DECIMAL(18,3)  NULL,
        StartsAt               DATETIME2      NULL,
        EndsAt                 DATETIME2      NULL,
        ValidDaysAfterClaim    INT            NULL,
        TotalLimit             INT            NULL,
        PerUserLimit           INT            NOT NULL CONSTRAINT DF_PromoCampaign_PerUserLimit DEFAULT 1,
        NewUsersOnly           BIT            NOT NULL CONSTRAINT DF_PromoCampaign_NewUsersOnly DEFAULT 0,
        CodeMode               INT            NOT NULL,          -- 1 Public | 2 Unique
        PublicCode             NVARCHAR(40)   NULL,
        PublicCodeNormalized   NVARCHAR(40)   NULL,
        ClaimsCount            INT            NOT NULL CONSTRAINT DF_PromoCampaign_ClaimsCount DEFAULT 0,
        UsesCount              INT            NOT NULL CONSTRAINT DF_PromoCampaign_UsesCount DEFAULT 0,
        IsActive               BIT            NOT NULL CONSTRAINT DF_PromoCampaign_IsActive DEFAULT 1,
        DeactivatedReason      NVARCHAR(40)   NULL,              -- 'BudgetExhausted' when the server switched it off
        CreatedBy              INT            NULL,
        CreatedAt              DATETIME2      NOT NULL CONSTRAINT DF_PromoCampaign_CreatedAt DEFAULT SYSUTCDATETIME(),
        ModifiedBy             INT            NULL,
        ModifiedAt             DATETIME2      NULL,
        IsDeleted              BIT            NOT NULL CONSTRAINT DF_PromoCampaign_IsDeleted DEFAULT 0
    );
    CREATE UNIQUE INDEX UX_PromoCampaign_PublicCodeNormalized ON dbo.PromoCampaign (PublicCodeNormalized)
        WHERE PublicCodeNormalized IS NOT NULL AND IsDeleted = 0;
    PRINT '  [CREATED] dbo.PromoCampaign';
END
ELSE PRINT '  [SKIPPED] dbo.PromoCampaign exists';
GO

/* ------------------------------------------------------------------ 2b. PromoCode */
IF OBJECT_ID('dbo.PromoCode', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PromoCode
    (
        Id               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PromoCode PRIMARY KEY,
        PromoCampaignId  INT              NOT NULL,
        Code             NVARCHAR(20)     NOT NULL,               -- PRM- + 8 chars without 0 O 1 I L
        NormalizedCode   NVARCHAR(20)     NOT NULL,               -- upper case, no dashes / spaces
        ClaimedByUserId  INT              NULL,
        ClaimedAt        DATETIME2        NULL,
        CreatedAt        DATETIME2        NOT NULL CONSTRAINT DF_PromoCode_CreatedAt DEFAULT SYSUTCDATETIME(),
        BatchId          UNIQUEIDENTIFIER NULL,
        CONSTRAINT FK_PromoCode_PromoCampaign_PromoCampaignId FOREIGN KEY (PromoCampaignId) REFERENCES dbo.PromoCampaign (Id)
    );
    CREATE UNIQUE INDEX UX_PromoCode_NormalizedCode ON dbo.PromoCode (NormalizedCode);
    CREATE INDEX IX_PromoCode_Campaign ON dbo.PromoCode (PromoCampaignId);
    PRINT '  [CREATED] dbo.PromoCode';
END
ELSE PRINT '  [SKIPPED] dbo.PromoCode exists';
GO

/* ------------------------------------------------------------------ 2c. UserPromo ("My codes") */
IF OBJECT_ID('dbo.UserPromo', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserPromo
    (
        Id                    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserPromo PRIMARY KEY,
        UserId                INT              NOT NULL,
        PromoCampaignId       INT              NOT NULL,
        PromoCodeId           INT              NULL,
        Source                INT              NOT NULL,          -- 1 Code | 2 Assigned
        ClaimedAt             DATETIME2        NOT NULL,
        ExpiresAt             DATETIME2        NULL,
        Status                INT              NOT NULL,          -- 1 Available | 2 Used | 3 Expired | 4 Revoked
        UsedAt                DATETIME2        NULL,
        RevokedAt             DATETIME2        NULL,
        RevokedByUserId       INT              NULL,
        RevokeReason          NVARCHAR(500)    NULL,
        ExpiryReminderSentAt  DATETIME2        NULL,
        AssignmentBatchId     UNIQUEIDENTIFIER NULL,
        CreatedBy             INT              NULL,
        CreatedAt             DATETIME2        NOT NULL CONSTRAINT DF_UserPromo_CreatedAt DEFAULT SYSUTCDATETIME(),
        ModifiedBy            INT              NULL,
        ModifiedAt            DATETIME2        NULL,
        IsDeleted             BIT              NOT NULL CONSTRAINT DF_UserPromo_IsDeleted DEFAULT 0,
        CONSTRAINT FK_UserPromo_PromoCampaign_PromoCampaignId FOREIGN KEY (PromoCampaignId) REFERENCES dbo.PromoCampaign (Id),
        CONSTRAINT FK_UserPromo_PromoCode_PromoCodeId         FOREIGN KEY (PromoCodeId)     REFERENCES dbo.PromoCode (Id),
        CONSTRAINT FK_UserPromo_UserAccount_UserId            FOREIGN KEY (UserId)          REFERENCES dbo.UserAccount (Id)
    );
    -- One AVAILABLE promo per user and campaign (also stops a double-tap claim).
    CREATE UNIQUE INDEX UX_UserPromo_User_Campaign_Available ON dbo.UserPromo (UserId, PromoCampaignId)
        WHERE Status = 1 AND IsDeleted = 0;
    CREATE INDEX IX_UserPromo_User_Status      ON dbo.UserPromo (UserId, Status);
    CREATE INDEX IX_UserPromo_Campaign         ON dbo.UserPromo (PromoCampaignId);
    CREATE INDEX IX_UserPromo_PromoCodeId      ON dbo.UserPromo (PromoCodeId);
    CREATE INDEX IX_UserPromo_AssignmentBatch  ON dbo.UserPromo (AssignmentBatchId);
    PRINT '  [CREATED] dbo.UserPromo';
END
ELSE PRINT '  [SKIPPED] dbo.UserPromo exists';
GO

/* ------------------------------------------------------------------ 2d. PromoUsage */
IF OBJECT_ID('dbo.PromoUsage', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PromoUsage
    (
        Id                     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PromoUsage PRIMARY KEY,
        UserPromoId            INT            NOT NULL,
        PromoCampaignId        INT            NOT NULL,
        UserId                 INT            NOT NULL,
        TransactionType        NVARCHAR(30)   NOT NULL,           -- 'PartnerScan'
        TransactionId          INT            NOT NULL,           -- PartnerTransaction.Id
        ProviderType           NVARCHAR(50)   NOT NULL,
        ProviderId             INT            NOT NULL,
        OriginalAmountJod      DECIMAL(18,3)  NOT NULL,
        DiscountJod            DECIMAL(18,3)  NOT NULL,
        FinalAmountJod         DECIMAL(18,3)  NOT NULL,
        CableShareJod          DECIMAL(18,3)  NOT NULL,           -- owed by Cable to the partner
        ProviderShareJod       DECIMAL(18,3)  NOT NULL,
        CommissionBeforeJod    DECIMAL(18,3)  NOT NULL,
        CommissionAfterJod     DECIMAL(18,3)  NOT NULL,
        BasePoints             INT            NOT NULL,
        BonusPoints            INT            NOT NULL,
        FinalPoints            INT            NOT NULL,
        SettledInSettlementId  INT            NULL,
        CreatedAt              DATETIME2      NOT NULL CONSTRAINT DF_PromoUsage_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_PromoUsage_PromoCampaign_PromoCampaignId FOREIGN KEY (PromoCampaignId) REFERENCES dbo.PromoCampaign (Id),
        CONSTRAINT FK_PromoUsage_UserPromo_UserPromoId         FOREIGN KEY (UserPromoId)     REFERENCES dbo.UserPromo (Id)
    );
    -- Single use: one promo backs one transaction, even under a race.
    CREATE UNIQUE INDEX UX_PromoUsage_UserPromo         ON dbo.PromoUsage (UserPromoId);
    CREATE INDEX IX_PromoUsage_Transaction              ON dbo.PromoUsage (TransactionType, TransactionId);
    CREATE INDEX IX_PromoUsage_Campaign                 ON dbo.PromoUsage (PromoCampaignId);
    CREATE INDEX IX_PromoUsage_Provider_CreatedAt       ON dbo.PromoUsage (ProviderType, ProviderId, CreatedAt);
    PRINT '  [CREATED] dbo.PromoUsage';
END
ELSE PRINT '  [SKIPPED] dbo.PromoUsage exists';
GO

/* ------------------------------------------------------------------ 2e. PromoClaimAttempt */
IF OBJECT_ID('dbo.PromoClaimAttempt', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PromoClaimAttempt
    (
        Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PromoClaimAttempt PRIMARY KEY,
        UserId       INT           NOT NULL,
        CodeTried    NVARCHAR(60)  NOT NULL,
        AttemptedAt  DATETIME2     NOT NULL
    );
    CREATE INDEX IX_PromoClaimAttempt_User_AttemptedAt ON dbo.PromoClaimAttempt (UserId, AttemptedAt);
    PRINT '  [CREATED] dbo.PromoClaimAttempt';
END
ELSE PRINT '  [SKIPPED] dbo.PromoClaimAttempt exists';
GO

/* ------------------------------------------------------------------ 3. settlement line */
IF COL_LENGTH('dbo.ProviderSettlement', 'PromoCableShareAmount') IS NULL
BEGIN
    ALTER TABLE dbo.ProviderSettlement
        ADD PromoCableShareAmount DECIMAL(18,3) NOT NULL
        CONSTRAINT DF_ProviderSettlement_PromoCableShareAmount DEFAULT 0;
    PRINT '  [ADDED]   dbo.ProviderSettlement.PromoCableShareAmount (NetBalance = offers + promo share - commission)';
END
ELSE PRINT '  [SKIPPED] dbo.ProviderSettlement.PromoCableShareAmount exists';
GO

/* ------------------------------------------------------------------ 4a. points history action types */
IF NOT EXISTS (SELECT 1 FROM dbo.LoyaltyPointAction WHERE ActionCode = 'GIFT_PURCHASE')
BEGIN
    INSERT INTO dbo.LoyaltyPointAction (ActionCode, Name, Description, Points, MaxPerDay, MaxPerLifetime, IsActive, CreatedAt, IsDeleted)
    VALUES ('GIFT_PURCHASE', N'Cable gift purchase', N'Points spent on a Cable gift (شراء هدية)', 0, NULL, NULL, 1, GETUTCDATE(), 0);
    PRINT '  [SEEDED]  LoyaltyPointAction GIFT_PURCHASE';
END
ELSE PRINT '  [SKIPPED] LoyaltyPointAction GIFT_PURCHASE exists';

IF NOT EXISTS (SELECT 1 FROM dbo.LoyaltyPointAction WHERE ActionCode = 'GIFT_REFUND')
BEGIN
    INSERT INTO dbo.LoyaltyPointAction (ActionCode, Name, Description, Points, MaxPerDay, MaxPerLifetime, IsActive, CreatedAt, IsDeleted)
    VALUES ('GIFT_REFUND', N'Cable gift refund', N'Points returned for a cancelled Cable gift (استرجاع هدية)', 0, NULL, NULL, 1, GETUTCDATE(), 0);
    PRINT '  [SEEDED]  LoyaltyPointAction GIFT_REFUND';
END
ELSE PRINT '  [SKIPPED] LoyaltyPointAction GIFT_REFUND exists';
GO

/* ------------------------------------------------------------------ 4b. notification types */
INSERT INTO dbo.NotificationType (Name, Description)
SELECT 'cable_gift_order', 'A Cable gift order was fulfilled or cancelled'
WHERE NOT EXISTS (SELECT 1 FROM dbo.NotificationType WHERE Name = 'cable_gift_order');
IF @@ROWCOUNT > 0 PRINT '  [SEEDED]  NotificationType cable_gift_order' ELSE PRINT '  [SKIPPED] NotificationType cable_gift_order exists';

INSERT INTO dbo.NotificationType (Name, Description)
SELECT 'promo_assigned', 'Cable gave the user a promo code (now in My codes)'
WHERE NOT EXISTS (SELECT 1 FROM dbo.NotificationType WHERE Name = 'promo_assigned');
IF @@ROWCOUNT > 0 PRINT '  [SEEDED]  NotificationType promo_assigned' ELSE PRINT '  [SKIPPED] NotificationType promo_assigned exists';

INSERT INTO dbo.NotificationType (Name, Description)
SELECT 'promo_expiring', 'A promo code in My codes expires within two days'
WHERE NOT EXISTS (SELECT 1 FROM dbo.NotificationType WHERE Name = 'promo_expiring');
IF @@ROWCOUNT > 0 PRINT '  [SEEDED]  NotificationType promo_expiring' ELSE PRINT '  [SKIPPED] NotificationType promo_expiring exists';
GO

-- Labels and routing (Part B columns) — dynamic SQL so the script still runs where those columns are missing.
IF COL_LENGTH('dbo.NotificationType', 'DeepLinksTo') IS NOT NULL AND COL_LENGTH('dbo.NotificationType', 'NameEn') IS NOT NULL
BEGIN
    EXEC sp_executesql N'
        UPDATE dbo.NotificationType SET DeepLinksTo = ''loyalty'', NameEn = N''Cable Gift'', NameAr = N''هدايا كيبل''
         WHERE Name = ''cable_gift_order'' AND (DeepLinksTo IS NULL OR NameEn IS NULL);
        UPDATE dbo.NotificationType SET DeepLinksTo = ''loyalty'', NameEn = N''New Code'', NameAr = N''كود جديد''
         WHERE Name = ''promo_assigned'' AND (DeepLinksTo IS NULL OR NameEn IS NULL);
        UPDATE dbo.NotificationType SET DeepLinksTo = ''loyalty'', NameEn = N''Code Expiring'', NameAr = N''كود بينتهي''
         WHERE Name = ''promo_expiring'' AND (DeepLinksTo IS NULL OR NameEn IS NULL);';
    PRINT '  [UPDATED] NotificationType labels / DeepLinksTo for the Sprint 5 types';
END
ELSE PRINT '  [SKIPPED] NotificationType has no DeepLinksTo / NameEn columns';
GO

PRINT '=== Sprint 5: Cable Gifts + promo codes — done ===';
GO
