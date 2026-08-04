-- ============================================================================
-- PRODUCTION MIGRATION SCRIPT
-- From: db_ab1977_cable (Dev) → To: db_ab1977_cableproduction (Production)
-- Generated: 2026-03-29
-- WARNING: Review carefully before executing. DO NOT execute blindly.
-- ============================================================================

-- ============================================================================
-- SECTION 1: ALTER EXISTING TABLES (Add new columns)
-- ============================================================================

-- 1.1 UserAccount - Add SecurityStamp and HasReadUpdateNotes
ALTER TABLE UserAccount ADD SecurityStamp nvarchar(100) NULL;
ALTER TABLE UserAccount ADD HasReadUpdateNotes bit NOT NULL DEFAULT 0;

-- 1.2 ChargingPoint - Add loyalty blocking + wallet columns
ALTER TABLE ChargingPoint ADD IsLoyaltyBlocked bit NOT NULL DEFAULT 0;
ALTER TABLE ChargingPoint ADD LoyaltyBlockedAt datetime NULL;
ALTER TABLE ChargingPoint ADD LoyaltyBlockedUntil datetime NULL;
ALTER TABLE ChargingPoint ADD LoyaltyBlockReason nvarchar(1000) NULL;
ALTER TABLE ChargingPoint ADD LoyaltyBlockedByUserId int NULL;
ALTER TABLE ChargingPoint ADD WalletBalance decimal(18,2) NOT NULL DEFAULT 0;
ALTER TABLE ChargingPoint ADD WalletCreditLimit decimal(18,2) NULL;

-- FK for ChargingPoint.LoyaltyBlockedByUserId
ALTER TABLE ChargingPoint ADD CONSTRAINT FK_ChargingPoint_LoyaltyBlockedByUser
    FOREIGN KEY (LoyaltyBlockedByUserId) REFERENCES UserAccount(Id);

-- ============================================================================
-- SECTION 2: CREATE NEW TABLES - Service Provider System
-- ============================================================================

-- 2.1 ServiceCategory
CREATE TABLE ServiceCategory (
    Id int IDENTITY(1,1) NOT NULL,
    Name nvarchar(255) NOT NULL,
    NameAr nvarchar(255) NULL,
    Description nvarchar(1000) NULL,
    IconUrl nvarchar(1000) NULL,
    SortOrder int NOT NULL DEFAULT 0,
    IsActive bit NOT NULL DEFAULT 1,
    CreatedAt datetime NOT NULL,
    CreatedBy int NULL,
    ModifiedAt datetime NULL,
    ModifiedBy int NULL,
    IsDeleted bit NOT NULL DEFAULT 0,
    CONSTRAINT PK_ServiceCategory PRIMARY KEY (Id)
);

CREATE NONCLUSTERED INDEX IX_ServiceCategory_SortOrder ON ServiceCategory(SortOrder);
CREATE NONCLUSTERED INDEX IX_ServiceCategory_IsActive_IsDeleted ON ServiceCategory(IsActive, IsDeleted);

-- 2.2 ServiceProvider
CREATE TABLE ServiceProvider (
    Id int IDENTITY(1,1) NOT NULL,
    Name nvarchar(255) NOT NULL,
    OwnerId int NOT NULL,
    ServiceCategoryId int NOT NULL,
    StatusId int NOT NULL,
    Description nvarchar(2000) NULL,
    Phone nvarchar(80) NULL,
    OwnerPhone nvarchar(80) NULL,
    Address nvarchar(2000) NULL,
    CountryName nvarchar(400) NULL,
    CityName nvarchar(400) NULL,
    Latitude float NOT NULL,
    Longitude float NOT NULL,
    Price float NULL,
    PriceDescription nvarchar(1000) NULL,
    FromTime nvarchar(32) NULL,
    ToTime nvarchar(32) NULL,
    MethodPayment nvarchar(400) NULL,
    VisitorsCount int NOT NULL DEFAULT 0,
    IsVerified bit NOT NULL DEFAULT 0,
    HasOffer bit NOT NULL DEFAULT 0,
    OfferDescription nvarchar(4000) NULL,
    Service nvarchar(MAX) NULL,
    Icon nvarchar(MAX) NULL,
    Note nvarchar(2000) NULL,
    WhatsAppNumber nvarchar(160) NULL,
    WebsiteUrl nvarchar(1000) NULL,
    CreatedAt datetime NOT NULL,
    CreatedBy int NULL,
    ModifiedAt datetime NULL,
    ModifiedBy int NULL,
    IsDeleted bit NOT NULL DEFAULT 0,
    IsLoyaltyBlocked bit NOT NULL DEFAULT 0,
    LoyaltyBlockedAt datetime NULL,
    LoyaltyBlockedUntil datetime NULL,
    LoyaltyBlockReason nvarchar(1000) NULL,
    LoyaltyBlockedByUserId int NULL,
    WalletBalance decimal(18,2) NOT NULL DEFAULT 0,
    WalletCreditLimit decimal(18,2) NULL,
    CONSTRAINT PK_ServiceProvider PRIMARY KEY (Id),
    CONSTRAINT FK_ServiceProvider_ServiceCategory FOREIGN KEY (ServiceCategoryId) REFERENCES ServiceCategory(Id),
    CONSTRAINT FK_ServiceProvider_Status FOREIGN KEY (StatusId) REFERENCES Status(Id),
    CONSTRAINT FK_ServiceProvider_UserAccount FOREIGN KEY (OwnerId) REFERENCES UserAccount(Id),
    CONSTRAINT FK_ServiceProvider_LoyaltyBlockedByUser FOREIGN KEY (LoyaltyBlockedByUserId) REFERENCES UserAccount(Id)
);

