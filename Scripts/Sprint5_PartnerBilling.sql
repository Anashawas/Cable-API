-- Sprint 5 — Partner billing: admin-managed plans, subscription records, upgrade requests, scan bundles.
-- Docs/Sprint5-PartnerBilling.md. Idempotent: every step checks before it acts and can be re-run.
-- Non-destructive: the existing Subscription / Payment rows are not changed; they are COPIED into
-- PlanSubscription records, and the existing engine keeps driving expiry, grace and switch-off.
-- Existing partner agreements stay on the percentage model (BillingModel default 'percentage').
-- Apply to dev first, then production together with the API build that contains BillingRoutes.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/* ---------------------------------------------------------------------------
   1. Tables
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.BillingPlan', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BillingPlan
    (
        Id                  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BillingPlan PRIMARY KEY,
        [Key]               NVARCHAR(50)   NOT NULL,               -- fixed after creation
        Category            NVARCHAR(20)   NOT NULL,               -- Subscription | Loyalty
        NameAr              NVARCHAR(100)  NOT NULL,
        NameEn              NVARCHAR(100)  NOT NULL,
        DescriptionAr       NVARCHAR(1000) NULL,
        DescriptionEn       NVARCHAR(1000) NULL,
        AppliesTo           NVARCHAR(200)  NOT NULL CONSTRAINT DF_BillingPlan_AppliesTo DEFAULT N'[]',        -- JSON ["ChargingPoint","ServiceProvider"]
        Features            NVARCHAR(MAX)  NOT NULL CONSTRAINT DF_BillingPlan_Features DEFAULT N'[]',         -- JSON feature keys
        IncludesPlanIds     NVARCHAR(200)  NOT NULL CONSTRAINT DF_BillingPlan_Includes DEFAULT N'[]',         -- JSON plan ids
        PointsRule          NVARCHAR(MAX)  NULL,                                                              -- JSON Fixed | Tiered (loyalty)
        LegacyEntityTypes   NVARCHAR(200)  NULL,                   -- CSV of Subscription.EntityType sold through this plan
        TrialDays           INT            NOT NULL CONSTRAINT DF_BillingPlan_TrialDays DEFAULT 0,
        TrialScans          INT            NOT NULL CONSTRAINT DF_BillingPlan_TrialScans DEFAULT 0,
        IsActive            BIT            NOT NULL CONSTRAINT DF_BillingPlan_IsActive DEFAULT 1,
        IsVisible           BIT            NOT NULL CONSTRAINT DF_BillingPlan_IsVisible DEFAULT 1,
        SortOrder           INT            NOT NULL CONSTRAINT DF_BillingPlan_SortOrder DEFAULT 0,
        BadgeColor          NVARCHAR(20)   NULL,
        DailySoftCap        INT            NULL,
        TargetMarginPercent DECIMAL(5,2)   NULL,
        CreatedBy           INT            NULL,
        CreatedAt           DATETIME       NOT NULL,
        ModifiedBy          INT            NULL,
        ModifiedAt          DATETIME       NULL,
        IsDeleted           BIT            NOT NULL CONSTRAINT DF_BillingPlan_IsDeleted DEFAULT 0
    );
    CREATE UNIQUE INDEX UX_BillingPlan_Key ON dbo.BillingPlan ([Key]);
    PRINT 'Created dbo.BillingPlan';
END
ELSE PRINT 'SKIP dbo.BillingPlan exists';
GO

IF OBJECT_ID('dbo.BillingPlanPriceOption', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BillingPlanPriceOption
    (
        Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BillingPlanPriceOption PRIMARY KEY,
        BillingPlanId INT           NOT NULL,
        Months        INT           NULL,      -- null = no time limit (scan-count bundle)
        PriceJod      DECIMAL(18,3) NOT NULL,
        Scans         INT           NULL,      -- null on a loyalty plan = unlimited scanning
        IsActive      BIT           NOT NULL CONSTRAINT DF_BillingPlanPriceOption_IsActive DEFAULT 1,
        SortOrder     INT           NOT NULL CONSTRAINT DF_BillingPlanPriceOption_SortOrder DEFAULT 0,
        CreatedBy     INT           NULL,
        CreatedAt     DATETIME      NOT NULL,
        ModifiedBy    INT           NULL,
        ModifiedAt    DATETIME      NULL,
        IsDeleted     BIT           NOT NULL CONSTRAINT DF_BillingPlanPriceOption_IsDeleted DEFAULT 0,
        CONSTRAINT FK_BillingPlanPriceOption_BillingPlan FOREIGN KEY (BillingPlanId) REFERENCES dbo.BillingPlan (Id)
    );
    CREATE INDEX IX_BillingPlanPriceOption_Plan ON dbo.BillingPlanPriceOption (BillingPlanId);
    PRINT 'Created dbo.BillingPlanPriceOption';
END
ELSE PRINT 'SKIP dbo.BillingPlanPriceOption exists';
GO

IF OBJECT_ID('dbo.BillingPlanChangeLog', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BillingPlanChangeLog
    (
        Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BillingPlanChangeLog PRIMARY KEY,
        BillingPlanId   INT            NOT NULL,
        ChangedByUserId INT            NULL,
        ChangedAt       DATETIME       NOT NULL,
        Changes         NVARCHAR(MAX)  NOT NULL,     -- JSON [{ field, from, to }] / [{ field, added, removed }]
        Note            NVARCHAR(1000) NULL
    );
    CREATE INDEX IX_BillingPlanChangeLog_Plan_ChangedAt ON dbo.BillingPlanChangeLog (BillingPlanId, ChangedAt);
    PRINT 'Created dbo.BillingPlanChangeLog';
END
ELSE PRINT 'SKIP dbo.BillingPlanChangeLog exists';
GO

IF OBJECT_ID('dbo.PlanSubscription', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PlanSubscription
    (
        Id                  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PlanSubscription PRIMARY KEY,
        ProviderType        NVARCHAR(50)   NOT NULL,
        ProviderId          INT            NOT NULL,
        BillingPlanId       INT            NOT NULL,
        PlanKey             NVARCHAR(50)   NOT NULL,     -- snapshot
        PlanCategory        NVARCHAR(20)   NOT NULL,
        PlanNameAr          NVARCHAR(100)  NOT NULL,     -- snapshot
        PlanNameEn          NVARCHAR(100)  NOT NULL,     -- snapshot
        PriceOptionId       INT            NULL,
        PriceOptionSnapshot NVARCHAR(400)  NULL,         -- JSON { months, priceJod, scans }
        PointsRuleSnapshot  NVARCHAR(MAX)  NULL,         -- JSON rule at purchase
        IsTrial             BIT            NOT NULL CONSTRAINT DF_PlanSubscription_IsTrial DEFAULT 0,
        StartsAt            DATETIME       NOT NULL,     -- UTC
        EndsAt              DATETIME       NULL,         -- UTC; null = scan bundle with no time limit
        ScansIncluded       INT            NULL,         -- null = unlimited / not a loyalty plan
        ScansUsed           INT            NOT NULL CONSTRAINT DF_PlanSubscription_ScansUsed DEFAULT 0,
        ScansCarriedOver    INT            NOT NULL CONSTRAINT DF_PlanSubscription_ScansCarriedOver DEFAULT 0,
        Status              NVARCHAR(20)   NOT NULL CONSTRAINT DF_PlanSubscription_Status DEFAULT 'Active',   -- Active | Cancelled | Upgraded | Renewed (rest computed)
        PricePaidJod        DECIMAL(18,3)  NOT NULL CONSTRAINT DF_PlanSubscription_PricePaid DEFAULT 0,
        CreditAppliedJod    DECIMAL(18,3)  NOT NULL CONSTRAINT DF_PlanSubscription_Credit DEFAULT 0,
        PaymentId           INT            NULL,         -- dbo.Payment (existing engine)
        SubscriptionId      INT            NULL,         -- dbo.Subscription (existing engine)
        WalletTransactionId INT            NULL,         -- dbo.ProviderWalletTransaction (bundlePurchase)
        UpgradedFromId      INT            NULL,
        Source              NVARCHAR(20)   NOT NULL,     -- payment | admin | trial | bundle | upgrade | migration
        CreatedByUserId     INT            NULL,
        CancelledAt         DATETIME       NULL,
        CancelReason        NVARCHAR(500)  NULL,
        CancelledByUserId   INT            NULL,
        Note                NVARCHAR(1000) NULL,
        CreatedBy           INT            NULL,
        CreatedAt           DATETIME       NOT NULL,
        ModifiedBy          INT            NULL,
        ModifiedAt          DATETIME       NULL,
        IsDeleted           BIT            NOT NULL CONSTRAINT DF_PlanSubscription_IsDeleted DEFAULT 0,
        CONSTRAINT FK_PlanSubscription_BillingPlan FOREIGN KEY (BillingPlanId) REFERENCES dbo.BillingPlan (Id)
    );
    CREATE INDEX IX_PlanSubscription_Provider ON dbo.PlanSubscription (ProviderType, ProviderId, PlanCategory);
    CREATE INDEX IX_PlanSubscription_Plan ON dbo.PlanSubscription (BillingPlanId);
    CREATE INDEX IX_PlanSubscription_Payment ON dbo.PlanSubscription (PaymentId);
    CREATE INDEX IX_PlanSubscription_Subscription ON dbo.PlanSubscription (SubscriptionId);
    PRINT 'Created dbo.PlanSubscription';
END
ELSE PRINT 'SKIP dbo.PlanSubscription exists';
GO

IF OBJECT_ID('dbo.PlanUpgradeRequest', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PlanUpgradeRequest
    (
        Id                          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PlanUpgradeRequest PRIMARY KEY,
        ProviderType                NVARCHAR(50)   NOT NULL,
        ProviderId                  INT            NOT NULL,
        BillingPlanId               INT            NOT NULL,
        PlanKey                     NVARCHAR(50)   NOT NULL,
        PriceOptionId               INT            NULL,
        Months                      INT            NULL,
        Kind                        NVARCHAR(20)   NOT NULL,      -- upgrade | renew | bundle
        Note                        NVARCHAR(1000) NULL,
        Status                      NVARCHAR(20)   NOT NULL CONSTRAINT DF_PlanUpgradeRequest_Status DEFAULT 'Pending',   -- Pending | Fulfilled | Rejected
        RequestedByUserId           INT            NOT NULL,
        HandledByUserId             INT            NULL,
        HandledAt                   DATETIME       NULL,
        RejectReason                NVARCHAR(500)  NULL,
        FulfilledPaymentId          INT            NULL,
        FulfilledPlanSubscriptionId INT            NULL,
        CreatedBy                   INT            NULL,
        CreatedAt                   DATETIME       NOT NULL,
        ModifiedBy                  INT            NULL,
        ModifiedAt                  DATETIME       NULL,
        IsDeleted                   BIT            NOT NULL CONSTRAINT DF_PlanUpgradeRequest_IsDeleted DEFAULT 0
    );
    CREATE INDEX IX_PlanUpgradeRequest_Provider_Status ON dbo.PlanUpgradeRequest (ProviderType, ProviderId, Status);
    CREATE INDEX IX_PlanUpgradeRequest_Status ON dbo.PlanUpgradeRequest (Status);
    PRINT 'Created dbo.PlanUpgradeRequest';
END
ELSE PRINT 'SKIP dbo.PlanUpgradeRequest exists';
GO

IF OBJECT_ID('dbo.BillingEvent', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BillingEvent
    (
        Id                   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BillingEvent PRIMARY KEY,
        EventKey             NVARCHAR(150)  NOT NULL,     -- unique: the same push / alert is never sent twice
        Kind                 NVARCHAR(50)   NOT NULL,
        Audience             NVARCHAR(10)   NOT NULL CONSTRAINT DF_BillingEvent_Audience DEFAULT 'owner',   -- owner | admin
        ProviderType         NVARCHAR(50)   NULL,
        ProviderId           INT            NULL,
        PlanSubscriptionId   INT            NULL,
        SubscriptionId       INT            NULL,
        Title                NVARCHAR(300)  NULL,
        Body                 NVARCHAR(1000) NULL,
        CreatedAt            DATETIME       NOT NULL,
        AcknowledgedAt       DATETIME       NULL,
        AcknowledgedByUserId INT            NULL
    );
    CREATE UNIQUE INDEX UX_BillingEvent_EventKey ON dbo.BillingEvent (EventKey);
    CREATE INDEX IX_BillingEvent_Audience_CreatedAt ON dbo.BillingEvent (Audience, CreatedAt);
    PRINT 'Created dbo.BillingEvent';
END
ELSE PRINT 'SKIP dbo.BillingEvent exists';
GO

IF OBJECT_ID('dbo.PartnerTransactionBundleScan', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PartnerTransactionBundleScan
    (
        Id                         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PartnerTransactionBundleScan PRIMARY KEY,
        PartnerTransactionId       INT      NOT NULL,
        PlanSubscriptionId         INT      NULL,     -- bundle active when the code was created
        ConsumedPlanSubscriptionId INT      NULL,     -- bundle the scan was taken from at completion
        ConsumedAt                 DATETIME NULL,
        QuotaReached               BIT      NOT NULL CONSTRAINT DF_PartnerTransactionBundleScan_QuotaReached DEFAULT 0,
        RefundedAt                 DATETIME NULL,
        CreatedAt                  DATETIME NOT NULL,
        CONSTRAINT FK_PartnerTransactionBundleScan_PartnerTransaction FOREIGN KEY (PartnerTransactionId) REFERENCES dbo.PartnerTransaction (Id)
    );
    CREATE UNIQUE INDEX UX_PartnerTransactionBundleScan_Transaction ON dbo.PartnerTransactionBundleScan (PartnerTransactionId);
    CREATE INDEX IX_PartnerTransactionBundleScan_Consumed ON dbo.PartnerTransactionBundleScan (ConsumedPlanSubscriptionId, ConsumedAt);
    PRINT 'Created dbo.PartnerTransactionBundleScan';
END
ELSE PRINT 'SKIP dbo.PartnerTransactionBundleScan exists';
GO

/* ---------------------------------------------------------------------------
   2. PartnerAgreement.BillingModel — every existing agreement becomes 'percentage' (today's behaviour)
   --------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.PartnerAgreement', 'BillingModel') IS NULL
BEGIN
    ALTER TABLE dbo.PartnerAgreement ADD BillingModel NVARCHAR(20) NOT NULL CONSTRAINT DF_PartnerAgreement_BillingModel DEFAULT 'percentage';
    PRINT 'Added PartnerAgreement.BillingModel (all existing agreements = percentage)';
END
ELSE PRINT 'SKIP PartnerAgreement.BillingModel exists';
GO

IF COL_LENGTH('dbo.PartnerAgreement', 'BillingModelChangedAt') IS NULL
BEGIN
    ALTER TABLE dbo.PartnerAgreement ADD BillingModelChangedAt DATETIME NULL;
    PRINT 'Added PartnerAgreement.BillingModelChangedAt';
END
ELSE PRINT 'SKIP PartnerAgreement.BillingModelChangedAt exists';
GO

/* ---------------------------------------------------------------------------
   3. Seed plans (decisions 1, 3, 8, 9, 15, 18). Prices and tiers are the document's
      defaults — the admin edits them from the plans screen. Existing rows are never overwritten.
   --------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.BillingPlan WHERE [Key] = 'free')
BEGIN
    INSERT INTO dbo.BillingPlan ([Key], Category, NameAr, NameEn, DescriptionAr, DescriptionEn, AppliesTo, Features, IncludesPlanIds,
                                 PointsRule, LegacyEntityTypes, TrialDays, TrialScans, IsActive, IsVisible, SortOrder, BadgeColor, CreatedAt)
    VALUES ('free', 'Subscription', N'مجانية', N'Free',
            N'الباقة الافتراضية لكل محطة ومزوّد بدون اشتراك فعّال', N'The default plan for every station and provider without an active subscription',
            N'["ChargingPoint","ServiceProvider"]', N'["verifiedBadge","editInfo"]', N'[]',
            NULL, NULL, 0, 0, 1, 1, 1, '#9E9E9E', GETUTCDATE());
    PRINT 'Seeded plan: free';
END
ELSE PRINT 'SKIP plan free exists';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.BillingPlan WHERE [Key] = 'premium')
BEGIN
    INSERT INTO dbo.BillingPlan ([Key], Category, NameAr, NameEn, DescriptionAr, DescriptionEn, AppliesTo, Features, IncludesPlanIds,
                                 PointsRule, LegacyEntityTypes, TrialDays, TrialScans, IsActive, IsVisible, SortOrder, BadgeColor, CreatedAt)
    VALUES ('premium', 'Subscription', N'مميّزة', N'Premium',
            N'الظهور أولاً بالخريطة، الصور، الشكاوى والتقييمات، الإحصائيات، الموظفين والبصمة', N'Appear first on the map, photos, complaints and ratings, analytics, employees and biometrics',
            N'["ChargingPoint","ServiceProvider"]',
            N'["verifiedBadge","editInfo","appearFirst","images","complaints","analytics","workers","biometrics"]', N'[]',
            NULL, 'StationPremium,ServiceProviderPremium', 14, 0, 1, 1, 2, '#F5A623', GETUTCDATE());
    DECLARE @premium INT = SCOPE_IDENTITY();
    INSERT INTO dbo.BillingPlanPriceOption (BillingPlanId, Months, PriceJod, Scans, IsActive, SortOrder, CreatedAt) VALUES
        (@premium, 1,  20,  NULL, 1, 0, GETUTCDATE()),
        (@premium, 3,  60,  NULL, 1, 1, GETUTCDATE()),
        (@premium, 6,  120, NULL, 1, 2, GETUTCDATE()),
        (@premium, 12, 240, NULL, 1, 3, GETUTCDATE());
    PRINT 'Seeded plan: premium (20 JOD / month; 1 / 3 / 6 / 12 months)';
END
ELSE PRINT 'SKIP plan premium exists';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.BillingPlan WHERE [Key] = 'connect')
BEGIN
    DECLARE @premiumId INT = (SELECT Id FROM dbo.BillingPlan WHERE [Key] = 'premium');
    INSERT INTO dbo.BillingPlan ([Key], Category, NameAr, NameEn, DescriptionAr, DescriptionEn, AppliesTo, Features, IncludesPlanIds,
                                 PointsRule, LegacyEntityTypes, TrialDays, TrialScans, IsActive, IsVisible, SortOrder, BadgeColor, CreatedAt)
    VALUES ('connect', 'Subscription', N'كيبل كونكت', N'Cable Connect',
            N'كل مميزات المميّزة مع الشواحن المباشرة (كيبل كونكت)', N'Everything in Premium plus live chargers (Cable Connect)',
            N'["ChargingPoint"]',
            N'["verifiedBadge","editInfo","appearFirst","images","complaints","analytics","workers","biometrics","connect"]',
            CONCAT(N'[', @premiumId, N']'),
            NULL, 'OcppConnect', 0, 0, 1, 1, 3, '#1E88E5', GETUTCDATE());
    DECLARE @connect INT = SCOPE_IDENTITY();
    INSERT INTO dbo.BillingPlanPriceOption (BillingPlanId, Months, PriceJod, Scans, IsActive, SortOrder, CreatedAt) VALUES
        (@connect, 1,  40,  NULL, 1, 0, GETUTCDATE()),
        (@connect, 3,  120, NULL, 1, 1, GETUTCDATE()),
        (@connect, 6,  240, NULL, 1, 2, GETUTCDATE()),
        (@connect, 12, 480, NULL, 1, 3, GETUTCDATE());
    PRINT 'Seeded plan: connect (40 JOD / month, includes premium)';
END
ELSE PRINT 'SKIP plan connect exists';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.BillingPlan WHERE [Key] = 'loyalty_bundle_400')
BEGIN
    INSERT INTO dbo.BillingPlan ([Key], Category, NameAr, NameEn, DescriptionAr, DescriptionEn, AppliesTo, Features, IncludesPlanIds,
                                 PointsRule, LegacyEntityTypes, TrialDays, TrialScans, IsActive, IsVisible, SortOrder, BadgeColor, TargetMarginPercent, CreatedAt)
    VALUES ('loyalty_bundle_400', 'Loyalty', N'حزمة 400 مسحة', N'400-scan bundle',
            N'400 مسحة ولاء مدفوعة مقدّماً — كل مسحة بعدها ما بتكلّفك إشي، امسح لكل زبون', N'400 prepaid loyalty scans — scan every customer at no extra cost',
            N'["ChargingPoint","ServiceProvider"]', N'[]', N'[]',
            N'{"mode":"Tiered","fixedPoints":null,"tiers":[{"fromJod":0,"toJod":5,"points":20},{"fromJod":5,"toJod":20,"points":50},{"fromJod":20,"toJod":null,"points":100}]}',
            NULL, 0, 0, 1, 1, 10, '#43A047', 20, GETUTCDATE());
    DECLARE @bundle INT = SCOPE_IDENTITY();
    INSERT INTO dbo.BillingPlanPriceOption (BillingPlanId, Months, PriceJod, Scans, IsActive, SortOrder, CreatedAt) VALUES
        (@bundle, NULL, 40, 400, 1, 0, GETUTCDATE());
    PRINT 'Seeded plan: loyalty_bundle_400 (400 scans / 40 JOD; 20 / 50 / 100 points for < 5 / 5-20 / 20+ JOD)';
END
ELSE PRINT 'SKIP plan loyalty_bundle_400 exists';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.BillingPlan WHERE [Key] = 'loyalty_unlimited')
BEGIN
    INSERT INTO dbo.BillingPlan ([Key], Category, NameAr, NameEn, DescriptionAr, DescriptionEn, AppliesTo, Features, IncludesPlanIds,
                                 PointsRule, LegacyEntityTypes, TrialDays, TrialScans, IsActive, IsVisible, SortOrder, BadgeColor, DailySoftCap, TargetMarginPercent, CreatedAt)
    VALUES ('loyalty_unlimited', 'Loyalty', N'ولاء بلا حدود', N'Unlimited Loyalty',
            N'مسح غير محدود طول مدة الاشتراك', N'Unlimited loyalty scans for the whole period',
            N'["ChargingPoint","ServiceProvider"]', N'[]', N'[]',
            N'{"mode":"Tiered","fixedPoints":null,"tiers":[{"fromJod":1,"toJod":3,"points":10},{"fromJod":3,"toJod":5,"points":20},{"fromJod":5,"toJod":10,"points":40},{"fromJod":10,"toJod":null,"points":70}]}',
            NULL, 14, 50, 1, 1, 11, '#7E57C2', 150, 20, GETUTCDATE());
    DECLARE @unlimited INT = SCOPE_IDENTITY();
    INSERT INTO dbo.BillingPlanPriceOption (BillingPlanId, Months, PriceJod, Scans, IsActive, SortOrder, CreatedAt) VALUES
        (@unlimited, 1, 60,  NULL, 1, 0, GETUTCDATE()),
        (@unlimited, 3, 160, NULL, 1, 1, GETUTCDATE());
    PRINT 'Seeded plan: loyalty_unlimited (60 JOD / month, 160 JOD / 3 months; soft cap 150 scans / day)';
END
ELSE PRINT 'SKIP plan loyalty_unlimited exists';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.BillingPlanChangeLog l JOIN dbo.BillingPlan p ON p.Id = l.BillingPlanId WHERE p.[Key] = 'free')
BEGIN
    INSERT INTO dbo.BillingPlanChangeLog (BillingPlanId, ChangedByUserId, ChangedAt, Changes, Note)
    SELECT p.Id, NULL, GETUTCDATE(), N'[{"field":"created"}]', N'Seeded by Sprint5_PartnerBilling.sql'
    FROM dbo.BillingPlan p
    WHERE p.[Key] IN ('free', 'premium', 'connect', 'loyalty_bundle_400', 'loyalty_unlimited')
      AND NOT EXISTS (SELECT 1 FROM dbo.BillingPlanChangeLog l WHERE l.BillingPlanId = p.Id);
    PRINT 'Seeded plan change log entries';
END
ELSE PRINT 'SKIP plan change log seed';
GO

/* ---------------------------------------------------------------------------
   4. Settings (decisions 12, 19). The point value (decision 10) is the default PointsConversionRate.
   --------------------------------------------------------------------------- */
