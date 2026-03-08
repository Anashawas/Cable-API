# User App Features - Flutter Implementation Guide

## Overview
This document covers all backend API endpoints for the **User App** in the Cable EV Charging Station Management System. It includes:
- **Service Providers** (user-facing): Browse, search, rate, favorite
- **Service Categories**: Browse categories
- **Offers & Transactions**: Browse offers, generate codes, track transactions
- **Loyalty System**: Points, tiers, rewards, redemptions, seasons, leaderboard

**Base URL**: `{serverUrl}/api`

**Authentication**: JWT Bearer token in `Authorization` header. Endpoints marked with `[Auth]` require authentication.

---

# PART 0: Service Providers & Categories (User-Facing)

These endpoints allow users to discover and interact with service providers. Rating and favoriting also awards loyalty points.

---

## 0.1 Service Categories

**Base Path**: `/api/service-categories`

### Get All Service Categories
- **Method**: `GET`
- **Path**: `/api/service-categories/GetAllServiceCategories`
- **Auth**: No
- **Response**: `List<ServiceCategoryDto>`

```json
// ServiceCategoryDto
{
  "id": 1,
  "name": "EV Charging",
  "nameAr": "شحن المركبات الكهربائية",
  "description": "Electric vehicle charging services",
  "iconUrl": "https://example.com/icon.png",
  "sortOrder": 1,
  "isActive": true
}
```

---

## 0.2 Browse Service Providers

**Base Path**: `/api/service-providers`

### Get All Service Providers
- **Method**: `GET`
- **Path**: `/api/service-providers/GetAllServiceProviders`
- **Auth**: No
- **Query Params**: `categoryId` (int, optional) - Filter by category
- **Response**: `List<ServiceProviderDto>`

```json
// ServiceProviderDto
{
  "id": 1,
  "name": "EV Fast Charge Station",
  "ownerId": 5,
  "ownerName": "Ahmed",
  "serviceCategoryId": 1,
  "serviceCategoryName": "EV Charging",
  "serviceCategoryNameAr": "شحن المركبات الكهربائية",
  "statusId": 1,
  "statusName": "Active",
  "description": "Fast charging station with multiple ports",
  "phone": "+965XXXXXXXX",
  "address": "Block 5, Street 10",
  "countryName": "Kuwait",
  "cityName": "Kuwait City",
  "latitude": 29.3759,
  "longitude": 47.9774,
  "price": 5.0,
  "priceDescription": "5 KWD per session",
  "fromTime": "08:00",
  "toTime": "22:00",
  "methodPayment": "Cash, Card",
  "visitorsCount": 150,
  "isVerified": true,
  "hasOffer": true,
  "offerDescription": "20% off first charge",
  "service": "EV Charging",
  "icon": "https://server/CableServiceProvider/icon.jpg",
  "whatsAppNumber": "+965XXXXXXXX",
  "websiteUrl": "https://example.com",
  "avgRating": 4.5,
  "rateCount": 23,
  "images": [
    "https://server/CableServiceProvider/img1.jpg",
    "https://server/CableServiceProvider/img2.jpg"
  ],
  "createdAt": "2025-01-15T10:30:00Z"
}
```

### Get Service Provider By ID
- **Method**: `GET`
- **Path**: `/api/service-providers/GetServiceProviderById/{id}`
- **Auth**: No
- **Path Params**: `id` (int, required)
- **Response**: `ServiceProviderDto`
- **Note**: Increments the visitor count each time this endpoint is called.

### Get Service Providers By Category
- **Method**: `GET`
- **Path**: `/api/service-providers/GetByCategory/{categoryId}`
- **Auth**: No
- **Path Params**: `categoryId` (int, required)
- **Response**: `List<ServiceProviderDto>`

### Get Nearby Service Providers
- **Method**: `GET`
- **Path**: `/api/service-providers/GetNearby`
- **Auth**: No
- **Query Params**:
  - `latitude` (double, required)
  - `longitude` (double, required)
  - `radiusKm` (double, optional, default: 10)
- **Response**: `List<ServiceProviderDto>`

---

## 0.3 Service Provider Ratings

### Get Ratings
- **Method**: `GET`
- **Path**: `/api/service-providers/GetRatings/{serviceProviderId}`
- **Auth**: No
- **Path Params**: `serviceProviderId` (int, required)
- **Response**: `List<ServiceProviderRatingDto>`

```json
// ServiceProviderRatingDto
{
  "id": 1,
  "userId": 5,
  "userName": "Ahmed",
  "rating": 5,
  "avgRating": 4.5,
  "comment": "Great service!",
  "createdAt": "2025-01-15T10:30:00Z"
}
```

