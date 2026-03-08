# Offers System Refactoring & Background Jobs

> This document covers two major changes implemented in the Cable EV Charging platform:
> 1. Offers system refactored from **Earn Points** to **Spend Points**
> 2. Hangfire **Background Jobs** service with 11 recurring jobs

---

## 1. Offers System - What Changed

### Old Model (Removed)
Users **earned points** when using an offer. The system calculated commission percentages, conversion rates, and awarded points based on a transaction amount entered by the provider.

**Old fields on ProviderOffer:** `CommissionPercentage`, `PointsRewardPercentage`, `PointsConversionRateId`, `MinTransactionAmount`, `MaxTransactionAmount`

**Old fields on OfferTransaction:** `TransactionAmount`, `CommissionPercentage`, `CommissionAmount`, `PointsRewardPercentage`, `PointsConversionRate`, `PointsEligibleAmount`, `PointsAwarded`

### New Model (Current)
Users **spend points** to redeem offers. Each offer has a **fixed points cost** and a **fixed monetary value** (what Cable pays the provider per redemption). No commission calculations, no conversion rates.

**New fields on ProviderOffer:** `PointsCost` (int), `MonetaryValue` (decimal), `CurrencyCode` (string, default "KWD")

**New fields on OfferTransaction:** `PointsDeducted` (int), `MonetaryValue` (decimal), `CurrencyCode` (string, default "KWD")

**New field on ProviderSettlement:** `TotalPointsDeducted` (int) - kept `TotalPointsAwarded` for partner transactions

### Flow Diagram

```
 PROVIDER STAFF                   SYSTEM                         USER (Mobile App)
  |                                 |                               |
  |  1. Select offer                |                               |
  |  POST /provider/CreateTransaction                               |
  |  { offerId: 5 }                |                               |
  |------------------------------->|                               |
  |                                 |                               |
  |  2. Gets QR code               |                               |
  |  { offerCode: "CBL-7X9K2M",   |                               |
  |    pointsCost: 100,            |                               |
  |    monetaryValue: 3.000 }      |                               |
  |<-------------------------------|                               |
  |                                 |                               |
  |  3. Shows QR to customer        |                               |
  |  ==============================+=============================>|
  |                                 |                               |
  |                                 |  4. User scans QR code        |
  |                                 |  POST /ScanOfferCode          |
  |                                 |  ?code=CBL-7X9K2M             |
  |                                 |<------------------------------|
  |                                 |                               |
  |                                 |  5. System:                   |
  |                                 |  - Checks user balance >= 100 |
  |                                 |  - Deducts 100 points         |
  |                                 |  - Marks TX completed         |
  |                                 |                               |
  |                                 |  6. Response:                 |
  |                                 |  { offerTitle: "Free Coffee", |
  |                                 |    pointsDeducted: 100,       |
  |                                 |    monetaryValue: 3.000 }     |
  |                                 |------------------------------>|
```

### Key Business Rules
1. Offers have a **fixed `PointsCost`** - users must have enough points to redeem
2. Offers have a **fixed `MonetaryValue`** - what Cable pays the provider per redemption
3. No commission percentages or conversion rates involved
4. QR codes expire after `OfferCodeExpiryMinutes` (default 30 min)
5. Points are deducted **before** marking the transaction as completed
6. If user has insufficient points, the transaction stays `Initiated` and an error is returned
7. `MaxUsesPerUser` and `MaxTotalUses` limit redemptions
8. Offers require admin approval before going live (`ApprovalStatus`)
9. Partners still **award points** via `AwardPointsFromOfferAsync` (unchanged)
10. Offers **deduct points** via `DeductPointsFromOfferAsync` (new)

---

## 2. API Endpoints

> Base URL: `/api/offers`