-- Billing.PointsPerJod is NOT seeded: the value of a point comes from the default PointsConversionRate
-- (decision 10, confirmed Oct 2026: 50 points = 1 JOD), so the percentage model and the margin maths share one number.
-- A row seeded by an earlier run of this script (value 1000) is removed.
DELETE FROM dbo.AppSetting WHERE [Key] = 'Billing.PointsPerJod' AND Value = '1000';
PRINT 'Billing.PointsPerJod follows the default points conversion rate';

IF NOT EXISTS (SELECT 1 FROM dbo.AppSetting WHERE [Key] = 'Billing.TargetMarginPercent' AND IsDeleted = 0)
BEGIN
    INSERT INTO dbo.AppSetting ([Key], Value, CreatedAt, IsDeleted) VALUES ('Billing.TargetMarginPercent', '20', GETUTCDATE(), 0);
    PRINT 'Seeded AppSetting Billing.TargetMarginPercent = 20';
END
ELSE PRINT 'SKIP AppSetting Billing.TargetMarginPercent exists';

IF NOT EXISTS (SELECT 1 FROM dbo.AppSetting WHERE [Key] = 'Billing.BundleMinimumTransactionAmount' AND IsDeleted = 0)
BEGIN
    INSERT INTO dbo.AppSetting ([Key], Value, CreatedAt, IsDeleted) VALUES ('Billing.BundleMinimumTransactionAmount', '2', GETUTCDATE(), 0);
    PRINT 'Seeded AppSetting Billing.BundleMinimumTransactionAmount = 2';