### Rate Service Provider
- **Method**: `POST`
- **Path**: `/api/service-providers/RateServiceProvider/{serviceProviderId}`
- **Auth**: `[Auth]`
- **Path Params**: `serviceProviderId` (int, required)
- **Request Body**:

```json
{
  "rating": 5,
  "comment": "Excellent charging station!"
}
```

- **Response**: `int` (rating ID)
- **Loyalty Points**: Awards points automatically (action code: `RATE_SERVICE`, 10 points base).

---

## 0.4 Service Provider Favorites

### Get My Favorites
- **Method**: `GET`
- **Path**: `/api/service-providers/GetMyFavorites`
- **Auth**: `[Auth]`
- **Response**: `List<ServiceProviderDto>`

### Add to Favorites
- **Method**: `POST`
- **Path**: `/api/service-providers/AddToFavorites/{serviceProviderId}`
- **Auth**: `[Auth]`
- **Path Params**: `serviceProviderId` (int, required)
- **Response**: `int` (favorite record ID)
- **Loyalty Points**: Awards points automatically (action code: `ADD_FAVORITE_SERVICE`, 5 points base).

### Remove from Favorites
- **Method**: `DELETE`
- **Path**: `/api/service-providers/RemoveFromFavorites/{serviceProviderId}`
- **Auth**: `[Auth]`
- **Path Params**: `serviceProviderId` (int, required)
- **Response**: `200 OK`

---

## 0.5 Service Provider Attachments

### Get Attachments
- **Method**: `GET`
- **Path**: `/api/service-providers/GetAttachments/{id}`
- **Auth**: No
- **Path Params**: `id` (int, required) - Service provider ID
- **Response**: `List<UploadFile>`

```json
// UploadFile
{
  "fileName": "abc123.jpg",
  "contentType": "image/jpeg",
  "filePath": "https://server/CableServiceProvider/abc123.jpg",
  "fileExtension": ".jpg",
  "fileSize": 245000
}
```

---

# PART 1: Offers & Transactions System

The offers system allows providers (Charging Points or Service Providers) to create special offers for users. Users generate offer codes, present them to providers, and providers confirm the transaction. The system tracks commissions and settlements.

**Base Path**: `/api/offers`

---

## 1. Conversion Rates

Conversion rates define how currency amounts convert to loyalty points. Used when creating offers.

**Base Path**: `/api/conversion-rates`

### 1.1 Get All Conversion Rates
- **Method**: `GET`
- **Path**: `/api/conversion-rates/GetAllConversionRates`
- **Auth**: `[Auth]`
- **Response**: `List<ConversionRateDto>`

```json
// ConversionRateDto
{
  "id": 1,
  "name": "KWD Standard",
  "currencyCode": "KWD",
  "pointsPerUnit": 10.0,
  "isDefault": true,
  "isActive": true
}
```

### 1.2 Create Conversion Rate (Admin)
- **Method**: `POST`
- **Path**: `/api/conversion-rates/CreateConversionRate`
- **Auth**: `[Auth]` (Admin)
- **Request Body**:

```json
{
  "name": "KWD Standard",
  "currencyCode": "KWD",
  "pointsPerUnit": 10.0,
  "isDefault": true,
  "isActive": true
}
```

- **Response**: `int` (created rate ID)

### 1.3 Update Conversion Rate (Admin)
- **Method**: `PUT`
- **Path**: `/api/conversion-rates/UpdateConversionRate/{id}`
- **Auth**: `[Auth]` (Admin)
- **Path Params**: `id` (int, required)
- **Request Body**: Same as Create
- **Response**: `200 OK`

---

## 2. User Offer Endpoints

### 2.1 Get Active Offers
- **Method**: `GET`
- **Path**: `/api/offers/GetActiveOffers`
- **Auth**: No
- **Query Params**:
  - `providerType` (string, optional) - `"ChargingPoint"` or `"ServiceProvider"`
  - `providerId` (int, optional) - Specific provider ID
  - `categoryId` (int, optional) - Service category filter
- **Response**: `List<OfferDto>`

```json
// OfferDto
{
  "id": 1,
  "title": "20% Off First Charge",
  "titleAr": "خصم 20% على أول شحنة",
  "description": "Get 20% discount on your first charging session",
  "descriptionAr": "احصل على خصم 20% على أول جلسة شحن",
  "providerType": "ChargingPoint",
  "providerId": 5,
  "providerName": "Downtown Charging Hub",
  "proposedByUserId": 10,
  "proposedByUserName": "Ahmed",
  "approvalStatus": 2,
  "commissionPercentage": 10.0,
  "pointsRewardPercentage": 5.0,
  "pointsConversionRateId": 1,
  "minTransactionAmount": 1.0,
  "maxTransactionAmount": 100.0,
  "maxUsesPerUser": 1,
  "maxTotalUses": 100,
  "currentTotalUses": 23,
  "offerCodeExpiryMinutes": 30,
  "imageUrl": "https://example.com/offer.jpg",
  "validFrom": "2025-01-01T00:00:00Z",
  "validTo": "2025-12-31T23:59:59Z",
  "isActive": true,
  "createdAt": "2025-01-01T10:00:00Z"
}
```