CREATE NONCLUSTERED INDEX IX_ServiceProvider_ServiceCategoryId ON ServiceProvider(ServiceCategoryId);
CREATE NONCLUSTERED INDEX IX_ServiceProvider_OwnerId ON ServiceProvider(OwnerId);
CREATE NONCLUSTERED INDEX IX_ServiceProvider_StatusId ON ServiceProvider(StatusId);
CREATE NONCLUSTERED INDEX IX_ServiceProvider_IsDeleted_IsVerified ON ServiceProvider(IsDeleted, IsVerified);
CREATE NONCLUSTERED INDEX IX_ServiceProvider_Lat_Lng ON ServiceProvider(Latitude, Longitude);

-- 2.3 ServiceProviderAttachment
CREATE TABLE ServiceProviderAttachment (
    Id int IDENTITY(1,1) NOT NULL,
    ServiceProviderId int NOT NULL,
    FileSize bigint NOT NULL,
    FileExtension nvarchar(100) NOT NULL,
    FileName nvarchar(255) NOT NULL,
    ContentType nvarchar(100) NOT NULL,
    CreatedAt datetime NOT NULL,
    CreatedBy int NULL,
    ModifiedAt datetime NULL,
    ModifiedBy int NULL,
    IsDeleted bit NOT NULL DEFAULT 0,
    CONSTRAINT PK_ServiceProviderAttachment PRIMARY KEY (Id),
    CONSTRAINT FK_ServiceProviderAttachment_ServiceProvider FOREIGN KEY (ServiceProviderId) REFERENCES ServiceProvider(Id)
);

CREATE NONCLUSTERED INDEX IX_ServiceProviderAttachment_ServiceProviderId ON ServiceProviderAttachment(ServiceProviderId);
CREATE NONCLUSTERED INDEX IX_ServiceProviderAttachment_IsDeleted ON ServiceProviderAttachment(IsDeleted);

-- 2.4 ServiceProviderRate
CREATE TABLE ServiceProviderRate (
    Id int IDENTITY(1,1) NOT NULL,
    ServiceProviderId int NOT NULL,
    UserId int NOT NULL,
    Rating int NOT NULL,
    AVGRating float NOT NULL DEFAULT 0,
    Comment nvarchar(2000) NULL,
    CreatedAt datetime NOT NULL,
    CreatedBy int NULL,
    ModifiedAt datetime NULL,
    ModifiedBy int NULL,
    IsDeleted bit NOT NULL DEFAULT 0,
    CONSTRAINT PK_ServiceProviderRate PRIMARY KEY (Id),
    CONSTRAINT FK_ServiceProviderRate_ServiceProvider FOREIGN KEY (ServiceProviderId) REFERENCES ServiceProvider(Id),
    CONSTRAINT FK_ServiceProviderRate_UserAccount FOREIGN KEY (UserId) REFERENCES UserAccount(Id)
);

CREATE NONCLUSTERED INDEX IX_ServiceProviderRate_ServiceProviderId ON ServiceProviderRate(ServiceProviderId);
CREATE NONCLUSTERED INDEX IX_ServiceProviderRate_UserId ON ServiceProviderRate(UserId);
CREATE NONCLUSTERED INDEX IX_ServiceProviderRate_UserId_ServiceProviderId ON ServiceProviderRate(UserId, ServiceProviderId);

-- 2.5 UserFavoriteServiceProvider
CREATE TABLE UserFavoriteServiceProvider (
    Id int IDENTITY(1,1) NOT NULL,
    UserId int NOT NULL,
    ServiceProviderId int NOT NULL,
    CreatedAt datetime NOT NULL,
    CreatedBy int NULL,
    ModifiedAt datetime NULL,
    ModifiedBy int NULL,
    IsDeleted bit NOT NULL DEFAULT 0,
    CONSTRAINT PK_UserFavoriteServiceProvider PRIMARY KEY (Id),
    CONSTRAINT FK_UserFavoriteServiceProvider_UserAccount FOREIGN KEY (UserId) REFERENCES UserAccount(Id),
    CONSTRAINT FK_UserFavoriteServiceProvider_ServiceProvider FOREIGN KEY (ServiceProviderId) REFERENCES ServiceProvider(Id)
);