### User Endpoints

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/GetActiveOffers?providerType=` | No | Get all active approved offers |
| GET | `/GetOfferById/{id}` | No | Get single offer by ID |
| POST | `/ScanOfferCode?code={code}` | Yes | Scan QR to redeem offer (deducts points) |
| GET | `/GetMyTransactions?status=` | Yes | Get current user's transaction history |

### Provider Endpoints

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/ProposeOffer` | Yes | Propose new offer (requires admin approval) |
| GET | `/GetOffersForProvider?providerType=&providerId=` | Yes | Get all offers for a provider |
| POST | `/provider/CreateTransaction` | Yes | Generate QR code for customer |
| POST | `/provider/CancelTransaction/{id}` | Yes | Cancel initiated transaction |
| GET | `/GetProviderTransactions?providerType=&providerId=&month=&year=` | Yes | Get provider's transactions |
| GET | `/GetProviderSettlement?providerType=&providerId=&year=&month=` | Yes | Get provider's settlement |

### Admin Endpoints

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| PUT | `/ApproveOffer/{id}` | Yes | Approve pending offer |
| PUT | `/RejectOffer/{id}` | Yes | Reject with note |
| PUT | `/UpdateOffer/{id}` | Yes | Update offer details |
| PUT | `/DeactivateOffer/{id}` | Yes | Deactivate offer |
| GET | `/GetPendingOffers` | Yes | Get all pending approvals |
| GET | `/GetSettlements?status=&month=&year=` | Yes | Get all settlements |
| GET | `/GetSettlementSummary?month=&year=` | Yes | Settlement dashboard summary |
| PUT | `/UpdateSettlementStatus/{id}` | Yes | Mark as Invoiced/Paid/Disputed |
| POST | `/GenerateSettlement` | Yes | Generate monthly settlements |

### Key Request/Response Examples

**POST /ProposeOffer**
```json
{
  "title": "Free Coffee",
  "titleAr": null,
  "description": "Redeem 100 points for a free coffee",
  "descriptionAr": null,
  "providerType": "ServiceProvider",
  "providerId": 5,
  "pointsCost": 100,
  "monetaryValue": 3.000,
  "currencyCode": "KWD",
  "maxUsesPerUser": 3,
  "maxTotalUses": 500,
  "offerCodeExpiryMinutes": 30,
  "imageUrl": null,
  "validFrom": "2025-01-01T00:00:00Z",
  "validTo": "2025-12-31T23:59:59Z"
}
// Response: 12 (offer ID)
```

**POST /provider/CreateTransaction**
```json
{
  "offerId": 12
}
// Response:
{
  "offerCode": "CBL-7X9K2M",
  "expiresAt": "2025-06-15T14:30:00Z",
  "pointsCost": 100,
  "monetaryValue": 3.000,
  "currencyCode": "KWD"
}
```

**POST /ScanOfferCode?code=CBL-7X9K2M**
```json
// Response:
{
  "offerTitle": "Free Coffee",
  "pointsDeducted": 100,
  "monetaryValue": 3.000,
  "currencyCode": "KWD"
}
```

---

## 3. DTOs & Data Models

### OfferDto
```
id, title, titleAr, description, descriptionAr,
providerType, providerId, providerName,
proposedByUserId, proposedByUserName,
approvalStatus,
pointsCost, monetaryValue, currencyCode,
maxUsesPerUser, maxTotalUses, currentTotalUses,
offerCodeExpiryMinutes, imageUrl,
validFrom, validTo,
isActive, createdAt
```

### OfferTransactionDto
```
id, providerOfferId, offerTitle,
userId, userName, offerCode, status,
pointsDeducted, monetaryValue, currencyCode,
providerType, providerId, confirmedByUserId,
codeExpiresAt, completedAt, createdAt
```

### ProviderSettlementDto
```
id, providerType, providerId,
providerOwnerId, providerOwnerName,
periodYear, periodMonth,
totalTransactions, totalTransactionAmount, totalCommissionAmount,
totalPointsAwarded, totalPointsDeducted,
settlementStatus,
invoicedAt, paidAt, paidAmount, adminNote, createdAt
```