**Approval Status Enum**:
- `1` = Pending
- `2` = Approved
- `3` = Rejected

### 2.2 Get Offer By ID
- **Method**: `GET`
- **Path**: `/api/offers/GetOfferById/{id}`
- **Auth**: No
- **Path Params**: `id` (int, required)
- **Response**: `OfferDto`

### 2.3 Use Offer (Generate Code)
- **Method**: `POST`
- **Path**: `/api/offers/UseOffer/{offerId}`
- **Auth**: `[Auth]`
- **Path Params**: `offerId` (int, required)
- **Response**: `OfferTransactionDto`
- **Note**: Generates a unique offer code for the user. The code has an expiry time defined by `offerCodeExpiryMinutes`.

```json
// OfferTransactionDto
{
  "id": 1,
  "providerOfferId": 1,
  "offerTitle": "20% Off First Charge",
  "userId": 5,
  "userName": "Ahmed",
  "offerCode": "ABC123",
  "status": 1,
  "transactionAmount": null,
  "currencyCode": null,
  "commissionPercentage": 10.0,
  "commissionAmount": null,
  "pointsRewardPercentage": 5.0,
  "pointsConversionRate": 10.0,
  "pointsEligibleAmount": null,
  "pointsAwarded": null,
  "providerType": "ChargingPoint",
  "providerId": 5,
  "confirmedByUserId": null,
  "codeExpiresAt": "2025-06-15T11:00:00Z",
  "completedAt": null,
  "createdAt": "2025-06-15T10:30:00Z"
}
```

**Transaction Status Enum**:
- `1` = CodeGenerated
- `2` = Confirmed
- `3` = Cancelled
- `4` = Expired

### 2.4 Get My Transactions
- **Method**: `GET`
- **Path**: `/api/offers/GetMyTransactions`
- **Auth**: `[Auth]`
- **Query Params**: `status` (int, optional) - Filter by transaction status
- **Response**: `List<OfferTransactionDto>`

### 2.5 Cancel Transaction
- **Method**: `PUT`
- **Path**: `/api/offers/CancelTransaction/{transactionId}`
- **Auth**: `[Auth]`
- **Path Params**: `transactionId` (int, required)
- **Response**: `200 OK`
- **Note**: Can only cancel transactions with status `CodeGenerated`.

---

## 3. Provider Offer Endpoints (Station Partner App)

### 3.1 Propose Offer
- **Method**: `POST`
- **Path**: `/api/offers/provider/ProposeOffer`
- **Auth**: `[Auth]`
- **Request Body**:

```json
{
  "title": "20% Off First Charge",
  "titleAr": "خصم 20% على أول شحنة",
  "description": "Get 20% discount on your first charging session",
  "descriptionAr": "احصل على خصم 20% على أول جلسة شحن",
  "providerType": "ChargingPoint",
  "providerId": 5,
  "commissionPercentage": 10.0,
  "pointsRewardPercentage": 5.0,
  "pointsConversionRateId": 1,
  "minTransactionAmount": 1.0,
  "maxTransactionAmount": 100.0,
  "maxUsesPerUser": 1,
  "maxTotalUses": 100,
  "offerCodeExpiryMinutes": 30,
  "imageUrl": "https://example.com/offer.jpg",
  "validFrom": "2025-01-01T00:00:00Z",
  "validTo": "2025-12-31T23:59:59Z"
}
```

- **Response**: `int` (created offer ID)
- **Note**: Offer starts with `Pending` approval status. Admin must approve it before it becomes active.

**ProviderType Values**:
- `"ChargingPoint"` - For charging point offers
- `"ServiceProvider"` - For service provider offers

### 3.2 Get Offers for Provider
- **Method**: `GET`
- **Path**: `/api/offers/provider/GetOffersForProvider`
- **Auth**: `[Auth]`
- **Query Params**:
  - `providerType` (string, required) - `"ChargingPoint"` or `"ServiceProvider"`
  - `providerId` (int, required)
- **Response**: `List<OfferDto>`