CREATE UNIQUE NONCLUSTERED INDEX IX_UserFavoriteServiceProvider_Unique ON UserFavoriteServiceProvider(UserId, ServiceProviderId);

-- ============================================================================
-- SECTION 3: CREATE NEW TABLES - Loyalty System
-- ============================================================================

-- 3.1 LoyaltyPointAction
CREATE TABLE LoyaltyPointAction (
    Id int IDENTITY(1,1) NOT NULL,
    ActionCode nvarchar(200) NOT NULL,
    Name nvarchar(255) NOT NULL,
    Description nvarchar(1000) NULL,
    Points int NOT NULL,
    MaxPerDay int NULL,
    MaxPerLifetime int NULL,
    IsActive bit NOT NULL DEFAULT 1,
    CreatedAt datetime NOT NULL,
    CreatedBy int NULL,
    ModifiedAt datetime NULL,
    ModifiedBy int NULL,
    IsDeleted bit NOT NULL DEFAULT 0,
    CONSTRAINT PK_LoyaltyPointAction PRIMARY KEY (Id)
);

CREATE UNIQUE NONCLUSTERED INDEX IX_LoyaltyPointAction_ActionCode_Unique ON LoyaltyPointAction(ActionCode);

-- 3.2 LoyaltySeason
CREATE TABLE LoyaltySeason (
    Id int IDENTITY(1,1) NOT NULL,
    Name nvarchar(255) NOT NULL,
    Description nvarchar(1000) NULL,
    StartDate datetime NOT NULL,
    EndDate datetime NOT NULL,
    IsActive bit NOT NULL DEFAULT 1,
    CreatedAt datetime NOT NULL,
    CreatedBy int NULL,
    ModifiedAt datetime NULL,
    ModifiedBy int NULL,
    IsDeleted bit NOT NULL DEFAULT 0,
    CONSTRAINT PK_LoyaltySeason PRIMARY KEY (Id)
);

CREATE NONCLUSTERED INDEX IX_LoyaltySeason_IsActive ON LoyaltySeason(IsActive);
CREATE NONCLUSTERED INDEX IX_LoyaltySeason_StartDate_EndDate ON LoyaltySeason(StartDate, EndDate);

-- 3.3 LoyaltyTier
CREATE TABLE LoyaltyTier (
    Id int IDENTITY(1,1) NOT NULL,
    Name nvarchar(200) NOT NULL,
    MinPoints int NOT NULL,
    Multiplier float NOT NULL DEFAULT 1.0,
    BonusPoints int NOT NULL DEFAULT 0,
    IconUrl nvarchar(1000) NULL,
    IsActive bit NOT NULL DEFAULT 1,
    CONSTRAINT PK_LoyaltyTier PRIMARY KEY (Id)
);

-- 3.4 UserLoyaltyAccount
CREATE TABLE UserLoyaltyAccount (
    Id int IDENTITY(1,1) NOT NULL,
    UserId int NOT NULL,
    TotalPointsEarned int NOT NULL DEFAULT 0,
    TotalPointsRedeemed int NOT NULL DEFAULT 0,
    CurrentBalance int NOT NULL DEFAULT 0,
    CreatedAt datetime NOT NULL,
    CreatedBy int NULL,
    ModifiedAt datetime NULL,
    ModifiedBy int NULL,
    IsDeleted bit NOT NULL DEFAULT 0,
    IsBlocked bit NOT NULL DEFAULT 0,
    BlockedAt datetime NULL,
    BlockedUntil datetime NULL,
    BlockReason nvarchar(1000) NULL,
    BlockedByUserId int NULL,
    CONSTRAINT PK_UserLoyaltyAccount PRIMARY KEY (Id),
    CONSTRAINT FK_UserLoyaltyAccount_UserAccount FOREIGN KEY (UserId) REFERENCES UserAccount(Id),
    CONSTRAINT FK_UserLoyaltyAccount_BlockedByUser FOREIGN KEY (BlockedByUserId) REFERENCES UserAccount(Id)
);

CREATE UNIQUE NONCLUSTERED INDEX IX_UserLoyaltyAccount_UserId_Unique ON UserLoyaltyAccount(UserId);
CREATE NONCLUSTERED INDEX IX_UserLoyaltyAccount_CurrentBalance ON UserLoyaltyAccount(CurrentBalance);