### SettlementSummaryDto
```
totalSettlements,
totalTransactionAmount, totalCommissionAmount,
totalPointsAwarded, totalPointsDeducted,
pendingCount, invoicedCount, paidCount, disputedCount
```

---

## 4. Loyalty Point Service

### DeductPointsFromOfferAsync (New - for Offers)
Called when user scans offer QR code. Located in `Infrastructrue/Loyalty/LoyaltyPointService.cs`.

1. Validates `pointsToDeduct > 0`
2. Gets user's loyalty wallet (`UserLoyaltyAccount`)
3. Checks `CurrentBalance >= pointsToDeduct` - throws `DataValidationException` if insufficient
4. Debits wallet: `CurrentBalance -= pointsToDeduct`, `TotalPointsRedeemed += pointsToDeduct`
5. Creates `LoyaltyPointTransaction` with `TransactionType = Redeem`, `Points = -pointsToDeduct`
6. Uses action code `REDEEM_OFFER`

### AwardPointsFromOfferAsync (Kept - for Partners)
Still used by partner transactions to award points to users. Uses action code `USE_OFFER`, `TransactionType = Earn`.

---

## 5. Background Jobs (Hangfire)

### Architecture
- **Interface:** `Application/Common/Interfaces/IBackgroundJobService.cs`
- **Implementation:** `Infrastructrue/BackgroundJobs/BackgroundJobService.cs`
- **DI Registration:** `Infrastructrue/DependencyInjection.cs` (Scoped lifetime)
- **Job Registration:** `WebApi/Program.cs` (after `app.MapHangfireDashboard`)
- **Dashboard URL:** `/Cable-Jobs-Dashboard`

Hangfire resolves `IBackgroundJobService` from DI per job execution, creating a fresh scope with its own `IApplicationDbContext`.

### All 11 Recurring Jobs

#### Transaction Code Expiry

| Job ID | Cron | Frequency | Description |
|--------|------|-----------|-------------|
| `expire-offer-transaction-codes` | `*/5 * * * *` | Every 5 min | Marks `Initiated` offer transactions as `Expired` when `CodeExpiresAt < now` |
| `expire-partner-transaction-codes` | `*/5 * * * *` | Every 5 min | Marks `Initiated` partner transactions as `Expired` when `CodeExpiresAt < now` |

#### Settlement

| Job ID | Cron | Frequency | Description |
|--------|------|-----------|-------------|
| `generate-monthly-settlements` | `30 0 1 * *` | 1st of month 00:30 UTC | Generates settlements for previous month (offer + partner transactions) |

#### Security Cleanup (Critical)

| Job ID | Cron | Frequency | Description |
|--------|------|-----------|-------------|
| `cleanup-expired-phone-verifications` | `0 2 * * *` | Daily 02:00 UTC | Soft-deletes OTP verification records expired > 24 hours |
| `cleanup-expired-password-resets` | `0 2 * * *` | Daily 02:00 UTC | Hard-deletes password reset codes expired > 24 hours |
| `cleanup-expired-otp-rate-limits` | `0 2 * * *` | Daily 02:00 UTC | Unblocks expired numbers, deletes stale rate limit records > 24 hours |

#### Business Expiry (Important)

| Job ID | Cron | Frequency | Description |
|--------|------|-----------|-------------|
| `deactivate-expired-offers` | `*/30 * * * *` | Every 30 min | Sets `IsActive = false` for offers past `ValidTo` |
| `deactivate-expired-shared-links` | `*/30 * * * *` | Every 30 min | Sets `IsActive = false` for links past `ExpiresAt` or maxed `CurrentUsage` |
| `end-expired-loyalty-seasons` | `0 0 * * *` | Daily midnight UTC | Sets `IsActive = false` for seasons past `EndDate` |
| `expire-loyalty-points` | `0 1 * * *` | Daily 01:00 UTC | Expires point transactions past `ExpiresAt`, deducts from user balance, creates audit trail |
| `deactivate-expired-rewards` | `0 0 * * *` | Daily midnight UTC | Sets `IsActive = false` for rewards past `ValidTo` or maxed `CurrentRedemptions` |