### 3.3 Lookup Transaction (by offer code)
- **Method**: `GET`
- **Path**: `/api/offers/provider/LookupTransaction/{offerCode}`
- **Auth**: `[Auth]`
- **Path Params**: `offerCode` (string, required) - The code presented by the user
- **Response**: `OfferTransactionDto`
- **Note**: Provider scans/enters the user's offer code to look up the transaction details.

### 3.4 Confirm Transaction
- **Method**: `PUT`
- **Path**: `/api/offers/provider/ConfirmTransaction/{transactionId}`
- **Auth**: `[Auth]`
- **Path Params**: `transactionId` (int, required)
- **Request Body**:

```json
{
  "offerCode": "ABC123",
  "transactionAmount": 25.500,
  "currencyCode": "KWD"
}
```

- **Response**: `200 OK`
- **Note**: This endpoint:
  1. Validates the offer code matches the transaction
  2. Validates the amount is within offer min/max limits
  3. Calculates commission amount
  4. Calculates loyalty points to award (based on conversion rate and reward percentage)
  5. Awards loyalty points to the user automatically
  6. Marks the transaction as confirmed

### 3.5 Get Provider Transactions
- **Method**: `GET`
- **Path**: `/api/offers/provider/GetProviderTransactions`
- **Auth**: `[Auth]`
- **Query Params**:
  - `providerType` (string, required)
  - `providerId` (int, required)
  - `status` (int, optional)
- **Response**: `List<OfferTransactionDto>`

### 3.6 Get Provider Settlement
- **Method**: `GET`
- **Path**: `/api/offers/provider/GetProviderSettlement`
- **Auth**: `[Auth]`
- **Query Params**:
  - `providerType` (string, required)
  - `providerId` (int, required)
  - `year` (int, optional)
  - `month` (int, optional)
- **Response**: `List<ProviderSettlementDto>`

```json
// ProviderSettlementDto
{
  "id": 1,
  "providerType": "ChargingPoint",
  "providerId": 5,
  "providerOwnerId": 10,
  "providerOwnerName": "Ahmed",
  "periodYear": 2025,
  "periodMonth": 6,
  "totalTransactions": 50,
  "totalTransactionAmount": 500.000,
  "totalCommissionAmount": 50.000,
  "totalPointsAwarded": 2500,
  "settlementStatus": 1,
  "invoicedAt": null,
  "paidAt": null,
  "paidAmount": null,
  "adminNote": null,
  "createdAt": "2025-07-01T00:00:00Z"
}
```

**Settlement Status Enum**:
- `1` = Pending
- `2` = Invoiced
- `3` = Paid
- `4` = Disputed

---

## 4. Admin Offer Endpoints

### 4.1 Approve Offer
- **Method**: `PUT`
- **Path**: `/api/offers/admin/ApproveOffer/{id}`
- **Auth**: `[Auth]` (Admin)
- **Path Params**: `id` (int, required) - Offer ID
- **Response**: `200 OK`

### 4.2 Reject Offer
- **Method**: `PUT`
- **Path**: `/api/offers/admin/RejectOffer/{id}`
- **Auth**: `[Auth]` (Admin)
- **Path Params**: `id` (int, required)
- **Request Body**:

```json
{
  "note": "Reason for rejection"
}
```

- **Response**: `200 OK`

### 4.3 Update Offer (Admin)
- **Method**: `PUT`
- **Path**: `/api/offers/admin/UpdateOffer/{id}`
- **Auth**: `[Auth]` (Admin)
- **Path Params**: `id` (int, required)
- **Request Body**:

```json
{
  "title": "Updated Offer Title",
  "titleAr": "عنوان العرض المحدث",
  "description": "Updated description",
  "descriptionAr": "وصف محدث",
  "providerType": "ChargingPoint",
  "providerId": 5,
  "commissionPercentage": 10.0,
  "pointsRewardPercentage": 5.0,
  "pointsConversionRateId": 1,
  "minTransactionAmount": 1.0,
  "maxTransactionAmount": 100.0,
  "maxUsesPerUser": 1,
  "maxTotalUses": 100,
  "offerCodeExpiryMinutes": 30,
  "imageUrl": "https://example.com/offer.jpg",
  "validFrom": "2025-01-01T00:00:00Z",
  "validTo": "2025-12-31T23:59:59Z",
  "isActive": true
}
```

- **Response**: `200 OK`

### 4.4 Deactivate Offer
- **Method**: `PUT`
- **Path**: `/api/offers/admin/DeactivateOffer/{id}`
- **Auth**: `[Auth]` (Admin)
- **Path Params**: `id` (int, required)
- **Response**: `200 OK`

### 4.5 Get Pending Offers
- **Method**: `GET`
- **Path**: `/api/offers/admin/GetPendingOffers`
- **Auth**: `[Auth]` (Admin)
- **Response**: `List<OfferDto>`
- **Note**: Returns all offers with `Pending` approval status.