-- 3.5 LoyaltyPointTransaction
CREATE TABLE LoyaltyPointTransaction (
    Id int IDENTITY(1,1) NOT NULL,
    UserLoyaltyAccountId int NOT NULL,
    LoyaltyPointActionId int NULL,
    LoyaltySeasonId int NULL,
    TransactionType int NOT NULL,
    Points int NOT NULL,
    BalanceAfter int NOT NULL,
    ReferenceType nvarchar(200) NULL,
    ReferenceId int NULL,
    Note nvarchar(1000) NULL,
    ExpiresAt datetime NULL,
    CreatedAt datetime NOT NULL,
    CreatedBy int NULL,
    ModifiedAt datetime NULL,
    ModifiedBy int NULL,
    IsDeleted bit NOT NULL DEFAULT 0,
    CONSTRAINT PK_LoyaltyPointTransaction PRIMARY KEY (Id),
    CONSTRAINT FK_LoyaltyPointTransaction_UserLoyaltyAccount FOREIGN KEY (UserLoyaltyAccountId) REFERENCES UserLoyaltyAccount(Id),
    CONSTRAINT FK_LoyaltyPointTransaction_LoyaltyPointAction FOREIGN KEY (LoyaltyPointActionId) REFERENCES LoyaltyPointAction(Id),
    CONSTRAINT FK_LoyaltyPointTransaction_LoyaltySeason FOREIGN KEY (LoyaltySeasonId) REFERENCES LoyaltySeason(Id)
);

CREATE NONCLUSTERED INDEX IX_LoyaltyPointTransaction_UserLoyaltyAccountId ON LoyaltyPointTransaction(UserLoyaltyAccountId);
CREATE NONCLUSTERED INDEX IX_LoyaltyPointTransaction_LoyaltySeasonId ON LoyaltyPointTransaction(LoyaltySeasonId);
CREATE NONCLUSTERED INDEX IX_LoyaltyPointTransaction_CreatedAt ON LoyaltyPointTransaction(CreatedAt);
CREATE NONCLUSTERED INDEX IX_LoyaltyPointTransaction_TransactionType ON LoyaltyPointTransaction(TransactionType);
CREATE NONCLUSTERED INDEX IX_LoyaltyPointTransaction_ReferenceType_ReferenceId ON LoyaltyPointTransaction(ReferenceType, ReferenceId);

-- 3.6 LoyaltyReward
CREATE TABLE LoyaltyReward (
    Id int IDENTITY(1,1) NOT NULL,
    Name nvarchar(255) NOT NULL,
    Description nvarchar(2000) NULL,
    PointsCost int NOT NULL,
    RewardType int NOT NULL,
    RewardValue nvarchar(1000) NULL,
    ProviderType nvarchar(100) NULL,
    ProviderId int NULL,
    ServiceCategoryId int NULL,
    MaxRedemptions int NULL,
    CurrentRedemptions int NOT NULL DEFAULT 0,
    ImageUrl nvarchar(1000) NULL,
    IsActive bit NOT NULL DEFAULT 1,
    ValidFrom datetime NOT NULL,
    ValidTo datetime NULL,
    CreatedAt datetime NOT NULL,
    CreatedBy int NULL,
    ModifiedAt datetime NULL,
    ModifiedBy int NULL,
    IsDeleted bit NOT NULL DEFAULT 0,
    CONSTRAINT PK_LoyaltyReward PRIMARY KEY (Id),
    CONSTRAINT FK_LoyaltyReward_ServiceCategory FOREIGN KEY (ServiceCategoryId) REFERENCES ServiceCategory(Id)
);

CREATE NONCLUSTERED INDEX IX_LoyaltyReward_IsActive_ValidFrom_ValidTo ON LoyaltyReward(IsActive, ValidFrom, ValidTo);
CREATE NONCLUSTERED INDEX IX_LoyaltyReward_RewardType ON LoyaltyReward(RewardType);
CREATE NONCLUSTERED INDEX IX_LoyaltyReward_ProviderType_ProviderId ON LoyaltyReward(ProviderType, ProviderId);
CREATE NONCLUSTERED INDEX IX_LoyaltyReward_ServiceCategoryId ON LoyaltyReward(ServiceCategoryId);

-- 3.7 UserRewardRedemption
CREATE TABLE UserRewardRedemption (
    Id int IDENTITY(1,1) NOT NULL,
    UserId int NOT NULL,
    LoyaltyRewardId int NOT NULL,
    LoyaltyPointTransactionId int NOT NULL,
    PointsSpent int NOT NULL,
    Status int NOT NULL,
    RedemptionCode nvarchar(100) NULL,
    ProviderType nvarchar(100) NULL,
    ProviderId int NULL,
    RedeemedAt datetime NOT NULL,
    FulfilledAt datetime NULL,
    CreatedAt datetime NOT NULL,
    CreatedBy int NULL,
    ModifiedAt datetime NULL,
    ModifiedBy int NULL,
    IsDeleted bit NOT NULL DEFAULT 0,
    CONSTRAINT PK_UserRewardRedemption PRIMARY KEY (Id),
    CONSTRAINT FK_UserRewardRedemption_UserAccount FOREIGN KEY (UserId) REFERENCES UserAccount(Id),
    CONSTRAINT FK_UserRewardRedemption_LoyaltyReward FOREIGN KEY (LoyaltyRewardId) REFERENCES LoyaltyReward(Id),
    CONSTRAINT FK_UserRewardRedemption_LoyaltyPointTransaction FOREIGN KEY (LoyaltyPointTransactionId) REFERENCES LoyaltyPointTransaction(Id)
);