END
ELSE PRINT 'SKIP AppSetting Billing.BundleMinimumTransactionAmount exists';
GO

/* ---------------------------------------------------------------------------
   5. Notification types (inbox rows for billing pushes)
   --------------------------------------------------------------------------- */
INSERT INTO dbo.NotificationType ([Name], [Description], DeepLinksTo, NameEn, NameAr)
SELECT v.[Name], v.[Description], v.DeepLinksTo, v.NameEn, v.NameAr
FROM (VALUES
    ('billing_subscription',    'Partner plan: expiring in 7 days / tomorrow, expired, grace ended, activated, trial ending', 'provider', N'Plan',             N'الباقة'),
    ('billing_bundle',          'Partner scan bundle: 20% / 10% left, used up, activated',                                     'provider', N'Scan bundle',      N'حزمة المسحات'),
    ('billing_upgrade_request', 'Admin: a partner sent an upgrade / renewal / bundle request',                                 'none',     N'Upgrade request',  N'طلب ترقية'),
    ('billing_admin_alert',     'Admin: unlimited loyalty plan over its daily soft cap or monthly break-even',                 'none',     N'Billing alert',    N'تنبيه فوترة')
) v ([Name], [Description], DeepLinksTo, NameEn, NameAr)
WHERE NOT EXISTS (SELECT 1 FROM dbo.NotificationType t WHERE t.[Name] = v.[Name]);
PRINT CONCAT('Notification types added: ', @@ROWCOUNT);
GO