### 4.6 Get All Settlements
- **Method**: `GET`
- **Path**: `/api/offers/admin/GetSettlements`
- **Auth**: `[Auth]` (Admin)
- **Query Params**:
  - `providerType` (string, optional)
  - `providerId` (int, optional)
  - `status` (int, optional)
  - `year` (int, optional)
  - `month` (int, optional)
- **Response**: `List<ProviderSettlementDto>`

### 4.7 Get Settlement Summary
- **Method**: `GET`
- **Path**: `/api/offers/admin/GetSettlementSummary`
- **Auth**: `[Auth]` (Admin)
- **Query Params**: `year` (int, optional), `month` (int, optional)
- **Response**: `SettlementSummaryDto`

```json
// SettlementSummaryDto
{
  "totalSettlements": 25,
  "totalTransactionAmount": 12500.000,
  "totalCommissionAmount": 1250.000,
  "totalPointsAwarded": 62500,
  "pendingCount": 10,
  "invoicedCount": 8,
  "paidCount": 5,
  "disputedCount": 2
}
```

### 4.8 Update Settlement Status
- **Method**: `PUT`
- **Path**: `/api/offers/admin/UpdateSettlementStatus/{id}`
- **Auth**: `[Auth]` (Admin)
- **Path Params**: `id` (int, required) - Settlement ID
- **Request Body**:

```json
{
  "status": 3,
  "paidAmount": 450.000,
  "note": "Payment processed via bank transfer"
}
```

- **Response**: `200 OK`

### 4.9 Generate Settlement
- **Method**: `POST`
- **Path**: `/api/offers/admin/GenerateSettlement`
- **Auth**: `[Auth]` (Admin)
- **Request Body**:

```json
{
  "year": 2025,
  "month": 6
}
```

- **Response**: `int` (number of settlements generated)
- **Note**: Generates settlement records for all providers with confirmed transactions in the specified month.

---

# PART 2: Loyalty System

The loyalty system rewards users with points for various actions. Points can be redeemed for rewards. The system uses seasonal tiers where user tier levels reset each season, but the points wallet balance persists.

**Base Path**: `/api/loyalty`

---

## 5. Loyalty System Concepts

### Tier System
| Tier | Min Points | Multiplier | Season Bonus |
|------|-----------|------------|-------------|
| Bronze | 0 | 1.0x | 0 |
| Silver | 500 | 1.5x | 100 |
| Gold | 2000 | 2.0x | 500 |
| Platinum | 5000 | 3.0x | 1500 |

- **Multiplier**: Applied to points earned. E.g., if an action gives 10 points and user is Gold tier, they earn 20 points.
- **Season Bonus**: Awarded when a season ends, based on the user's tier at that time.
- **Tier resets** each season. The points wallet balance does NOT reset.

### Point Actions (Pre-configured)
| Action Code | Points | Description |
|------------|--------|-------------|
| ADD_FAVORITE | 5 | Add charging point to favorites |
| RATE_STATION | 10 | Rate a charging point |
| FIRST_CHARGE | 50 | Complete first charge (1/lifetime) |
| DAILY_LOGIN | 2 | Daily login (1/day) |
| REFER_FRIEND | 100 | Refer a friend (10/lifetime) |
| COMPLETE_PROFILE | 25 | Complete profile (1/lifetime) |
| ADD_VEHICLE | 15 | Register a vehicle (5/lifetime) |
| SHARE_STATION | 5 | Share a station (3/day) |
| ADD_FAVORITE_SERVICE | 5 | Add service provider to favorites |
| RATE_SERVICE | 10 | Rate a service provider |
| USE_SERVICE | 20 | Use a service (5/day) |
| WRITE_REVIEW | 15 | Write a review (3/day) |

### Points From Offers (Dynamic)
When a user completes an offer transaction, loyalty points are calculated dynamically:
```
pointsEligibleAmount = transactionAmount - commissionAmount
pointsAwarded = floor(pointsEligibleAmount * pointsConversionRate * pointsRewardPercentage / 100)
```
Then the tier multiplier is applied on top.

---

## 6. User Loyalty Endpoints

### 6.1 Get My Loyalty Account
- **Method**: `GET`
- **Path**: `/api/loyalty/GetMyLoyaltyAccount`
- **Auth**: `[Auth]`
- **Response**: `LoyaltyAccountDto`

```json
// LoyaltyAccountDto
{
  "totalPointsEarned": 1250,
  "totalPointsRedeemed": 200,
  "currentBalance": 1050,
  "currentTierName": "Silver",
  "currentMultiplier": 1.5,
  "seasonPointsEarned": 750,
  "seasonName": "Summer 2025"
}
```