CREATE NONCLUSTERED INDEX IX_UserRewardRedemption_UserId ON UserRewardRedemption(UserId);
CREATE NONCLUSTERED INDEX IX_UserRewardRedemption_LoyaltyRewardId ON UserRewardRedemption(LoyaltyRewardId);
CREATE NONCLUSTERED INDEX IX_UserRewardRedemption_Status ON UserRewardRedemption(Status);
CREATE NONCLUSTERED INDEX IX_UserRewardRedemption_RedemptionCode ON UserRewardRedemption(RedemptionCode);
CREATE NONCLUSTERED INDEX IX_UserRewardRedemption_ProviderType_ProviderId ON UserRewardRedemption(ProviderType, ProviderId);

-- 3.8 UserSeasonProgress
CREATE TABLE UserSeasonProgress (
    Id int IDENTITY(1,1) NOT NULL,
    UserId int NOT NULL,
    LoyaltySeasonId int NOT NULL,
    SeasonPointsEarned int NOT NULL DEFAULT 0,
    TierLevel int NOT NULL,
    CreatedAt datetime NOT NULL,
    CreatedBy int NULL,
    ModifiedAt datetime NULL,
    ModifiedBy int NULL,
    IsDeleted bit NOT NULL DEFAULT 0,
    CONSTRAINT PK_UserSeasonProgress PRIMARY KEY (Id),
    CONSTRAINT FK_UserSeasonProgress_UserAccount FOREIGN KEY (UserId) REFERENCES UserAccount(Id),
    CONSTRAINT FK_UserSeasonProgress_LoyaltySeason FOREIGN KEY (LoyaltySeasonId) REFERENCES LoyaltySeason(Id),
    CONSTRAINT FK_UserSeasonProgress_LoyaltyTier FOREIGN KEY (TierLevel) REFERENCES LoyaltyTier(Id)
);

CREATE UNIQUE NONCLUSTERED INDEX IX_UserSeasonProgress_UserId_SeasonId_Unique ON UserSeasonProgress(UserId, LoyaltySeasonId);
CREATE NONCLUSTERED INDEX IX_UserSeasonProgress_LoyaltySeasonId ON UserSeasonProgress(LoyaltySeasonId);
CREATE NONCLUSTERED INDEX IX_UserSeasonProgress_SeasonPointsEarned ON UserSeasonProgress(SeasonPointsEarned);

-- ============================================================================
-- SECTION 4: CREATE NEW TABLES - Points Conversion & Offers
-- ============================================================================

-- 4.1 PointsConversionRate
CREATE TABLE PointsConversionRate (
    Id int IDENTITY(1,1) NOT NULL,
    Name nvarchar(255) NOT NULL,
    CurrencyCode nvarchar(20) NOT NULL,
    PointsPerUnit float NOT NULL,
    IsDefault bit NOT NULL DEFAULT 0,
    IsActive bit NOT NULL DEFAULT 1,
    CreatedAt datetime NOT NULL,
    CreatedBy int NULL,
    ModifiedAt datetime NULL,
    ModifiedBy int NULL,
    IsDeleted bit NOT NULL DEFAULT 0,
    CONSTRAINT PK_PointsConversionRate PRIMARY KEY (Id)
);

CREATE NONCLUSTERED INDEX IX_PointsConversionRate_IsDefault ON PointsConversionRate(IsDefault);
CREATE NONCLUSTERED INDEX IX_PointsConversionRate_IsActive_IsDeleted ON PointsConversionRate(IsActive, IsDeleted);

-- 4.2 ProviderOffer
CREATE TABLE ProviderOffer (
    Id int IDENTITY(1,1) NOT NULL,
    Title nvarchar(255) NOT NULL,
    TitleAr nvarchar(255) NULL,
    Description nvarchar(2000) NULL,
    DescriptionAr nvarchar(2000) NULL,
    ProviderType nvarchar(100) NOT NULL,
    ProviderId int NOT NULL,
    ProposedByUserId int NOT NULL,
    ApprovalStatus int NOT NULL DEFAULT 1,
    ApprovedByUserId int NULL,
    ApprovalNote nvarchar(1000) NULL,
    ApprovedAt datetime NULL,
    MaxUsesPerUser int NULL,
    MaxTotalUses int NULL,
    CurrentTotalUses int NOT NULL DEFAULT 0,
    OfferCodeExpirySeconds int NOT NULL DEFAULT 60,
    ImageUrl nvarchar(1000) NULL,
    ValidFrom datetime NOT NULL,
    ValidTo datetime NULL,
    IsActive bit NOT NULL DEFAULT 1,
    CreatedAt datetime NOT NULL,
    CreatedBy int NULL,
    ModifiedAt datetime NULL,
    ModifiedBy int NULL,
    IsDeleted bit NOT NULL DEFAULT 0,
    PointsCost int NOT NULL,
    MonetaryValue decimal(18,3) NOT NULL,
    CurrencyCode nvarchar(20) NOT NULL DEFAULT 'JOD',
    CONSTRAINT PK_ProviderOffer PRIMARY KEY (Id),
    CONSTRAINT FK_ProviderOffer_ProposedByUser FOREIGN KEY (ProposedByUserId) REFERENCES UserAccount(Id),
    CONSTRAINT FK_ProviderOffer_ApprovedByUser FOREIGN KEY (ApprovedByUserId) REFERENCES UserAccount(Id)
);