/* ---------------------------------------------------------------------------
   6. Migration: existing subscriptions → subscription records (non-destructive copy).
      One record per non-void payment; a Subscription row with no payment gets one record for its period.
      Source = 'migration', so no activation push is sent for history.
   --------------------------------------------------------------------------- */
INSERT INTO dbo.PlanSubscription (ProviderType, ProviderId, BillingPlanId, PlanKey, PlanCategory, PlanNameAr, PlanNameEn,
                                  PriceOptionId, PriceOptionSnapshot, PointsRuleSnapshot, IsTrial, StartsAt, EndsAt,
                                  ScansIncluded, ScansUsed, ScansCarriedOver, Status, PricePaidJod, CreditAppliedJod,
                                  PaymentId, SubscriptionId, Source, CreatedByUserId, Note, CreatedBy, CreatedAt, IsDeleted)
SELECT CASE s.EntityType WHEN 'ServiceProviderPremium' THEN 'ServiceProvider' ELSE 'ChargingPoint' END,
       s.EntityId, bp.Id, bp.[Key], bp.Category, bp.NameAr, bp.NameEn,
       po.Id,
       CONCAT('{"months":', COALESCE(CAST(m.Months AS NVARCHAR(10)), 'null'), ',"priceJod":', CAST(p.Amount AS NVARCHAR(30)), ',"scans":null}'),
       NULL, 0, p.PeriodStart, p.PeriodEnd,
       NULL, 0, 0, 'Active', p.Amount, 0,
       p.Id, s.Id, 'migration', p.CreatedBy, N'Copied from the existing subscription engine', p.CreatedBy, p.CreatedAt, 0