- **Note**: If no account exists yet, returns empty/default values. Account is auto-created when points are first earned.

### 6.2 Get My Points History
- **Method**: `GET`
- **Path**: `/api/loyalty/GetMyPointsHistory`
- **Auth**: `[Auth]`
- **Query Params**:
  - `page` (int, optional, default: 1)
  - `pageSize` (int, optional, default: 20)
  - `seasonId` (int, optional) - Filter by specific season
- **Response**: `List<PointsHistoryDto>`

```json
// PointsHistoryDto
{
  "id": 1,
  "transactionType": 1,
  "points": 15,
  "balanceAfter": 1050,
  "referenceType": "ChargingPoint",
  "referenceId": 5,
  "note": "Rated charging point",
  "actionName": "Rate Station",
  "createdAt": "2025-06-15T10:30:00Z"
}
```

**TransactionType Enum**:
- `1` = Earn
- `2` = Redeem
- `3` = Expired
- `4` = AdminAdjust
- `5` = SeasonBonus

### 6.3 Get Available Rewards
- **Method**: `GET`
- **Path**: `/api/loyalty/GetAvailableRewards`
- **Auth**: `[Auth]`
- **Query Params**:
  - `providerType` (string, optional)
  - `providerId` (int, optional)
  - `categoryId` (int, optional)
- **Response**: `List<RewardDto>`

```json
// RewardDto
{
  "id": 1,
  "name": "Free Charging Session",
  "description": "Get one free charging session at any partner station",
  "pointsCost": 500,
  "rewardType": 2,
  "rewardValue": "1 session",
  "providerType": "ChargingPoint",
  "providerId": 5,
  "serviceCategoryId": null,
  "maxRedemptions": 100,
  "currentRedemptions": 23,
  "imageUrl": "https://example.com/reward.jpg",
  "validFrom": "2025-01-01T00:00:00Z",
  "validTo": "2025-12-31T23:59:59Z"
}
```

**RewardType Enum**:
- `1` = Discount
- `2` = FreeCharge
- `3` = FreeService
- `4` = PriorityAccess
- `5` = Badge

### 6.4 Get Rewards for Provider
- **Method**: `GET`
- **Path**: `/api/loyalty/GetRewardsForProvider/{providerType}/{providerId}`
- **Auth**: `[Auth]`
- **Path Params**:
  - `providerType` (string, required) - `"ChargingPoint"` or `"ServiceProvider"`
  - `providerId` (int, required)
- **Response**: `List<RewardDto>`
- **Note**: Returns rewards linked to this specific provider PLUS rewards available for any provider (no provider filter).

### 6.5 Redeem Reward
- **Method**: `POST`
- **Path**: `/api/loyalty/RedeemReward/{rewardId}`
- **Auth**: `[Auth]`
- **Path Params**: `rewardId` (int, required)
- **Response**: `RedeemRewardResult`

```json
// RedeemRewardResult
{
  "redemptionId": 1,
  "redemptionCode": "RWD-A3F8K2"
}
```

- **Note**: Deducts points from user's balance. Generates a unique redemption code (format: `RWD-XXXXXX`). Fails if insufficient balance or reward max redemptions reached.

### 6.6 Get My Redemptions
- **Method**: `GET`
- **Path**: `/api/loyalty/GetMyRedemptions`
- **Auth**: `[Auth]`
- **Query Params**: `status` (int, optional) - Filter by redemption status
- **Response**: `List<RedemptionDto>`

```json
// RedemptionDto
{
  "id": 1,
  "rewardName": "Free Charging Session",
  "pointsSpent": 500,
  "status": 1,
  "redemptionCode": "RWD-A3F8K2",
  "providerType": "ChargingPoint",
  "providerId": 5,
  "redeemedAt": "2025-06-15T10:30:00Z",
  "fulfilledAt": null
}
```

**RedemptionStatus Enum**:
- `1` = Pending
- `2` = Fulfilled
- `3` = Cancelled

### 6.7 Get Current Season
- **Method**: `GET`
- **Path**: `/api/loyalty/GetCurrentSeason`
- **Auth**: `[Auth]`
- **Response**: `CurrentSeasonDto`

```json
// CurrentSeasonDto
{
  "seasonId": 1,
  "name": "Summer 2025",
  "description": "Summer loyalty season",
  "startDate": "2025-06-01T00:00:00Z",
  "endDate": "2025-08-31T23:59:59Z",
  "isActive": true,
  "seasonPointsEarned": 750,
  "tierName": "Silver",
  "multiplier": 1.5,
  "nextTierMinPoints": 2000,
  "nextTierName": "Gold"
}
```