CREATE NONCLUSTERED INDEX IX_ProviderOffer_ProviderType_ProviderId ON ProviderOffer(ProviderType, ProviderId);
CREATE NONCLUSTERED INDEX IX_ProviderOffer_ApprovalStatus ON ProviderOffer(ApprovalStatus);
CREATE NONCLUSTERED INDEX IX_ProviderOffer_IsActive_ValidFrom_ValidTo ON ProviderOffer(IsActive, ValidFrom, ValidTo);
CREATE NONCLUSTERED INDEX IX_ProviderOffer_ProposedByUserId ON ProviderOffer(ProposedByUserId);
CREATE NONCLUSTERED INDEX IX_ProviderOffer_IsDeleted ON ProviderOffer(IsDeleted);

-- 4.3 OfferTransaction
CREATE TABLE OfferTransaction (
    Id int IDENTITY(1,1) NOT NULL,
    ProviderOfferId int NOT NULL,
    UserId int NULL,
    OfferCode nvarchar(40) NOT NULL,
    Status int NOT NULL,
    CurrencyCode nvarchar(20) NOT NULL,
    ProviderType nvarchar(100) NOT NULL,
    ProviderId int NOT NULL,
    ConfirmedByUserId int NULL,
    CodeExpiresAt datetime NOT NULL,
    CompletedAt datetime NULL,
    CreatedAt datetime NOT NULL,
    CreatedBy int NULL,
    ModifiedAt datetime NULL,
    ModifiedBy int NULL,
    IsDeleted bit NOT NULL DEFAULT 0,
    PointsDeducted int NOT NULL DEFAULT 0,
    MonetaryValue decimal(18,3) NOT NULL DEFAULT 0,
    WalletCreditedAmount decimal(18,3) NOT NULL DEFAULT 0,
    CONSTRAINT PK_OfferTransaction PRIMARY KEY (Id),
    CONSTRAINT FK_OfferTransaction_ProviderOffer FOREIGN KEY (ProviderOfferId) REFERENCES ProviderOffer(Id),
    CONSTRAINT FK_OfferTransaction_User FOREIGN KEY (UserId) REFERENCES UserAccount(Id),
    CONSTRAINT FK_OfferTransaction_ConfirmedByUser FOREIGN KEY (ConfirmedByUserId) REFERENCES UserAccount(Id)
);

CREATE UNIQUE NONCLUSTERED INDEX IX_OfferTransaction_OfferCode_Unique ON OfferTransaction(OfferCode);
CREATE NONCLUSTERED INDEX IX_OfferTransaction_ProviderOfferId ON OfferTransaction(ProviderOfferId);
CREATE NONCLUSTERED INDEX IX_OfferTransaction_UserId ON OfferTransaction(UserId);
CREATE NONCLUSTERED INDEX IX_OfferTransaction_Status ON OfferTransaction(Status);
CREATE NONCLUSTERED INDEX IX_OfferTransaction_ProviderType_ProviderId ON OfferTransaction(ProviderType, ProviderId);
CREATE NONCLUSTERED INDEX IX_OfferTransaction_CodeExpiresAt ON OfferTransaction(CodeExpiresAt);
CREATE NONCLUSTERED INDEX IX_OfferTransaction_CompletedAt ON OfferTransaction(CompletedAt);

-- 4.4 OfferAttachment
CREATE TABLE OfferAttachment (
    Id int IDENTITY(1,1) NOT NULL,
    OfferId int NOT NULL,
    FileSize bigint NOT NULL,
    FileExtension nvarchar(100) NOT NULL,
    FileName nvarchar(255) NOT NULL,
    ContentType nvarchar(100) NOT NULL,
    CreatedAt datetime NOT NULL,
    CreatedBy int NULL,
    ModifiedAt datetime NULL,
    ModifiedBy int NULL,
    IsDeleted bit NOT NULL DEFAULT 0,
    CONSTRAINT PK_OfferAttachment PRIMARY KEY (Id),
    CONSTRAINT FK_OfferAttachment_ProviderOffer FOREIGN KEY (OfferId) REFERENCES ProviderOffer(Id)
);