---

## 6. Settlement System

### How It Works
`GenerateMonthlySettlementsAsync(year, month)` processes two types of completed transactions:

**Offer Transactions** (Points Deducted, Fixed Monetary Value):
- Groups completed `OfferTransaction` records by `(ProviderType, ProviderId)`
- Sums `MonetaryValue` (what Cable pays provider) and `PointsDeducted`
- Sets `TotalPointsAwarded = 0` (offers don't award points)

**Partner Transactions** (Points Awarded, Commission-Based):
- Groups completed `PartnerTransaction` records by `(ProviderType, ProviderId)`
- Sums `TransactionAmount`, `CommissionAmount`, `PointsAwarded`
- Sets `TotalPointsDeducted = 0` (partners don't deduct points)

**Upsert Logic:**
- If a `ProviderSettlement` already exists for `(ProviderType, ProviderId, Year, Month)`, accumulates totals
- If new, creates settlement with `SettlementStatus = Pending`

**Settlement Lifecycle:** `Pending` -> `Invoiced` -> `Paid` (or `Disputed`)

The `POST /api/offers/GenerateSettlement` endpoint delegates to the same service method, so admin can also trigger it manually.

---

## 7. SQL Migration (Already Executed)

```sql
-- ProviderOffer: ADD new columns
ALTER TABLE [dbo].[ProviderOffer] ADD [PointsCost] INT NOT NULL DEFAULT 0;
ALTER TABLE [dbo].[ProviderOffer] ADD [MonetaryValue] DECIMAL(18,3) NOT NULL DEFAULT 0;
ALTER TABLE [dbo].[ProviderOffer] ADD [CurrencyCode] NVARCHAR(10) NOT NULL DEFAULT 'KWD';

-- ProviderOffer: DROP old FK and columns
ALTER TABLE [dbo].[ProviderOffer] DROP CONSTRAINT [FK_ProviderOffer_PointsConversionRate];
ALTER TABLE [dbo].[ProviderOffer] DROP COLUMN [CommissionPercentage], [PointsRewardPercentage],
    [PointsConversionRateId], [MinTransactionAmount], [MaxTransactionAmount];

-- OfferTransaction: ADD new columns
ALTER TABLE [dbo].[OfferTransaction] ADD [PointsDeducted] INT NOT NULL DEFAULT 0;
ALTER TABLE [dbo].[OfferTransaction] ADD [MonetaryValue] DECIMAL(18,3) NOT NULL DEFAULT 0;

-- OfferTransaction: DROP old columns
ALTER TABLE [dbo].[OfferTransaction] DROP COLUMN [TransactionAmount], [CommissionPercentage],
    [CommissionAmount], [PointsRewardPercentage], [PointsConversionRate],
    [PointsEligibleAmount], [PointsAwarded];

-- OfferTransaction: Update CurrencyCode to required with default
ALTER TABLE [dbo].[OfferTransaction] ALTER COLUMN [CurrencyCode] NVARCHAR(10) NOT NULL;
ALTER TABLE [dbo].[OfferTransaction] ADD DEFAULT 'KWD' FOR [CurrencyCode];

-- ProviderSettlement: ADD new field
ALTER TABLE [dbo].[ProviderSettlement] ADD [TotalPointsDeducted] INT NOT NULL DEFAULT 0;

-- Seed REDEEM_OFFER action for loyalty system
INSERT INTO [dbo].[LoyaltyPointAction]
    ([ActionCode],[Name],[Description],[Points],[MaxPerDay],[MaxPerLifetime],[IsActive],[CreatedAt],[IsDeleted])
VALUES
    ('REDEEM_OFFER','Redeem an offer','Points deducted when redeeming an offer',0,NULL,NULL,1,GETUTCDATE(),0);
```

---

## 8. Files Changed Summary

### Offers Refactoring (Earn -> Spend Points)

| File | Action | What Changed |
|------|--------|-------------|
| `Domain/Enitites/ProviderOffer.cs` | Modified | Removed 5 old fields + ConversionRate nav. Added PointsCost, MonetaryValue, CurrencyCode |
| `Domain/Enitites/OfferTransaction.cs` | Modified | Removed 7 old fields. Added PointsDeducted, MonetaryValue, CurrencyCode |
| `Domain/Enitites/ProviderSettlement.cs` | Modified | Added TotalPointsDeducted |
| `Domain/Enitites/PointsConversionRate.cs` | Modified | Removed Offers navigation collection |
| `Infrastructrue/Persistence/Configurations/ProviderOfferConfiguration.cs` | Modified | New field configs, removed old FK |
| `Infrastructrue/Persistence/Configurations/OfferTransactionConfiguration.cs` | Modified | New field configs |
| `Infrastructrue/Persistence/Configurations/ProviderSettlementConfiguration.cs` | Modified | Added TotalPointsDeducted config |
| `Application/Common/Interfaces/ILoyaltyPointService.cs` | Modified | Added DeductPointsFromOfferAsync |
| `Infrastructrue/Loyalty/LoyaltyPointService.cs` | Modified | Added DeductPointsFromOfferAsync implementation |
| `Application/Offers/Commands/ProposeOffer/ProposeOfferCommand.cs` | Rewritten | New params: PointsCost, MonetaryValue, CurrencyCode |
| `Application/Offers/Commands/ProposeOffer/ProposeOfferCommandValidator.cs` | Rewritten | New validation rules |
| `Application/Offers/Commands/UpdateOffer/UpdateOfferCommand.cs` | Rewritten | Same field changes |
| `Application/Offers/Commands/InitiateOfferTransaction/InitiateOfferTransactionCommand.cs` | Rewritten | Simplified to just OfferId |
| `Application/Offers/Commands/ConfirmOfferTransaction/ConfirmOfferTransactionCommand.cs` | Rewritten | Calls DeductPointsFromOfferAsync |
| `Application/Offers/Queries/GetActiveOffers/OfferDto.cs` | Rewritten | New DTO fields |
| `Application/Offers/Queries/GetMyOfferTransactions/OfferTransactionDto.cs` | Rewritten | New DTO fields |
| `Application/Offers/Queries/GetSettlements/ProviderSettlementDto.cs` | Modified | Added TotalPointsDeducted |
| `Application/Offers/Queries/GetSettlementSummary/GetSettlementSummaryRequest.cs` | Modified | Added TotalPointsDeducted |
| All 6 offer query handlers | Modified | Updated DTO mappings |
| All 3 settlement query handlers | Modified | Updated DTO mappings |
| `WebApi/Requests/Offers/ProposeOfferRequest.cs` | Rewritten | New request fields |
| `WebApi/Requests/Offers/UpdateOfferRequest.cs` | Rewritten | New request fields |
| `WebApi/Requests/Offers/ConfirmTransactionRequest.cs` | Rewritten | Simplified to just OfferId |
| `WebApi/Routes/OfferRoutes.cs` | Modified | Updated route mappings and descriptions |

### Background Jobs Implementation

| File | Action | What Changed |
|------|--------|-------------|
| `Application/Common/Interfaces/IBackgroundJobService.cs` | Created | 11-method interface |
| `Infrastructrue/BackgroundJobs/BackgroundJobService.cs` | Created | Full implementation with logging |
| `Infrastructrue/DependencyInjection.cs` | Modified | Added RegisterBackgroundJobServices (Scoped) |
| `WebApi/Program.cs` | Modified | Registered 11 Hangfire recurring jobs |
| `Application/Offers/Commands/GenerateSettlement/GenerateSettlementCommand.cs` | Modified | Thin delegator to IBackgroundJobService |
| `Application/Offers/Commands/ExpireOfferTransactions/` | Deleted | Logic moved to BackgroundJobService |
| `Application/Partners/Commands/ExpirePartnerTransactions/` | Deleted | Logic moved to BackgroundJobService |