- **Note**: Shows user's progress in the current active season, including how many points needed for the next tier.

### 6.8 Get Season History
- **Method**: `GET`
- **Path**: `/api/loyalty/GetSeasonHistory`
- **Auth**: `[Auth]`
- **Response**: `List<SeasonHistoryDto>`

```json
// SeasonHistoryDto
{
  "seasonId": 1,
  "name": "Spring 2025",
  "startDate": "2025-03-01T00:00:00Z",
  "endDate": "2025-05-31T23:59:59Z",
  "seasonPointsEarned": 1500,
  "tierName": "Gold",
  "bonusPointsAwarded": 500
}
```

### 6.9 Get Leaderboard
- **Method**: `GET`
- **Path**: `/api/loyalty/GetLeaderboard`
- **Auth**: `[Auth]`
- **Query Params**: `top` (int, optional, default: 10)
- **Response**: `List<LeaderboardEntryDto>`

```json
// LeaderboardEntryDto
{
  "rank": 1,
  "userId": 5,
  "userName": "Ahmed",
  "seasonPointsEarned": 3500,
  "tierName": "Gold"
}
```

---

## 7. Admin Loyalty Endpoints

### 7.1 Create Season
- **Method**: `POST`
- **Path**: `/api/loyalty/admin/CreateSeason`
- **Auth**: `[Auth]` (Admin)
- **Request Body**:

```json
{
  "name": "Summer 2025",
  "description": "Summer loyalty season with special bonuses",
  "startDate": "2025-06-01T00:00:00Z",
  "endDate": "2025-08-31T23:59:59Z",
  "activateImmediately": true
}
```

- **Response**: `int` (created season ID)
- **Validation**: Name required, EndDate must be after StartDate. Cannot activate if another season is already active.

### 7.2 End Season
- **Method**: `PUT`
- **Path**: `/api/loyalty/admin/EndSeason/{id}`
- **Auth**: `[Auth]` (Admin)
- **Path Params**: `id` (int, required) - Season ID
- **Response**: `EndSeasonResult`

```json
// EndSeasonResult
{
  "usersProcessed": 150,
  "totalBonusPointsAwarded": 45000
}
```

- **Note**: This endpoint:
  1. Deactivates the season
  2. Awards tier bonus points to ALL users based on their final tier
  3. Returns the total users processed and bonus points awarded

### 7.3 Admin Adjust Points
- **Method**: `POST`
- **Path**: `/api/loyalty/admin/AdjustPoints`
- **Auth**: `[Auth]` (Admin)
- **Request Body**:

```json
{
  "userId": 5,
  "points": 100,
  "note": "Compensation for system error"
}
```

- **Response**: `200 OK`
- **Note**: Points can be positive (add) or negative (deduct). Cannot deduct below zero balance.

### 7.4 Create Reward
- **Method**: `POST`
- **Path**: `/api/loyalty/admin/CreateReward`
- **Auth**: `[Auth]` (Admin)
- **Request Body**:

```json
{
  "name": "Free Charging Session",
  "description": "Get one free charging session",
  "pointsCost": 500,
  "rewardType": 2,
  "rewardValue": "1 session",
  "providerType": "ChargingPoint",
  "providerId": 5,
  "serviceCategoryId": null,
  "maxRedemptions": 100,
  "imageUrl": "https://example.com/reward.jpg",
  "validFrom": "2025-01-01T00:00:00Z",
  "validTo": "2025-12-31T23:59:59Z"
}
```

- **Response**: `int` (created reward ID)
- **Validation**: Name, PointsCost (>0), RewardType, ValidFrom required.
- **Note**: `providerType` and `providerId` are optional. If set, reward is linked to a specific provider. If null, reward is available for all.

### 7.5 Update Reward
- **Method**: `PUT`
- **Path**: `/api/loyalty/admin/UpdateReward/{id}`
- **Auth**: `[Auth]` (Admin)
- **Path Params**: `id` (int, required) - Reward ID
- **Request Body**: Same as Create + `isActive` (bool)
- **Response**: `200 OK`

### 7.6 Fulfill Redemption
- **Method**: `PUT`
- **Path**: `/api/loyalty/admin/FulfillRedemption/{id}`
- **Auth**: `[Auth]` (Admin)
- **Path Params**: `id` (int, required) - Redemption ID
- **Response**: `200 OK`
- **Note**: Changes status from Pending to Fulfilled. Sets the `fulfilledAt` timestamp.

### 7.7 Cancel Redemption
- **Method**: `PUT`
- **Path**: `/api/loyalty/admin/CancelRedemption/{id}`
- **Auth**: `[Auth]` (Admin)
- **Path Params**: `id` (int, required) - Redemption ID
- **Response**: `200 OK`
- **Note**: Can only cancel pending redemptions. Refunds the points back to user's wallet. Decrements the reward's redemption counter.