-- ============================================================================
-- SECTION 5: CREATE NEW TABLES - Partner & Settlement System
-- ============================================================================

-- 5.1 PartnerAgreement
CREATE TABLE PartnerAgreement (
    Id int IDENTITY(1,1) NOT NULL,
    ProviderType nvarchar(100) NOT NULL,
    ProviderId int NOT NULL,
    CommissionPercentage float NOT NULL,
    PointsRewardPercentage float NOT NULL,
    PointsConversionRateId int NULL,
    CodeExpirySeconds int NOT NULL DEFAULT 120,
    IsActive bit NOT NULL DEFAULT 1,
    Note nvarchar(1000) NULL,
    IsDeleted bit NOT NULL DEFAULT 0,
    CreatedAt datetime NULL,
    CreatedBy int NULL,
    ModifiedAt datetime NULL,
    ModifiedBy int NULL,
    MinimumTransactionAmount decimal(18,3) NULL,
    CONSTRAINT PK_PartnerAgreement PRIMARY KEY (Id),
    CONSTRAINT FK_PartnerAgreement_PointsConversionRate FOREIGN KEY (PointsConversionRateId) REFERENCES PointsConversionRate(Id)
);

CREATE NONCLUSTERED INDEX IX_PartnerAgreement_ProviderType_ProviderId ON PartnerAgreement(ProviderType, ProviderId);
CREATE NONCLUSTERED INDEX IX_PartnerAgreement_IsActive ON PartnerAgreement(IsActive);
CREATE NONCLUSTERED INDEX IX_PartnerAgreement_IsDeleted ON PartnerAgreement(IsDeleted);

-- 5.2 PartnerTransaction
CREATE TABLE PartnerTransaction (
    Id int IDENTITY(1,1) NOT NULL,
    PartnerAgreementId int NOT NULL,
    UserId int NULL,
    TransactionCode nvarchar(40) NOT NULL,
    Status int NOT NULL,
    ProviderType nvarchar(100) NOT NULL,
    ProviderId int NOT NULL,
    TransactionAmount decimal(18,3) NULL,
    CurrencyCode nvarchar(20) NULL,
    CommissionPercentage float NOT NULL,
    CommissionAmount decimal(18,3) NULL,
    PointsRewardPercentage float NOT NULL,
    PointsConversionRate float NOT NULL,
    PointsEligibleAmount decimal(18,3) NULL,
    PointsAwarded int NULL,
    ConfirmedByUserId int NULL,
    CodeExpiresAt datetime NOT NULL,
    CompletedAt datetime NULL,
    IsDeleted bit NOT NULL DEFAULT 0,
    CreatedAt datetime NULL,
    CreatedBy int NULL,
    ModifiedAt datetime NULL,
    ModifiedBy int NULL,
    WalletCoveredAmount decimal(18,3) NOT NULL DEFAULT 0,
    CONSTRAINT PK_PartnerTransaction PRIMARY KEY (Id),
    CONSTRAINT FK_PartnerTransaction_PartnerAgreement FOREIGN KEY (PartnerAgreementId) REFERENCES PartnerAgreement(Id),
    CONSTRAINT FK_PartnerTransaction_User FOREIGN KEY (UserId) REFERENCES UserAccount(Id),
    CONSTRAINT FK_PartnerTransaction_ConfirmedByUser FOREIGN KEY (ConfirmedByUserId) REFERENCES UserAccount(Id)
);

CREATE UNIQUE NONCLUSTERED INDEX IX_PartnerTransaction_TransactionCode_Unique ON PartnerTransaction(TransactionCode);
CREATE NONCLUSTERED INDEX IX_PartnerTransaction_PartnerAgreementId ON PartnerTransaction(PartnerAgreementId);
CREATE NONCLUSTERED INDEX IX_PartnerTransaction_UserId ON PartnerTransaction(UserId);
CREATE NONCLUSTERED INDEX IX_PartnerTransaction_Status ON PartnerTransaction(Status);
CREATE NONCLUSTERED INDEX IX_PartnerTransaction_ProviderType_ProviderId ON PartnerTransaction(ProviderType, ProviderId);
CREATE NONCLUSTERED INDEX IX_PartnerTransaction_CodeExpiresAt ON PartnerTransaction(CodeExpiresAt);
CREATE NONCLUSTERED INDEX IX_PartnerTransaction_CompletedAt ON PartnerTransaction(CompletedAt);

-- 5.3 ProviderPayment (legacy table, still exists in dev)
CREATE TABLE ProviderPayment (
    Id int IDENTITY(1,1) NOT NULL,
    ProviderType nvarchar(100) NOT NULL,
    ProviderId int NOT NULL,
    Amount decimal(18,3) NOT NULL,
    Note nvarchar(1000) NULL,
    RecordedByUserId int NOT NULL,
    IsDeleted bit NOT NULL DEFAULT 0,
    CreatedAt datetime NOT NULL,
    CreatedBy int NULL,
    ModifiedAt datetime NULL,
    ModifiedBy int NULL,
    CONSTRAINT PK_ProviderPayment PRIMARY KEY (Id),
    CONSTRAINT FK_ProviderPayment_RecordedByUser FOREIGN KEY (RecordedByUserId) REFERENCES UserAccount(Id)
);