FROM dbo.Payment p
JOIN dbo.Subscription s ON s.Id = p.SubscriptionId
JOIN dbo.BillingPlan bp ON bp.[Key] = CASE s.EntityType WHEN 'OcppConnect' THEN 'connect' ELSE 'premium' END
CROSS APPLY (SELECT Months = NULLIF(DATEDIFF(MONTH, p.PeriodStart, p.PeriodEnd), 0)) m
OUTER APPLY (SELECT TOP 1 o.Id FROM dbo.BillingPlanPriceOption o
             WHERE o.BillingPlanId = bp.Id AND o.Months = m.Months AND o.Scans IS NULL AND o.IsDeleted = 0
             ORDER BY o.SortOrder) po
WHERE s.IsDeleted = 0 AND p.IsDeleted = 0 AND p.IsVoid = 0
  AND s.EntityType IN ('StationPremium', 'ServiceProviderPremium', 'OcppConnect')
  AND NOT EXISTS (SELECT 1 FROM dbo.PlanSubscription r WHERE r.PaymentId = p.Id);
PRINT CONCAT('Migrated payments into PlanSubscription records: ', @@ROWCOUNT);
GO

INSERT INTO dbo.PlanSubscription (ProviderType, ProviderId, BillingPlanId, PlanKey, PlanCategory, PlanNameAr, PlanNameEn,
                                  PriceOptionId, PriceOptionSnapshot, PointsRuleSnapshot, IsTrial, StartsAt, EndsAt,
                                  ScansIncluded, ScansUsed, ScansCarriedOver, Status, PricePaidJod, CreditAppliedJod,
                                  PaymentId, SubscriptionId, Source, Note, CreatedAt, IsDeleted)