### 7.8 Get Provider Redemptions
- **Method**: `GET`
- **Path**: `/api/loyalty/admin/GetProviderRedemptions`
- **Auth**: `[Auth]` (Admin)
- **Query Params**:
  - `providerType` (string, optional)
  - `providerId` (int, optional)
  - `status` (int, optional)
- **Response**: `List<ProviderRedemptionDto>`

```json
// ProviderRedemptionDto
{
  "id": 1,
  "userName": "Ahmed",
  "rewardName": "Free Charging Session",
  "pointsSpent": 500,
  "status": 1,
  "redemptionCode": "RWD-A3F8K2",
  "redeemedAt": "2025-06-15T10:30:00Z",
  "fulfilledAt": null
}
```

---

## Error Responses

All endpoints may return:

| Status Code | Description |
|---|---|
| `200` | Success |
| `400` | Validation error (bad request body) |
| `401` | Unauthorized (missing or invalid token) |
| `403` | Forbidden (insufficient permissions) |
| `404` | Resource not found |
| `500` | Internal server error |

**Validation Error Format**:
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Name": ["The Name field is required."]
  }
}
```

---

## Offer Transaction Flow (End-to-End)

```
1. Admin creates conversion rates (one-time setup)
2. Provider proposes an offer → status: Pending
3. Admin approves the offer → status: Approved, isActive: true
4. User browses active offers (GetActiveOffers)
5. User selects offer → UseOffer → gets offer code + expiry time
6. User presents code to provider (show on phone screen)
7. Provider enters code → LookupTransaction → sees transaction details
8. Provider confirms with amount → ConfirmTransaction
   → Commission calculated
   → Loyalty points calculated and awarded to user
9. Monthly: Admin generates settlements (GenerateSettlement)
10. Admin reviews and pays settlements (UpdateSettlementStatus)
```

## Loyalty Points Flow

```
1. Admin creates a season (CreateSeason with activateImmediately: true)
2. Users earn points through actions:
   - Rate a station/service → automatic points
   - Add to favorites → automatic points
   - Complete offer transactions → dynamic points based on transaction amount
3. As users earn points:
   - Wallet balance increases
   - Season progress tracked
   - Tier automatically recalculated (Bronze → Silver → Gold → Platinum)
   - Higher tiers = higher multiplier on future points
4. Users browse and redeem rewards (RedeemReward → get redemption code)
5. User presents code to provider
6. Admin fulfills redemption (FulfillRedemption)
7. At season end: Admin calls EndSeason
   → All users get tier bonus points
   → Season deactivated
   → Admin creates new season for next period
```

---

## Flutter App Screens Suggestion

### User App Screens:

**Service Providers:**
1. **Service Provider List** - Browse/search providers with category filter and nearby
2. **Service Provider Detail** - View full details, ratings, attachments, map location, offers
3. **Category Browser** - Browse service categories
4. **My Favorite Providers** - List of favorited service providers
5. **Rate Provider** - Submit rating and comment
6. **Nearby Providers** - Map view with location-based search

**Offers:**
7. **Offers List** - Browse active offers with provider/category filters
8. **Offer Detail** - View offer details, tap "Use Offer" to generate code
9. **My Offer Code** - Show generated code with countdown timer
10. **My Transactions** - History of all offer transactions

**Loyalty:**
11. **Loyalty Dashboard** - Balance, tier, multiplier, season progress bar
12. **Points History** - Paginated list of all point transactions
13. **Rewards Store** - Browse available rewards with filter
14. **My Redemptions** - List of redeemed rewards with codes
15. **Leaderboard** - Top users in current season
16. **Season Info** - Current season details + past seasons

### Station Partner App Screens:
1. **My Offers** - List of proposed/approved/rejected offers
2. **Propose Offer** - Form to create new offer
3. **Scan/Enter Code** - Input offer code from user
4. **Confirm Transaction** - Enter transaction amount and confirm
5. **My Transactions** - History of confirmed transactions
6. **My Settlements** - Monthly settlement records
7. **Provider Rewards** - Rewards linked to this provider

### Admin Screens:
1. **Pending Offers** - Review and approve/reject offers
2. **Manage Offers** - Update/deactivate offers
3. **Season Management** - Create/end seasons
4. **Reward Management** - Create/update rewards
5. **Settlement Dashboard** - Summary + individual settlements
6. **Point Adjustments** - Manual point add/deduct
7. **Redemption Management** - Fulfill/cancel redemptions
8. **Conversion Rates** - Manage currency-to-points rates