CREATE NONCLUSTERED INDEX IX_ProviderPayment_Provider ON ProviderPayment(ProviderType, ProviderId);
CREATE NONCLUSTERED INDEX IX_ProviderPayment_RecordedByUser ON ProviderPayment(RecordedByUserId);

-- 5.4 ProviderSettlement
CREATE TABLE ProviderSettlement (
    Id int IDENTITY(1,1) NOT NULL,
    ProviderType nvarchar(100) NOT NULL,
    ProviderId int NOT NULL,
    ProviderOwnerId int NOT NULL,
    PeriodYear int NOT NULL,
    PeriodMonth int NOT NULL DEFAULT 0,
    PeriodType int NOT NULL DEFAULT 2,
    PeriodWeek int NOT NULL DEFAULT 0,
    PartnerTransactionCount int NOT NULL DEFAULT 0,
    PartnerTransactionAmount decimal(18,3) NOT NULL DEFAULT 0,
    PartnerCommissionAmount decimal(18,3) NOT NULL DEFAULT 0,
    TotalPointsAwarded int NOT NULL DEFAULT 0,
    OfferTransactionCount int NOT NULL DEFAULT 0,
    OfferPaymentAmount decimal(18,3) NOT NULL DEFAULT 0,
    TotalPointsDeducted int NOT NULL DEFAULT 0,
    NetBalance decimal(18,3) NOT NULL DEFAULT 0,
    WalletApplied decimal(18,3) NOT NULL DEFAULT 0,
    SettlementStatus int NOT NULL DEFAULT 1,
    PaidAt datetime NULL,
    AdminNote nvarchar(2000) NULL,
    CreatedAt datetime NOT NULL,
    CreatedBy int NULL,
    ModifiedAt datetime NULL,
    ModifiedBy int NULL,
    IsDeleted bit NOT NULL DEFAULT 0,
    CONSTRAINT PK_ProviderSettlement PRIMARY KEY (Id),
    CONSTRAINT FK_ProviderSettlement_ProviderOwner FOREIGN KEY (ProviderOwnerId) REFERENCES UserAccount(Id)
);

CREATE NONCLUSTERED INDEX IX_ProviderSettlement_Provider_Period ON ProviderSettlement(ProviderType, ProviderId, PeriodType, PeriodYear, PeriodMonth, PeriodWeek);
CREATE NONCLUSTERED INDEX IX_ProviderSettlement_SettlementStatus ON ProviderSettlement(SettlementStatus);
CREATE NONCLUSTERED INDEX IX_ProviderSettlement_ProviderOwnerId ON ProviderSettlement(ProviderOwnerId);
CREATE NONCLUSTERED INDEX IX_ProviderSettlement_Period ON ProviderSettlement(PeriodType, PeriodYear, PeriodMonth, PeriodWeek);

-- 5.5 ProviderWalletTransaction
CREATE TABLE ProviderWalletTransaction (
    Id int IDENTITY(1,1) NOT NULL,
    ProviderType nvarchar(100) NOT NULL,
    ProviderId int NOT NULL,
    TransactionType int NOT NULL,
    Amount decimal(18,3) NOT NULL,
    BalanceAfter decimal(18,3) NOT NULL,
    ReferenceType nvarchar(100) NULL,
    ReferenceId int NULL,
    Note nvarchar(2000) NULL,
    RecordedByUserId int NOT NULL,
    CreatedAt datetime2 NOT NULL,
    CreatedBy int NULL,
    ModifiedAt datetime2 NULL,
    ModifiedBy int NULL,
    IsDeleted bit NOT NULL DEFAULT 0,
    CONSTRAINT PK_ProviderWalletTransaction PRIMARY KEY (Id),
    CONSTRAINT FK_ProviderWalletTransaction_RecordedByUser FOREIGN KEY (RecordedByUserId) REFERENCES UserAccount(Id)
);

CREATE NONCLUSTERED INDEX IX_ProviderWalletTransaction_Provider ON ProviderWalletTransaction(ProviderType, ProviderId);
CREATE NONCLUSTERED INDEX IX_ProviderWalletTransaction_RecordedByUser ON ProviderWalletTransaction(RecordedByUserId);

-- ============================================================================
-- SECTION 6: VERIFICATION QUERIES (Run after migration to verify)
-- ============================================================================

-- Check all new tables exist
-- SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME;

-- Check new columns on existing tables
-- SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'ChargingPoint' ORDER BY ORDINAL_POSITION;
-- SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'UserAccount' ORDER BY ORDINAL_POSITION;