SELECT CASE s.EntityType WHEN 'ServiceProviderPremium' THEN 'ServiceProvider' ELSE 'ChargingPoint' END,
       s.EntityId, bp.Id, bp.[Key], bp.Category, bp.NameAr, bp.NameEn,
       NULL, CONCAT('{"months":', COALESCE(CAST(s.PlanMonths AS NVARCHAR(10)), 'null'), ',"priceJod":0,"scans":null}'),
       NULL, 0, s.StartDate, s.ExpiresAt,
       NULL, 0, 0, 'Active', 0, 0,
       NULL, s.Id, 'migration', N'Copied from the existing subscription engine (no payment on record)', s.CreatedAt, 0
FROM dbo.Subscription s
JOIN dbo.BillingPlan bp ON bp.[Key] = CASE s.EntityType WHEN 'OcppConnect' THEN 'connect' ELSE 'premium' END
WHERE s.IsDeleted = 0
  AND s.EntityType IN ('StationPremium', 'ServiceProviderPremium', 'OcppConnect')
  AND NOT EXISTS (SELECT 1 FROM dbo.Payment p WHERE p.SubscriptionId = s.Id AND p.IsDeleted = 0 AND p.IsVoid = 0)
  AND NOT EXISTS (SELECT 1 FROM dbo.PlanSubscription r WHERE r.SubscriptionId = s.Id);
PRINT CONCAT('Migrated unpaid subscription periods into PlanSubscription records: ', @@ROWCOUNT);
GO

PRINT 'Sprint5_PartnerBilling.sql done.';
GO
