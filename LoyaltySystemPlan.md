# Cable Platform Expansion - Service Providers + Offers & Transactions + Loyalty System

## Table of Contents

### Phase 0: Service Provider Foundation
1. [Service Provider Overview](#1-service-provider-overview)
2. [Service Provider Database Design](#2-service-provider-database-design)
3. [Service Provider Domain Entities](#3-service-provider-domain-entities)
4. [Service Provider Enums](#4-service-provider-enums)
5. [Service Provider EF Configurations](#5-service-provider-ef-configurations)
6. [Service Provider Application Layer (CQRS)](#6-service-provider-application-layer-cqrs)
7. [Service Provider API Routes](#7-service-provider-api-routes)

### Phase 0.5: Offers & Transactions (Revenue + Points from Real Spending)
8. [Offers & Transactions Overview](#8-offers--transactions-overview)
9. [Offers & Transactions Database Design](#9-offers--transactions-database-design)
10. [Offers & Transactions Domain Entities](#10-offers--transactions-domain-entities)
11. [Offers & Transactions Enums](#11-offers--transactions-enums)
12. [Offers & Transactions EF Configurations](#12-offers--transactions-ef-configurations)
13. [Offers & Transactions Application Layer (CQRS)](#13-offers--transactions-application-layer-cqrs)
14. [Offers & Transactions API Routes](#14-offers--transactions-api-routes)
15. [Offers & Transactions Flow Diagrams](#15-offers--transactions-flow-diagrams)

### Phase 1: Loyalty & Points System
16. [Loyalty Overview](#16-loyalty-overview)
17. [Loyalty System Architecture](#17-loyalty-system-architecture)
18. [Loyalty Database Design](#18-loyalty-database-design)
19. [Loyalty Domain Entities](#19-loyalty-domain-entities)
20. [Loyalty Enums](#20-loyalty-enums)
21. [Loyalty EF Configurations](#21-loyalty-ef-configurations)
22. [Loyalty Application Layer (CQRS)](#22-loyalty-application-layer-cqrs)
23. [Loyalty Integration with Existing Features](#23-loyalty-integration-with-existing-features)
24. [Loyalty API Routes](#24-loyalty-api-routes)
25. [Notification Integration](#25-notification-integration)
26. [Seed Data](#26-seed-data)

### Implementation
27. [Full Implementation Order](#27-full-implementation-order)

---

# PHASE 0: SERVICE PROVIDER FOUNDATION

> **This phase MUST be completed before the Loyalty System.**
> It adds support for non-charging services (car wash, tire service, etc.) without touching the existing ChargingPoint system.

---

## 1. Service Provider Overview

### Why Phase 0 First?
The loyalty system needs to support spending points at **both** charging points AND service providers. If we build loyalty first, we'd have to refactor the reward model later. Building the service provider foundation first means the loyalty system is designed correctly from day one.

### Design Principle: Approach A — Separate Tables
- **ChargingPoint stays EXACTLY as it is** — zero changes, zero risk
- New **ServiceProvider** table handles all non-charging services
- Both ChargingPoint and ServiceProvider are **independently referenceable** from the loyalty system via `ProviderType` + `ProviderId` pattern
- The mobile app gets a new "Services" section alongside the existing "Charging Map"

### How ChargingPoint and ServiceProvider Relate

```
+------------------+                    +--------------------+
|  ChargingPoint   |                    |  ServiceProvider   |
|  (UNCHANGED)     |                    |  (NEW)             |
|                  |                    |                    |
|  Id              |                    |  Id                |
|  Name            |                    |  Name              |
|  OwnerId (FK)    |                    |  OwnerId (FK)      |
|  Latitude/Long   |                    |  Latitude/Long     |
|  Phone           |                    |  Phone             |
|  StatusId        |                    |  StatusId          |
|  IsVerified      |                    |  IsVerified        |
|  VisitorsCount   |                    |  VisitorsCount     |
|  Price, Hours    |                    |  Price, Hours      |
|  ...             |                    |  ServiceCategoryId |
+--------+---------+                    +--------+-----------+
         |                                       |
         | Both can be referenced                 |
         | by Loyalty Rewards via:               |
         |   ProviderType = "ChargingPoint"      |
         |   ProviderType = "ServiceProvider"     |
         +-------------------+-------------------+
                             |
                             v
                  +----------+-----------+
                  |    LoyaltyReward     |
                  |                      |
                  |  ProviderType (str)  |
                  |  ProviderId (int?)   |
                  +----------------------+
```

---

## 2. Service Provider Database Design

### 2.1 `dbo.ServiceCategory` — Types of services offered

> Simple lookup table. Admin-managed.

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `Id` | int (PK, Identity) | No | Auto-increment primary key |
| `Name` | nvarchar(255) | No | Category name (e.g., "Car Wash", "Tire Service") |
| `NameAr` | nvarchar(255) | Yes | Arabic name |
| `Description` | nvarchar(500) | Yes | Category description |
| `IconUrl` | nvarchar(500) | Yes | Category icon for mobile app |
| `SortOrder` | int | No | Display order in the app |
| `IsActive` | bit | No | Can be hidden without deleting |
| `CreatedAt` | datetime | No | BaseAuditableEntity |
| `CreatedBy` | int | Yes | BaseAuditableEntity |
| `ModifiedAt` | datetime | Yes | BaseAuditableEntity |
| `ModifiedBy` | int | Yes | BaseAuditableEntity |
| `IsDeleted` | bit | No | Soft delete (BaseAuditableEntity) |

**Indexes:**
- `IX_ServiceCategory_SortOrder` — for ordered display
- `IX_ServiceCategory_IsActive_IsDeleted` — active categories

**Seed Data:**

| Id | Name | NameAr | SortOrder | IsActive |
|----|------|--------|-----------|----------|
| 1 | Car Wash | غسيل سيارات | 1 | true |
| 2 | Tire Service | خدمة إطارات | 2 | true |
| 3 | Oil Change | تغيير زيت | 3 | true |
| 4 | Towing | سحب سيارات | 4 | true |
| 5 | Car Detailing | تنظيف وتلميع | 5 | true |
| 6 | Battery Service | خدمة بطارية | 6 | true |

---

### 2.2 `dbo.ServiceProvider` — The actual service businesses

> Follows the same pattern as ChargingPoint: owner, location, contact, hours, verification.

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `Id` | int (PK, Identity) | No | Auto-increment primary key |
| `Name` | nvarchar(255) | No | Business name |
| `OwnerId` | int (FK -> UserAccount) | No | Business owner (same concept as ChargingPoint.OwnerId) |
| `ServiceCategoryId` | int (FK -> ServiceCategory) | No | What type of service |
| `StatusId` | int (FK -> Status) | No | Open/Closed (reuses existing Status table: 1=مفتوح, 2=مغلق) |
| `Description` | nvarchar(1000) | Yes | Business description |
| `Phone` | nvarchar(40) | Yes | Contact phone |
| `OwnerPhone` | nvarchar(40) | Yes | Owner's phone |
| `Address` | nvarchar(1000) | Yes | Physical address |
| `CountryName` | nvarchar(200) | Yes | Country |
| `CityName` | nvarchar(200) | Yes | City |
| `Latitude` | float | No | GPS latitude |
| `Longitude` | float | No | GPS longitude |
| `Price` | float | Yes | Starting price or average price |
| `PriceDescription` | nvarchar(500) | Yes | e.g., "Starting from 5 KWD", "10-25 KWD depending on size" |
| `FromTime` | nvarchar(16) | Yes | Opening time |
| `ToTime` | nvarchar(16) | Yes | Closing time |
| `MethodPayment` | nvarchar(200) | Yes | Accepted payment methods |
| `VisitorsCount` | int | No | Default 0 |
| `IsVerified` | bit | No | Admin verified |
| `HasOffer` | bit | No | Has active offer |
| `OfferDescription` | nvarchar(2000) | Yes | Offer details |
| `Service` | nvarchar(max) | Yes | Additional services description |
| `Icon` | nvarchar(max) | Yes | Custom icon |
| `Note` | nvarchar(1000) | Yes | Admin notes |
| `WhatsAppNumber` | nvarchar(80) | Yes | WhatsApp contact |
| `WebsiteUrl` | nvarchar(500) | Yes | Business website |
| `CreatedAt` | datetime | No | BaseAuditableEntity |
| `CreatedBy` | int | Yes | BaseAuditableEntity |
| `ModifiedAt` | datetime | Yes | BaseAuditableEntity |
| `ModifiedBy` | int | Yes | BaseAuditableEntity |
| `IsDeleted` | bit | No | Soft delete (BaseAuditableEntity) |

**Indexes:**
- `IX_ServiceProvider_ServiceCategoryId` — filter by category
- `IX_ServiceProvider_OwnerId` — provider's services
- `IX_ServiceProvider_StatusId` — open/closed filter
- `IX_ServiceProvider_IsDeleted_IsVerified` — active verified providers
- `IX_ServiceProvider_Lat_Lng` — location-based queries

**Foreign Keys:**
- `FK_ServiceProvider_UserAccount` -> `dbo.UserAccount(Id)`
- `FK_ServiceProvider_ServiceCategory` -> `dbo.ServiceCategory(Id)`
- `FK_ServiceProvider_Status` -> `dbo.Status(Id)`

---

### 2.3 `dbo.ServiceProviderAttachment` — Images for service providers

> Same pattern as ChargingPointAttachment and EmergencyServiceAttachment.

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `Id` | int (PK, Identity) | No | Auto-increment primary key |
| `ServiceProviderId` | int (FK) | No | Which service provider |
| `FileSize` | bigint | No | File size in bytes |
| `FileExtension` | nvarchar(100) | No | e.g., ".jpg", ".png" |
| `FileName` | nvarchar(255) | No | Original filename |
| `ContentType` | nvarchar(100) | No | MIME type |
| `CreatedAt` | datetime | No | BaseAuditableEntity |
| `CreatedBy` | int | Yes | BaseAuditableEntity |
| `ModifiedAt` | datetime | Yes | BaseAuditableEntity |
| `ModifiedBy` | int | Yes | BaseAuditableEntity |
| `IsDeleted` | bit | No | Soft delete (BaseAuditableEntity) |

**Indexes:**
- `IX_ServiceProviderAttachment_ServiceProviderId` — attachments per provider
- `IX_ServiceProviderAttachment_IsDeleted` — active attachments

**Foreign Keys:**
- `FK_ServiceProviderAttachment_ServiceProvider` -> `dbo.ServiceProvider(Id)`

---

### 2.4 `dbo.ServiceProviderRate` — User ratings for service providers

> Same pattern as the existing Rate table for ChargingPoints.

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `Id` | int (PK, Identity) | No | Auto-increment primary key |
| `ServiceProviderId` | int (FK) | No | Which service provider |
| `UserId` | int (FK -> UserAccount) | No | Who rated |
| `Rating` | int | No | 1-5 stars |
| `AVGRating` | float | No | Running average at time of rating |
| `Comment` | nvarchar(1000) | Yes | Optional review text |
| `CreatedAt` | datetime | No | BaseAuditableEntity |
| `CreatedBy` | int | Yes | BaseAuditableEntity |
| `ModifiedAt` | datetime | Yes | BaseAuditableEntity |
| `ModifiedBy` | int | Yes | BaseAuditableEntity |
| `IsDeleted` | bit | No | Soft delete (BaseAuditableEntity) |

**Indexes:**
- `IX_ServiceProviderRate_ServiceProviderId` — ratings per provider
- `IX_ServiceProviderRate_UserId` — user's rating history
- `IX_ServiceProviderRate_UserId_ServiceProviderId` — prevent duplicate ratings

**Foreign Keys:**
- `FK_ServiceProviderRate_ServiceProvider` -> `dbo.ServiceProvider(Id)`
- `FK_ServiceProviderRate_UserAccount` -> `dbo.UserAccount(Id)`

---

### 2.5 `dbo.UserFavoriteServiceProvider` — User favorites for service providers

> Same pattern as UserFavoriteChargingPoint.

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `Id` | int (PK, Identity) | No | Auto-increment primary key |
| `UserId` | int (FK -> UserAccount) | No | Who favorited |
| `ServiceProviderId` | int (FK) | No | Which service provider |
| `CreatedAt` | datetime | No | BaseAuditableEntity |
| `CreatedBy` | int | Yes | BaseAuditableEntity |
| `ModifiedAt` | datetime | Yes | BaseAuditableEntity |
| `ModifiedBy` | int | Yes | BaseAuditableEntity |
| `IsDeleted` | bit | No | Soft delete (BaseAuditableEntity) |

**Indexes:**
- `IX_UserFavoriteServiceProvider_UserId_ServiceProviderId` — unique per user+provider

**Constraints:**
- `UQ_UserFavoriteServiceProvider_User_Provider` — UNIQUE on (UserId, ServiceProviderId)

**Foreign Keys:**
- `FK_UserFavoriteServiceProvider_UserAccount` -> `dbo.UserAccount(Id)`
- `FK_UserFavoriteServiceProvider_ServiceProvider` -> `dbo.ServiceProvider(Id)`

---

### Service Provider Entity Relationship Diagram

```
+------------------+
|  ServiceCategory |
|                  |
|  Id              |
|  Name / NameAr   |
|  IconUrl         |
|  SortOrder       |
+--------+---------+
         | 1:N
         v
+--------+---------------------+     +----------------+
|  ServiceProvider             |---->|  Status         |
|                              | N:1 |  (reused)      |
|  Id                          |     |  1=Open 2=Close |
|  Name                        |     +----------------+
|  OwnerId (FK->UserAccount)  |
|  ServiceCategoryId (FK)      |     +----------------+
|  StatusId (FK->Status)       |---->|  UserAccount   |
|  Description, Phone, Address |     |  (reused)      |
|  Latitude, Longitude         |     +----------------+
|  Price, PriceDescription     |
|  FromTime, ToTime            |
|  IsVerified, HasOffer        |
|  VisitorsCount               |
+---+--------+---------+------+
    |        |         |
    | 1:N    | 1:N     | 1:N
    v        v         v
+---+----+ +-+------+ ++------------------------+
|Attach- | |Rate    | |UserFavoriteService-     |
|ment    | |        | |Provider                 |
|        | |UserId  | |                         |
|FileSize| |Rating  | |UserId                   |
|FileName| |Comment | |ServiceProviderId        |
+--------+ +--------+ +-------------------------+
```

---

## 3. Service Provider Domain Entities

### 3.1 `ServiceCategory.cs`
```csharp
// Domain/Enitites/ServiceCategory.cs
// Extends: BaseAuditableEntity
// Properties: Name, NameAr, Description, IconUrl, SortOrder, IsActive
// Navigation: ICollection<ServiceProvider> ServiceProviders
```

### 3.2 `ServiceProvider.cs`
```csharp
// Domain/Enitites/ServiceProvider.cs
// Extends: BaseAuditableEntity
// Properties: Name, OwnerId, ServiceCategoryId, StatusId, Description, Phone, OwnerPhone,
//             Address, CountryName, CityName, Latitude, Longitude, Price, PriceDescription,
//             FromTime, ToTime, MethodPayment, VisitorsCount, IsVerified, HasOffer,
//             OfferDescription, Service, Icon, Note, WhatsAppNumber, WebsiteUrl
// Navigation: UserAccount Owner, ServiceCategory Category, Status Status,
//             ICollection<ServiceProviderAttachment> Attachments,
//             ICollection<ServiceProviderRate> Rates,
//             ICollection<UserFavoriteServiceProvider> Favorites
```

### 3.3 `ServiceProviderAttachment.cs`
```csharp
// Domain/Enitites/ServiceProviderAttachment.cs
// Extends: BaseAuditableEntity
// Properties: ServiceProviderId, FileSize, FileExtension, FileName, ContentType
// Navigation: ServiceProvider ServiceProvider
```

### 3.4 `ServiceProviderRate.cs`
```csharp
// Domain/Enitites/ServiceProviderRate.cs
// Extends: BaseAuditableEntity
// Properties: ServiceProviderId, UserId, Rating, AVGRating, Comment
// Navigation: ServiceProvider ServiceProvider, UserAccount User
```

### 3.5 `UserFavoriteServiceProvider.cs`
```csharp
// Domain/Enitites/UserFavoriteServiceProvider.cs
// Extends: BaseAuditableEntity
// Properties: UserId, ServiceProviderId
// Navigation: UserAccount User, ServiceProvider ServiceProvider
```

### 3.6 Update `UserAccount.cs` — Add navigation properties
```csharp
// Add to existing UserAccount.cs:
public virtual ICollection<ServiceProvider> OwnedServiceProviders { get; set; } = new List<ServiceProvider>();
public virtual ICollection<ServiceProviderRate> ServiceProviderRates { get; set; } = new List<ServiceProviderRate>();
public virtual ICollection<UserFavoriteServiceProvider> FavoriteServiceProviders { get; set; } = new List<UserFavoriteServiceProvider>();
```

---

## 4. Service Provider Enums

No new enums needed for Phase 0. The `Status` table is reused (1=Open, 2=Closed).

---

## 5. Service Provider EF Configurations

New configuration files in `Infrastructrue/Persistence/Configurations/`:

| File | Key Configuration |
|------|-------------------|
| `ServiceCategoryConfiguration.cs` | Index on SortOrder, IsActive |
| `ServiceProviderConfiguration.cs` | FKs to UserAccount, ServiceCategory, Status; indexes on category, location |
| `ServiceProviderAttachmentConfiguration.cs` | FK to ServiceProvider, index on IsDeleted |
| `ServiceProviderRateConfiguration.cs` | FKs to ServiceProvider + UserAccount |
| `UserFavoriteServiceProviderConfiguration.cs` | Unique on (UserId, ServiceProviderId) |

### Update `IApplicationDbContext.cs` — Add 5 DbSets:
```csharp
DbSet<ServiceCategory> ServiceCategories { get; set; }
DbSet<ServiceProvider> ServiceProviders { get; set; }
DbSet<ServiceProviderAttachment> ServiceProviderAttachments { get; set; }
DbSet<ServiceProviderRate> ServiceProviderRates { get; set; }
DbSet<UserFavoriteServiceProvider> UserFavoriteServiceProviders { get; set; }
```

---

## 6. Service Provider Application Layer (CQRS)

### 6.1 Commands

| Folder Path | Command | Description |
|-------------|---------|-------------|
| `Application/ServiceProviders/Commands/CreateServiceProvider/` | `CreateServiceProviderCommand` | Admin/Owner creates a new service provider |
| `Application/ServiceProviders/Commands/UpdateServiceProvider/` | `UpdateServiceProviderCommand` | Update service provider details |
| `Application/ServiceProviders/Commands/DeleteServiceProvider/` | `DeleteServiceProviderCommand` | Soft delete |
| `Application/ServiceProviders/Commands/VerifyServiceProvider/` | `VerifyServiceProviderCommand` | Admin verifies a provider |
| `Application/ServiceProviders/Commands/RateServiceProvider/` | `RateServiceProviderCommand` | User rates a service provider |
| `Application/ServiceProviders/Commands/AddToFavoriteService/` | `AddToFavoriteServiceCommand` | User favorites a service provider |
| `Application/ServiceProviders/Commands/RemoveFromFavoriteService/` | `RemoveFromFavoriteServiceCommand` | User removes favorite |
| `Application/ServiceCategories/Commands/CreateServiceCategory/` | `CreateServiceCategoryCommand` | Admin creates a category |
| `Application/ServiceCategories/Commands/UpdateServiceCategory/` | `UpdateServiceCategoryCommand` | Admin updates a category |

### 6.2 Queries

| Folder Path | Query | Returns |
|-------------|-------|---------|
| `Application/ServiceProviders/Queries/GetAllServiceProviders/` | `GetAllServiceProvidersRequest` | All active providers (with filters) |
| `Application/ServiceProviders/Queries/GetServiceProviderById/` | `GetServiceProviderByIdRequest` | Single provider with details |
| `Application/ServiceProviders/Queries/GetServiceProvidersByCategory/` | `GetProvidersByCategoryRequest` | Providers filtered by category |
| `Application/ServiceProviders/Queries/GetNearbyServiceProviders/` | `GetNearbyProvidersRequest` | Providers near lat/lng |
| `Application/ServiceProviders/Queries/GetMyFavoriteServices/` | `GetMyFavoriteServicesRequest` | User's favorite service providers |
| `Application/ServiceProviders/Queries/GetServiceProviderRatings/` | `GetProviderRatingsRequest` | Ratings for a provider |
| `Application/ServiceCategories/Queries/GetAllServiceCategories/` | `GetAllCategoriesRequest` | All active categories |

---

## 7. Service Provider API Routes

### New File: `WebApi/Routes/ServiceProviderRoutes.cs`

```
/api/service-providers (route group, tag: "Service Providers")

  User Endpoints (RequireAuthorization):
  =======================================
  GET  /                          -> GetAllServiceProvidersRequest
       ?categoryId=1&lat=&lng=      Returns: List<ServiceProviderDto>

  GET  /{id}                      -> GetServiceProviderByIdRequest
                                     Returns: ServiceProviderDetailDto

  GET  /by-category/{categoryId}  -> GetProvidersByCategoryRequest
                                     Returns: List<ServiceProviderDto>

  GET  /nearby                    -> GetNearbyProvidersRequest
       ?lat=29.37&lng=47.98&radiusKm=10
                                     Returns: List<ServiceProviderDto>

  POST /{id}/rate                 -> RateServiceProviderCommand
       Body: { rating, comment? }    Returns: int (rateId)

  POST /{id}/favorite             -> AddToFavoriteServiceCommand
                                     Returns: int (favoriteId)

  DELETE /{id}/favorite           -> RemoveFromFavoriteServiceCommand
                                     Returns: 200

  GET  /my-favorites              -> GetMyFavoriteServicesRequest
                                     Returns: List<ServiceProviderDto>

  GET  /{id}/ratings              -> GetProviderRatingsRequest
                                     Returns: List<RatingDto>


  Admin Endpoints (RequireAuthorization + Admin role):
  ====================================================
  POST /                          -> CreateServiceProviderCommand
                                     Returns: int (providerId)

  PUT  /{id}                      -> UpdateServiceProviderCommand
                                     Returns: 200

  DELETE /{id}                    -> DeleteServiceProviderCommand
                                     Returns: 200

  PATCH /{id}/verify              -> VerifyServiceProviderCommand
                                     Returns: 200


/api/service-categories (route group, tag: "Service Categories")

  GET  /                          -> GetAllCategoriesRequest
                                     Returns: List<ServiceCategoryDto>

  POST /                          -> CreateServiceCategoryCommand (Admin)
                                     Returns: int (categoryId)

  PUT  /{id}                      -> UpdateServiceCategoryCommand (Admin)
                                     Returns: 200
```

---

# PHASE 0.5: OFFERS & TRANSACTIONS (REVENUE + POINTS FROM REAL SPENDING)

> **This phase MUST be completed after Phase 0 and before Phase 1.**
> It adds the ability for providers to create offers, users to use those offers at physical locations, and Cable to earn commission + award points based on real transaction amounts.

---

## 8. Offers & Transactions Overview

### The Business Model
Cable acts as a **marketplace** that drives customers to charging points and service providers. When a user visits a provider through Cable's offer system:

1. **Cable earns commission** — a configurable percentage of the transaction amount
2. **User earns points** — a configurable percentage converted to points via a configurable rate
3. **Provider gains customers** — driven by the Cable app's user base

### The Complete User Journey

```
STEP 1: Provider proposes an offer → Admin reviews → Approves/Rejects
        "Charge at Kuwait EV Hub: Cable users get 5% back in points"

STEP 2: User sees the offer in the app → taps "Use This Offer"
        → System generates a unique OFFER CODE (e.g., "CBL-7X9K2M")
        → Code expires in 30 minutes (configurable)
        → Status: Initiated

STEP 3: User goes to the physical location
        → Tells provider: "I'm from Cable app, code: CBL-7X9K2M"

STEP 4: Provider opens their dashboard in the app
        → Enters the code: CBL-7X9K2M
        → Enters the actual amount paid: 10 KWD
        → Clicks "Confirm Transaction"

STEP 5: System automatically calculates:
        → Cable commission (10%): 1.00 KWD → added to settlement ledger
        → User points: 5% of 10 KWD = 0.50 KWD × conversion rate → points
        → Transaction status: Completed
        → User gets notification: "You earned X points!"
        → Provider gets notification: "Transaction confirmed"

STEP 6: Monthly settlement:
        → Admin sees: "Kuwait EV Hub owes Cable 150 KWD this month"
        → Admin marks settlement as Paid after receiving payment
```

### Why Code-Based (Not Barcode/QR)?
- **No camera/scanner needed** on provider's device
- **Short code is easy** to type on any device
- **Expiry prevents abuse** — code is useless after 30 minutes
- **Provider confirms the real amount** — no manipulation possible
- **Works offline** at the provider location (code entered later)

### Design Principles
- Offers use the same `ProviderType` + `ProviderId` pattern as rewards
- Provider-proposed offers require **admin approval** before going live
- Points conversion rate is **configurable per offer** or globally
- Settlement tracking is a complete **accounting ledger** per provider per month
- Integrates with Phase 1 loyalty system: offer transactions trigger `ILoyaltyPointService`

---

## 9. Offers & Transactions Database Design

### 9.1 `dbo.PointsConversionRate` — How currency converts to points

> Global or per-offer configurable rate. Only ONE record should be `IsDefault = true`.

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `Id` | int (PK, Identity) | No | Auto-increment primary key |
| `Name` | nvarchar(255) | No | e.g., "Default Rate", "Premium Rate", "Summer Promo Rate" |
| `CurrencyCode` | nvarchar(10) | No | e.g., "KWD", "USD", "SAR" |
| `PointsPerUnit` | float | No | How many points per 1 unit of currency. e.g., 10 = 1 KWD gives 10 points |
| `IsDefault` | bit | No | Only ONE row should be true — used when offer doesn't specify a rate |
| `IsActive` | bit | No | Can be deactivated |
| `CreatedAt` | datetime | No | BaseAuditableEntity |
| `CreatedBy` | int | Yes | BaseAuditableEntity |
| `ModifiedAt` | datetime | Yes | BaseAuditableEntity |
| `ModifiedBy` | int | Yes | BaseAuditableEntity |
| `IsDeleted` | bit | No | Soft delete (BaseAuditableEntity) |

**Indexes:**
- `IX_PointsConversionRate_IsDefault` — fast lookup for default rate
- `IX_PointsConversionRate_IsActive_IsDeleted` — active rates

**Seed Data:**

| Id | Name | CurrencyCode | PointsPerUnit | IsDefault | IsActive |
|----|------|-------------|---------------|-----------|----------|
| 1 | Default Rate | KWD | 10.0 | true | true |

**Example: How conversion works:**
```
User spends 10 KWD at a provider
Offer PointsRewardPercentage = 5%
Points-eligible amount = 10 × 5% = 0.50 KWD
Conversion rate = 10 points per 1 KWD
Points awarded = 0.50 × 10 = 5 points
```

---

### 9.2 `dbo.ProviderOffer` — Offers/Deals at specific providers

> An offer represents a deal that a provider runs through Cable. It defines the commission Cable takes and the points users earn.

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `Id` | int (PK, Identity) | No | Auto-increment primary key |
| `Title` | nvarchar(255) | No | Offer title, e.g., "Charge & Earn at Kuwait EV Hub" |
| `TitleAr` | nvarchar(255) | Yes | Arabic title |
| `Description` | nvarchar(1000) | Yes | Offer description |
| `DescriptionAr` | nvarchar(1000) | Yes | Arabic description |
| `ProviderType` | nvarchar(50) | No | "ChargingPoint" or "ServiceProvider" |
| `ProviderId` | int | No | FK to ChargingPoint.Id or ServiceProvider.Id (polymorphic) |
| `ProposedByUserId` | int (FK -> UserAccount) | No | Who proposed this offer (provider owner or admin) |
| `ApprovalStatus` | int | No | Enum: 1=Pending, 2=Approved, 3=Rejected |
| `ApprovedByUserId` | int (FK -> UserAccount) | Yes | Admin who approved/rejected |
| `ApprovalNote` | nvarchar(500) | Yes | Admin's note on approval/rejection |
| `ApprovedAt` | datetime | Yes | When approved/rejected |
| `CommissionPercentage` | float | No | Cable's commission %. e.g., 10.0 means Cable takes 10% |
| `PointsRewardPercentage` | float | No | User's points reward %. e.g., 5.0 means user earns 5% in points |
| `PointsConversionRateId` | int (FK) | Yes | Specific conversion rate (NULL = use default) |
| `MinTransactionAmount` | float | Yes | Minimum transaction amount for offer to apply |
| `MaxTransactionAmount` | float | Yes | Maximum transaction amount for offer to apply |
| `MaxUsesPerUser` | int | Yes | How many times one user can use this offer (NULL = unlimited) |
| `MaxTotalUses` | int | Yes | Total uses across all users (NULL = unlimited) |
| `CurrentTotalUses` | int | No | Counter: how many times used so far. Default 0 |
| `OfferCodeExpiryMinutes` | int | No | How long generated codes are valid. Default 30 |
| `ImageUrl` | nvarchar(500) | Yes | Offer banner image |
| `ValidFrom` | datetime | No | When offer becomes available |
| `ValidTo` | datetime | Yes | When offer expires (NULL = no expiry) |
| `IsActive` | bit | No | Can be deactivated |
| `CreatedAt` | datetime | No | BaseAuditableEntity |
| `CreatedBy` | int | Yes | BaseAuditableEntity |
| `ModifiedAt` | datetime | Yes | BaseAuditableEntity |
| `ModifiedBy` | int | Yes | BaseAuditableEntity |
| `IsDeleted` | bit | No | Soft delete (BaseAuditableEntity) |

**Indexes:**
- `IX_ProviderOffer_ProviderType_ProviderId` — offers for a specific provider
- `IX_ProviderOffer_ApprovalStatus` — pending offers for admin review
- `IX_ProviderOffer_IsActive_ValidFrom_ValidTo` — currently available offers
- `IX_ProviderOffer_ProposedByUserId` — provider's proposed offers
- `IX_ProviderOffer_IsDeleted` — active offers

**Foreign Keys:**
- `FK_ProviderOffer_ProposedByUser` -> `dbo.UserAccount(Id)`
- `FK_ProviderOffer_ApprovedByUser` -> `dbo.UserAccount(Id)` (optional)
- `FK_ProviderOffer_PointsConversionRate` -> `dbo.PointsConversionRate(Id)` (optional)

**Approval Flow:**
```
Provider proposes offer → ApprovalStatus = Pending (1)
                              |
                    Admin reviews
                    /           \
          Approved (2)       Rejected (3)
          Offer goes live    Provider notified
```

---

### 9.3 `dbo.OfferTransaction` — Records each real-world transaction

> Created when user initiates a code, completed when provider confirms the amount.

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `Id` | int (PK, Identity) | No | Auto-increment primary key |
| `ProviderOfferId` | int (FK -> ProviderOffer) | No | Which offer was used |
| `UserId` | int (FK -> UserAccount) | No | Which user |
| `OfferCode` | nvarchar(20) | No | Unique short code, e.g., "CBL-7X9K2M" |
| `Status` | int | No | Enum: 1=Initiated, 2=Completed, 3=Expired, 4=Cancelled |
| `TransactionAmount` | decimal(18,3) | Yes | Actual amount paid (entered by provider). NULL until confirmed |
| `CurrencyCode` | nvarchar(10) | Yes | e.g., "KWD". NULL until confirmed |
| `CommissionPercentage` | float | No | Snapshot from offer at time of transaction |
| `CommissionAmount` | decimal(18,3) | Yes | Calculated: TransactionAmount × CommissionPercentage / 100 |
| `PointsRewardPercentage` | float | No | Snapshot from offer |
| `PointsConversionRate` | float | No | Snapshot: PointsPerUnit at time of transaction |
| `PointsEligibleAmount` | decimal(18,3) | Yes | TransactionAmount × PointsRewardPercentage / 100 |
| `PointsAwarded` | int | Yes | Floor(PointsEligibleAmount × PointsConversionRate) |
| `ProviderType` | nvarchar(50) | No | Snapshot from offer |
| `ProviderId` | int | No | Snapshot from offer |
| `ConfirmedByUserId` | int (FK -> UserAccount) | Yes | Provider/admin who confirmed |
| `CodeExpiresAt` | datetime | No | When the code becomes invalid |
| `CompletedAt` | datetime | Yes | When provider confirmed |
| `CreatedAt` | datetime | No | BaseAuditableEntity |
| `CreatedBy` | int | Yes | BaseAuditableEntity |
| `ModifiedAt` | datetime | Yes | BaseAuditableEntity |
| `ModifiedBy` | int | Yes | BaseAuditableEntity |
| `IsDeleted` | bit | No | Soft delete (BaseAuditableEntity) |

**Indexes:**
- `IX_OfferTransaction_OfferCode_Unique` — UNIQUE on `OfferCode`
- `IX_OfferTransaction_ProviderOfferId` — transactions per offer
- `IX_OfferTransaction_UserId` — user's transaction history
- `IX_OfferTransaction_Status` — filter by status
- `IX_OfferTransaction_ProviderType_ProviderId` — transactions at a provider
- `IX_OfferTransaction_CodeExpiresAt` — expired code cleanup
- `IX_OfferTransaction_CompletedAt` — settlement date range

**Foreign Keys:**
- `FK_OfferTransaction_ProviderOffer` -> `dbo.ProviderOffer(Id)`
- `FK_OfferTransaction_User` -> `dbo.UserAccount(Id)`
- `FK_OfferTransaction_ConfirmedByUser` -> `dbo.UserAccount(Id)` (optional)

**Transaction Lifecycle:**
```
User taps "Use Offer"          → Status = Initiated (1), OfferCode generated, CodeExpiresAt set
Code expires (30 min)          → Status = Expired (3) [HangFire background job]
Provider enters code + amount  → Status = Completed (2), all amounts calculated
Admin cancels                  → Status = Cancelled (4)
```

**Calculation Example:**
```
Offer: CommissionPercentage = 10%, PointsRewardPercentage = 5%
Conversion Rate: 10 points per 1 KWD

User pays 10 KWD at provider:
┌──────────────────────────────────────────────────────┐
│ TransactionAmount    = 10.000 KWD                     │
│ CommissionAmount     = 10 × 10% = 1.000 KWD (Cable)  │
│ PointsEligibleAmount = 10 × 5%  = 0.500 KWD          │
│ PointsAwarded        = 0.500 × 10 = 5 points (User)  │
└──────────────────────────────────────────────────────┘
```

---

### 9.4 `dbo.ProviderSettlement` — Monthly settlement ledger

> Tracks how much each provider owes Cable (or vice versa) per month.

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `Id` | int (PK, Identity) | No | Auto-increment primary key |
| `ProviderType` | nvarchar(50) | No | "ChargingPoint" or "ServiceProvider" |
| `ProviderId` | int | No | Which provider |
| `ProviderOwnerId` | int (FK -> UserAccount) | No | Provider's owner (for quick lookup) |
| `PeriodYear` | int | No | Settlement year, e.g., 2026 |
| `PeriodMonth` | int | No | Settlement month, 1-12 |
| `TotalTransactions` | int | No | Number of completed transactions in this period |
| `TotalTransactionAmount` | decimal(18,3) | No | Sum of all TransactionAmount |
| `TotalCommissionAmount` | decimal(18,3) | No | Sum of all CommissionAmount (what provider owes Cable) |
| `TotalPointsAwarded` | int | No | Sum of all PointsAwarded to users |
| `SettlementStatus` | int | No | Enum: 1=Pending, 2=Invoiced, 3=Paid, 4=Disputed |
| `InvoicedAt` | datetime | Yes | When invoice was sent |
| `PaidAt` | datetime | Yes | When payment was received |
| `PaidAmount` | decimal(18,3) | Yes | Actual amount paid (may differ if disputed) |
| `AdminNote` | nvarchar(1000) | Yes | Admin comments |
| `CreatedAt` | datetime | No | BaseAuditableEntity |
| `CreatedBy` | int | Yes | BaseAuditableEntity |
| `ModifiedAt` | datetime | Yes | BaseAuditableEntity |
| `ModifiedBy` | int | Yes | BaseAuditableEntity |
| `IsDeleted` | bit | No | Soft delete (BaseAuditableEntity) |

**Indexes:**
- `IX_ProviderSettlement_Provider_Period` — UNIQUE on (ProviderType, ProviderId, PeriodYear, PeriodMonth)
- `IX_ProviderSettlement_SettlementStatus` — filter pending/unpaid
- `IX_ProviderSettlement_ProviderOwnerId` — settlements for an owner
- `IX_ProviderSettlement_PeriodYear_PeriodMonth` — monthly reports

**Foreign Keys:**
- `FK_ProviderSettlement_ProviderOwner` -> `dbo.UserAccount(Id)`

**Settlement Flow:**
```
End of month (HangFire job or admin trigger):
  1. For each provider with completed transactions:
     - Sum all OfferTransaction where CompletedAt is within the month
     - Create/update ProviderSettlement record
     - Status = Pending

Admin reviews:
  2. Sends invoice → Status = Invoiced
  3. Provider pays → Status = Paid
  4. Dispute → Status = Disputed + AdminNote
```

---

### Offers & Transactions Entity Relationship Diagram

```
+---------------------+        +----------------------+
| PointsConversion-   |        |  UserAccount         |
| Rate                |        |  (reused)            |
|                     |        |                      |
| Id                  |        | ProposedByUserId ◄───┐
| Name                |        | ApprovedByUserId ◄──┐│
| CurrencyCode        |        | ConfirmedByUserId ◄┐││
| PointsPerUnit       |        +-----────────────────+││
| IsDefault           |                     │         │││
+----------+----------+                     │         │││
           │ 0..1                           │         │││
           ▼                                │         │││
+----------+---------------------------+    │         │││
|  ProviderOffer                       |    │         │││
|                                      +────┘         │││
|  Id                                  | ProposedBy───┘││
|  Title / TitleAr                     | ApprovedBy────┘│
|  ProviderType + ProviderId           |                │
|  CommissionPercentage                |                │
|  PointsRewardPercentage              |                │
|  PointsConversionRateId (FK)         |                │
|  ApprovalStatus (Pending/Approved)   |                │
|  ValidFrom / ValidTo                 |                │
|  MaxUsesPerUser, MaxTotalUses        |                │
+----------+---------------------------+                │
           │ 1:N                                        │
           ▼                                            │
+----------+---------------------------+                │
|  OfferTransaction                    |                │
|                                      |                │
|  Id                                  |                │
|  ProviderOfferId (FK)                |                │
|  UserId (FK -> UserAccount)          |                │
|  OfferCode ("CBL-7X9K2M")           |                │
|  Status (Initiated→Completed)        |                │
|  TransactionAmount                   |                │
|  CommissionAmount                    |                │
|  PointsAwarded                       |                │
|  ConfirmedByUserId (FK) ─────────────┘
|  CodeExpiresAt                       |
+----------+---------------------------+
           │ aggregated into
           ▼
+----------+---------------------------+
|  ProviderSettlement                  |
|                                      |
|  ProviderType + ProviderId           |
|  PeriodYear + PeriodMonth            |
|  TotalTransactions                   |
|  TotalTransactionAmount              |
|  TotalCommissionAmount               |
|  SettlementStatus (Pending→Paid)     |
+--------------------------------------+
```

---

## 10. Offers & Transactions Domain Entities

### 10.1 `PointsConversionRate.cs`
```csharp
// Domain/Enitites/PointsConversionRate.cs
// Extends: BaseAuditableEntity
// Properties: Name, CurrencyCode, PointsPerUnit, IsDefault, IsActive
// Navigation: ICollection<ProviderOffer> Offers
```

### 10.2 `ProviderOffer.cs`
```csharp
// Domain/Enitites/ProviderOffer.cs
// Extends: BaseAuditableEntity
// Properties: Title, TitleAr, Description, DescriptionAr,
//             ProviderType, ProviderId, ProposedByUserId, ApprovalStatus,
//             ApprovedByUserId, ApprovalNote, ApprovedAt,
//             CommissionPercentage, PointsRewardPercentage,
//             PointsConversionRateId, MinTransactionAmount, MaxTransactionAmount,
//             MaxUsesPerUser, MaxTotalUses, CurrentTotalUses,
//             OfferCodeExpiryMinutes, ImageUrl, ValidFrom, ValidTo, IsActive
// Navigation: UserAccount ProposedByUser, UserAccount? ApprovedByUser,
//             PointsConversionRate? ConversionRate,
//             ICollection<OfferTransaction> Transactions
```

### 10.3 `OfferTransaction.cs`
```csharp
// Domain/Enitites/OfferTransaction.cs
// Extends: BaseAuditableEntity
// Properties: ProviderOfferId, UserId, OfferCode, Status,
//             TransactionAmount, CurrencyCode, CommissionPercentage,
//             CommissionAmount, PointsRewardPercentage, PointsConversionRate,
//             PointsEligibleAmount, PointsAwarded, ProviderType, ProviderId,
//             ConfirmedByUserId, CodeExpiresAt, CompletedAt
// Navigation: ProviderOffer Offer, UserAccount User, UserAccount? ConfirmedByUser
```

### 10.4 `ProviderSettlement.cs`
```csharp
// Domain/Enitites/ProviderSettlement.cs
// Extends: BaseAuditableEntity
// Properties: ProviderType, ProviderId, ProviderOwnerId,
//             PeriodYear, PeriodMonth, TotalTransactions,
//             TotalTransactionAmount, TotalCommissionAmount,
//             TotalPointsAwarded, SettlementStatus,
//             InvoicedAt, PaidAt, PaidAmount, AdminNote
// Navigation: UserAccount ProviderOwner
```

### 10.5 Update `UserAccount.cs` — Add navigation properties
```csharp
// Add to existing UserAccount.cs:
public virtual ICollection<ProviderOffer> ProposedOffers { get; set; } = new List<ProviderOffer>();
public virtual ICollection<OfferTransaction> OfferTransactions { get; set; } = new List<OfferTransaction>();
```

---

## 11. Offers & Transactions Enums

New enums in `Cable.Core/Enums/`:

### `OfferApprovalStatus.cs`
```csharp
public enum OfferApprovalStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3
}
```

### `OfferTransactionStatus.cs`
```csharp
public enum OfferTransactionStatus
{
    Initiated = 1,
    Completed = 2,
    Expired = 3,
    Cancelled = 4
}
```

### `SettlementStatus.cs`
```csharp
public enum SettlementStatus
{
    Pending = 1,
    Invoiced = 2,
    Paid = 3,
    Disputed = 4
}
```

---

## 12. Offers & Transactions EF Configurations

4 configuration files in `Infrastructrue/Persistence/Configurations/`:

| File | Key Configuration |
|------|-------------------|
| `PointsConversionRateConfiguration.cs` | Index on IsDefault, IsActive |
| `ProviderOfferConfiguration.cs` | FKs to UserAccount (proposer, approver), ConversionRate; indexes on ProviderType+ProviderId, ApprovalStatus, ValidFrom/To |
| `OfferTransactionConfiguration.cs` | UNIQUE on OfferCode; FKs to ProviderOffer, UserAccount (user, confirmer); indexes on Status, CodeExpiresAt, CompletedAt |
| `ProviderSettlementConfiguration.cs` | UNIQUE on (ProviderType, ProviderId, PeriodYear, PeriodMonth); index on SettlementStatus |

### Update `IApplicationDbContext.cs` — Add 4 DbSets:
```csharp
DbSet<PointsConversionRate> PointsConversionRates { get; set; }
DbSet<ProviderOffer> ProviderOffers { get; set; }
DbSet<OfferTransaction> OfferTransactions { get; set; }
DbSet<ProviderSettlement> ProviderSettlements { get; set; }
```

---

## 13. Offers & Transactions Application Layer (CQRS)

### 13.1 Commands

| Folder Path | Command | Description |
|-------------|---------|-------------|
| `Application/Offers/Commands/ProposeOffer/` | `ProposeOfferCommand` | Provider proposes a new offer (status = Pending) |
| `Application/Offers/Commands/ApproveOffer/` | `ApproveOfferCommand` | Admin approves an offer |
| `Application/Offers/Commands/RejectOffer/` | `RejectOfferCommand` | Admin rejects an offer |
| `Application/Offers/Commands/UpdateOffer/` | `UpdateOfferCommand` | Provider/Admin updates offer details |
| `Application/Offers/Commands/DeactivateOffer/` | `DeactivateOfferCommand` | Deactivate an offer |
| `Application/Offers/Commands/InitiateOfferTransaction/` | `InitiateOfferTransactionCommand` | User taps "Use Offer" → generates code |
| `Application/Offers/Commands/ConfirmOfferTransaction/` | `ConfirmOfferTransactionCommand` | Provider enters code + amount → completes transaction |
| `Application/Offers/Commands/CancelOfferTransaction/` | `CancelOfferTransactionCommand` | Admin/User cancels an initiated transaction |
| `Application/Offers/Commands/ExpireOfferTransactions/` | `ExpireOfferTransactionsCommand` | HangFire: expire codes past CodeExpiresAt |
| `Application/Offers/Commands/GenerateSettlement/` | `GenerateSettlementCommand` | Admin/HangFire: generate monthly settlement for a provider |
| `Application/Offers/Commands/UpdateSettlementStatus/` | `UpdateSettlementStatusCommand` | Admin: mark settlement as Invoiced/Paid/Disputed |
| `Application/ConversionRates/Commands/CreateConversionRate/` | `CreateConversionRateCommand` | Admin creates a conversion rate |
| `Application/ConversionRates/Commands/UpdateConversionRate/` | `UpdateConversionRateCommand` | Admin updates a conversion rate |

### 13.2 Queries

| Folder Path | Query | Returns |
|-------------|-------|---------|
| `Application/Offers/Queries/GetActiveOffers/` | `GetActiveOffersRequest` | Active approved offers (user-facing, filterable by ProviderType, location) |
| `Application/Offers/Queries/GetOfferById/` | `GetOfferByIdRequest` | Single offer with details |
| `Application/Offers/Queries/GetOffersForProvider/` | `GetOffersForProviderRequest` | All offers for a specific provider |
| `Application/Offers/Queries/GetPendingOffers/` | `GetPendingOffersRequest` | Admin: offers awaiting approval |
| `Application/Offers/Queries/GetMyOfferTransactions/` | `GetMyOfferTransactionsRequest` | User's transaction history |
| `Application/Offers/Queries/GetProviderTransactions/` | `GetProviderTransactionsRequest` | Provider owner: transactions at their location |
| `Application/Offers/Queries/GetTransactionByCode/` | `GetTransactionByCodeRequest` | Lookup transaction by offer code (for provider confirmation) |
| `Application/Offers/Queries/GetSettlements/` | `GetSettlementsRequest` | Admin: all settlements (filterable by status, month) |
| `Application/Offers/Queries/GetProviderSettlement/` | `GetProviderSettlementRequest` | Settlement details for a specific provider/month |
| `Application/Offers/Queries/GetSettlementSummary/` | `GetSettlementSummaryRequest` | Admin dashboard: totals across all providers |
| `Application/ConversionRates/Queries/GetAllConversionRates/` | `GetAllConversionRatesRequest` | All conversion rates |

---

## 14. Offers & Transactions API Routes

### New File: `WebApi/Routes/OfferRoutes.cs`

```
/api/offers (route group, tag: "Offers")

  User Endpoints (RequireAuthorization):
  =======================================
  GET  /                            -> GetActiveOffersRequest
       ?providerType=&lat=&lng=       Returns: List<OfferDto>

  GET  /{id}                        -> GetOfferByIdRequest
                                       Returns: OfferDetailDto

  POST /{id}/use                    -> InitiateOfferTransactionCommand
                                       Returns: { offerCode, expiresAt }

  GET  /my-transactions             -> GetMyOfferTransactionsRequest
       ?status=                        Returns: List<OfferTransactionDto>

  POST /transactions/cancel/{id}    -> CancelOfferTransactionCommand
                                       Returns: 200


  Provider Endpoints (RequireAuthorization + Owner/Station role):
  ===============================================================
  POST /propose                     -> ProposeOfferCommand
       Body: { title, providerType, providerId, commission%, points%, ... }
                                       Returns: int (offerId)

  GET  /my-provider-offers          -> GetOffersForProviderRequest
                                       Returns: List<OfferDto>

  GET  /transactions/lookup/{code}  -> GetTransactionByCodeRequest
                                       Returns: OfferTransactionDto (pending confirmation)

  POST /transactions/confirm        -> ConfirmOfferTransactionCommand
       Body: { offerCode, transactionAmount, currencyCode }
                                       Returns: { commissionAmount, pointsAwarded }

  GET  /my-provider-transactions    -> GetProviderTransactionsRequest
       ?month=&year=                   Returns: List<OfferTransactionDto>

  GET  /my-provider-settlements     -> GetProviderSettlementRequest
       ?month=&year=                   Returns: ProviderSettlementDto


  Admin Endpoints (RequireAuthorization + Admin role):
  ====================================================
  PATCH /{id}/approve               -> ApproveOfferCommand
                                       Returns: 200

  PATCH /{id}/reject                -> RejectOfferCommand
       Body: { note }                  Returns: 200

  PUT  /{id}                        -> UpdateOfferCommand
                                       Returns: 200

  PATCH /{id}/deactivate            -> DeactivateOfferCommand
                                       Returns: 200

  GET  /pending                     -> GetPendingOffersRequest
                                       Returns: List<OfferDto>

  GET  /admin/settlements           -> GetSettlementsRequest
       ?status=&month=&year=           Returns: List<ProviderSettlementDto>

  GET  /admin/settlements/summary   -> GetSettlementSummaryRequest
       ?month=&year=                   Returns: SettlementSummaryDto

  PATCH /admin/settlements/{id}/status -> UpdateSettlementStatusCommand
       Body: { status, note }           Returns: 200

  POST /admin/settlements/generate  -> GenerateSettlementCommand
       Body: { year, month }            Returns: int (count of settlements generated)


/api/conversion-rates (route group, tag: "Conversion Rates")

  GET  /                            -> GetAllConversionRatesRequest (Admin)
                                       Returns: List<ConversionRateDto>

  POST /                            -> CreateConversionRateCommand (Admin)
                                       Returns: int (rateId)

  PUT  /{id}                        -> UpdateConversionRateCommand (Admin)
                                       Returns: 200
```

---

## 15. Offers & Transactions Flow Diagrams

### Flow 1: Provider Proposes an Offer
```
Provider (Station/Service Owner)          Admin
         |                                  |
         | POST /api/offers/propose         |
         | { title: "Charge & Earn",        |
         |   providerType: "ChargingPoint", |
         |   providerId: 42,                |
         |   commissionPercentage: 10,      |
         |   pointsRewardPercentage: 5,     |
         |   validFrom: "2026-03-01",       |
         |   validTo: "2026-06-01" }        |
         |                                  |
         | → Status = Pending               |
         |                                  |
         | Notification: "New offer pending"|
         |                           ------>|
         |                                  | GET /api/offers/pending
         |                                  | Reviews offer details
         |                                  |
         |                                  | PATCH /api/offers/5/approve
         |                                  |
         | Notification: "Offer approved!"  |
         |<------                           |
         |                                  |
         | Offer is now LIVE in the app     |
```

### Flow 2: User Uses an Offer
```
User                     Provider Dashboard        System
  |                              |                    |
  | Sees offer in app            |                    |
  | POST /api/offers/5/use       |                    |
  |----------------------------->|                    |
  |                              |                    |
  | Response:                    |                    |
  | { code: "CBL-7X9K2M",       |                    |
  |   expiresAt: "14:30:00" }   |                    |
  |                              |                    |
  | Goes to provider physically  |                    |
  | "I'm from Cable, CBL-7X9K2M"|                    |
  |                              |                    |
  |    GET /transactions/lookup/CBL-7X9K2M            |
  |                              |-------------------> |
  |                              | Response:           |
  |                              | { user: "Ahmed",    |
  |                              |   offer: "Charge.." |
  |                              |   status: Initiated }|
  |                              |                     |
  | User pays 10 KWD             |                    |
  |                              |                    |
  |    POST /transactions/confirm                     |
  |    { code: "CBL-7X9K2M",    |                    |
  |      amount: 10,             |                    |
  |      currency: "KWD" }       |                    |
  |                              |-------------------> |
  |                              |                    | Calculates:
  |                              |                    | Commission: 1.00 KWD
  |                              |                    | Points: 5
  |                              |                    | Awards points to user wallet
  |                              |                    | Updates settlement ledger
  |                              |                    |
  | Notification:                |                    |
  | "You earned 5 points!"       |                    |
  |<------------------------------------------------- |
  |                              |                    |
  |          Notification:       |                    |
  |          "Transaction done"  |                    |
  |          <-------------------|                    |
```

### Flow 3: Monthly Settlement
```
End of Month (HangFire or Admin)         Admin
         |                                  |
         | GenerateSettlementCommand        |
         | { year: 2026, month: 3 }         |
         |                                  |
         | For each provider:               |
         |   Sum completed transactions     |
         |   Create ProviderSettlement      |
         |   Status = Pending               |
         |                                  |
         |                                  | GET /admin/settlements?month=3&year=2026
         |                                  | Sees: Kuwait EV Hub owes 150 KWD
         |                                  |
         |                                  | Sends invoice to provider
         |                                  | PATCH /admin/settlements/1/status
         |                                  | { status: "Invoiced" }
         |                                  |
         |                                  | Provider pays
         |                                  | PATCH /admin/settlements/1/status
         |                                  | { status: "Paid", paidAmount: 150.000 }
```

### Notification Integration for Offers

| Event | Recipient | NotificationTypeId | Message |
|-------|-----------|-------------------|---------|
| Offer proposed | Admin | 1 (System) | "New offer pending approval: {title}" |
| Offer approved | Provider owner | 1 (System) | "Your offer '{title}' has been approved!" |
| Offer rejected | Provider owner | 1 (System) | "Your offer '{title}' was rejected: {note}" |
| Transaction initiated | - | - | No notification (user already has the code) |
| Transaction completed | User | 10 (point_received) | "You earned {points} points at {provider}!" |
| Transaction completed | Provider | 1 (System) | "Transaction confirmed. Code: {code}, Amount: {amount}" |
| Code about to expire | User | 1 (System) | "Your offer code expires in 5 minutes!" (optional) |
| Settlement generated | Provider | 1 (System) | "Your monthly settlement is ready: {amount} KWD" |

---

# PHASE 1: LOYALTY & POINTS SYSTEM

> **Built on top of Phase 0 and Phase 0.5.** The loyalty system now supports both ChargingPoints AND ServiceProviders as reward targets, AND integrates with the offer transaction system for earning points from real spending.

---

## 16. Loyalty Overview

A points-based loyalty system where **users earn points** for actions within the Cable app (rating stations, rating services, sharing links, adding favorites, etc.) and can **redeem points for rewards** at **specific charging points, specific service providers, or system-wide**.

### Seasonal Tier Model
- Tiers are **seasonal** — Admin defines seasons (e.g., 3 months, 6 months)
- At the end of each season, **all user tiers reset to Bronze**
- Users must stay active every season to maintain or climb tiers
- **Points balance (wallet) does NOT reset** — users keep earned points to redeem rewards across seasons
- Season-end bonus points awarded based on final tier reached

### What Resets vs. What Stays

| Data | Resets Each Season? | Why |
|------|---------------------|-----|
| Tier Level | YES — back to Bronze | Forces continuous engagement |
| Season Points Earned | YES — starts at 0 | This determines tier within the season |
| Points Balance (wallet) | NO — permanent | Fair: users earned them, let them spend anytime |
| TotalPointsEarned (lifetime) | NO — permanent | For analytics & admin reporting |
| Transaction History | NO — permanent | Full audit trail always preserved |

### Provider-Linked Rewards
Users can spend points at:
- **Specific ChargingPoint** — "Free charge at Kuwait EV Hub" (200 pts)
- **Specific ServiceProvider** — "Free wash at SparkClean" (150 pts)
- **Any provider in a category** — "10% off any car wash" (100 pts)
- **System-wide** — "Priority Access Badge" (500 pts)

### Business Goals
- Increase user engagement and retention
- Encourage ratings and reviews on BOTH charging points and services
- Promote sharing and referrals
- Reward loyal users
- **Drive traffic to specific providers** via targeted rewards
- **Seasonal reset drives recurring engagement**

### Compatibility
- Follows existing **Clean Architecture + CQRS** pattern
- All entities extend `BaseAuditableEntity`
- Uses **MediatR** for commands/queries
- Uses existing **ICurrentUserService** for authenticated user context
- Integrates with existing **NotificationInbox** system
- Soft delete pattern (`IsDeleted`) consistent across all tables
- Uses existing **HangFire** for season-end background jobs

---

## 17. Loyalty System Architecture

```
+-----------------------------------------------------------------+
|                        API Routes                                |
|             LoyaltyRoutes.cs                                     |
|  /api/loyalty/my-account    /api/loyalty/rewards                 |
|  /api/loyalty/my-history    /api/loyalty/rewards/{id}/redeem     |
|  /api/loyalty/leaderboard   /api/loyalty/admin/seasons           |
|  /api/loyalty/admin/adjust-points                                |
+---------------------------+-------------------------------------+
                            |
+---------------------------v-------------------------------------+
|                     Application Layer                            |
|                                                                  |
|  Commands:                         Queries:                      |
|  +- AwardPointsCommand             +- GetMyLoyaltyAccountRequest |
|  +- RedeemRewardCommand            +- GetMyPointsHistoryRequest  |
|  +- CancelRedemptionCommand        +- GetAvailableRewardsRequest |
|  +- AdminAdjustPointsCommand       +- GetMyRedemptionsRequest    |
|  +- CreateSeasonCommand            +- GetLeaderboardRequest      |
|  +- EndSeasonCommand               +- GetCurrentSeasonRequest    |
|  +- CreateRewardCommand            +- GetSeasonHistoryRequest    |
|                                                                  |
|  Service:                                                        |
|  +- ILoyaltyPointService (called from existing handlers)         |
+---------------------------+-------------------------------------+
                            |
+---------------------------v-------------------------------------+
|                     Domain Entities                               |
|                                                                  |
|  LoyaltyPointAction | UserLoyaltyAccount | LoyaltyTier           |
|  LoyaltyPointTransaction | LoyaltyReward | UserRewardRedemption  |
|  LoyaltySeason | UserSeasonProgress                              |
+---------------------------+-------------------------------------+
                            |
+---------------------------v-------------------------------------+
|                     Database (SQL Server)                         |
|  dbo.LoyaltyPointAction      | dbo.UserLoyaltyAccount           |
|  dbo.LoyaltyPointTransaction | dbo.LoyaltyTier                  |
|  dbo.LoyaltyReward           | dbo.UserRewardRedemption         |
|  dbo.LoyaltySeason           | dbo.UserSeasonProgress           |
+-----------------------------------------------------------------+
```

### How Points Flow (with Seasons)

```
User does action (e.g., rates a service provider)
        |
        v
Handler (RateServiceProvider)
        |
        v
Calls ILoyaltyPointService.AwardPointsAsync(userId, "RATE_SERVICE", "ServiceProvider", providerId)
        |
        v
AwardPointsCommand Handler:
  1. Get current active LoyaltySeason
     -> If no active season -> return 0

  2. Lookup LoyaltyPointAction by ActionCode
     -> If not found or not active -> return 0

  3. Check daily limit, lifetime limit, duplicate check

  4. Find or create UserLoyaltyAccount (wallet)
  5. Find or create UserSeasonProgress for current season

  6. Get tier multiplier -> calculate points

  7. Create LoyaltyPointTransaction (Type=Earn, SeasonId=current)

  8. Update wallet: TotalPointsEarned += points, CurrentBalance += points
  9. Update season: SeasonPointsEarned += points

  10. Recalculate season tier -> notify if upgraded

  11. SaveChanges
  12. Return points awarded
```

### How Reward Redemption Works (Provider-Linked)

```
User wants to redeem "Free Wash at SparkClean" (150 pts)
        |
        v
POST /api/loyalty/rewards/5/redeem
        |
        v
RedeemRewardCommand Handler:
  1. Get LoyaltyReward (Id=5)
     -> ProviderType = "ServiceProvider"
     -> ProviderId = 15 (SparkClean)
     -> PointsCost = 150

  2. Validate: reward active, within dates, not maxed out

  3. Get UserLoyaltyAccount -> validate CurrentBalance >= 150

  4. Debit wallet: CurrentBalance -= 150

  5. Create UserRewardRedemption (Status = Pending)
     -> ServiceProvider owner gets notified
     -> User shows redemption code at the service

  6. When service is delivered:
     -> Admin/Owner marks redemption as Fulfilled
```

---

## 18. Loyalty Database Design

### 18.1 `dbo.LoyaltyPointAction` — Defines what actions earn points

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `Id` | int (PK, Identity) | No | Auto-increment primary key |
| `ActionCode` | nvarchar(100) | No | Unique code. e.g., `RATE_STATION`, `RATE_SERVICE` |
| `Name` | nvarchar(255) | No | Display name (supports Arabic) |
| `Description` | nvarchar(500) | Yes | Details about the action |
| `Points` | int | No | Base points awarded per action |
| `MaxPerDay` | int | Yes | Max times per day (NULL = unlimited) |
| `MaxPerLifetime` | int | Yes | Max times ever (NULL = unlimited) |
| `IsActive` | bit | No | Can be disabled without deleting |
| `CreatedAt` | datetime | No | BaseAuditableEntity |
| `CreatedBy` | int | Yes | BaseAuditableEntity |
| `ModifiedAt` | datetime | Yes | BaseAuditableEntity |
| `ModifiedBy` | int | Yes | BaseAuditableEntity |
| `IsDeleted` | bit | No | Soft delete (BaseAuditableEntity) |

**Indexes:**
- `IX_LoyaltyPointAction_ActionCode_Unique` — UNIQUE on `ActionCode`

---

### 18.2 `dbo.LoyaltyTier` — Tier definitions (seasonal thresholds)

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `Id` | int (PK, Identity) | No | Auto-increment primary key |
| `Name` | nvarchar(100) | No | Tier name: Bronze, Silver, Gold, Platinum |
| `MinPoints` | int | No | Minimum **season** points to reach this tier |
| `Multiplier` | float | No | Points multiplier. e.g., 1.0x, 1.5x, 2.0x |
| `BonusPoints` | int | No | Bonus points awarded at season end. Default 0 |
| `IconUrl` | nvarchar(500) | Yes | Badge icon for the mobile app |
| `IsActive` | bit | No | Default true |

**No soft delete** — static reference table.

---

### 18.3 `dbo.LoyaltySeason` — Season definitions

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `Id` | int (PK, Identity) | No | Auto-increment primary key |
| `Name` | nvarchar(255) | No | e.g., "Season 1 - Spring 2026" |
| `Description` | nvarchar(500) | Yes | Optional description |
| `StartDate` | datetime | No | When the season begins |
| `EndDate` | datetime | No | When the season ends |
| `IsActive` | bit | No | Only ONE season active at a time |
| `CreatedAt` | datetime | No | BaseAuditableEntity |
| `CreatedBy` | int | Yes | BaseAuditableEntity |
| `ModifiedAt` | datetime | Yes | BaseAuditableEntity |
| `ModifiedBy` | int | Yes | BaseAuditableEntity |
| `IsDeleted` | bit | No | Soft delete (BaseAuditableEntity) |

**Indexes:**
- `IX_LoyaltySeason_IsActive` — fast lookup for current season
- `IX_LoyaltySeason_StartDate_EndDate` — date range queries

---

### 18.4 `dbo.UserSeasonProgress` — User's progress within a season

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `Id` | int (PK, Identity) | No | Auto-increment primary key |
| `UserId` | int (FK -> UserAccount) | No | Which user |
| `LoyaltySeasonId` | int (FK -> LoyaltySeason) | No | Which season |
| `SeasonPointsEarned` | int | No | Points earned in THIS season only |
| `TierLevel` | int (FK -> LoyaltyTier) | No | Current tier in this season (default 1 = Bronze) |
| `CreatedAt` | datetime | No | BaseAuditableEntity |
| `CreatedBy` | int | Yes | BaseAuditableEntity |
| `ModifiedAt` | datetime | Yes | BaseAuditableEntity |
| `ModifiedBy` | int | Yes | BaseAuditableEntity |
| `IsDeleted` | bit | No | Soft delete (BaseAuditableEntity) |

**Indexes:**
- `IX_UserSeasonProgress_UserId_SeasonId_Unique` — UNIQUE on (UserId, LoyaltySeasonId)
- `IX_UserSeasonProgress_LoyaltySeasonId` — season leaderboard queries
- `IX_UserSeasonProgress_SeasonPointsEarned` — leaderboard sorting

**Foreign Keys:**
- `FK_UserSeasonProgress_UserAccount` -> `dbo.UserAccount(Id)`
- `FK_UserSeasonProgress_LoyaltySeason` -> `dbo.LoyaltySeason(Id)`
- `FK_UserSeasonProgress_LoyaltyTier` -> `dbo.LoyaltyTier(Id)`

---

### 18.5 `dbo.UserLoyaltyAccount` — Permanent wallet

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `Id` | int (PK, Identity) | No | Auto-increment primary key |
| `UserId` | int (FK -> UserAccount) | No | **Unique** — one per user |
| `TotalPointsEarned` | int | No | Lifetime total earned (never resets) |
| `TotalPointsRedeemed` | int | No | Lifetime total spent |
| `CurrentBalance` | int | No | Spendable points |
| `CreatedAt` | datetime | No | BaseAuditableEntity |
| `CreatedBy` | int | Yes | BaseAuditableEntity |
| `ModifiedAt` | datetime | Yes | BaseAuditableEntity |
| `ModifiedBy` | int | Yes | BaseAuditableEntity |
| `IsDeleted` | bit | No | Soft delete (BaseAuditableEntity) |

**Indexes:**
- `IX_UserLoyaltyAccount_UserId_Unique` — UNIQUE on `UserId`
- `IX_UserLoyaltyAccount_CurrentBalance` — leaderboard

---

### 18.6 `dbo.LoyaltyPointTransaction` — Full audit trail

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `Id` | int (PK, Identity) | No | Auto-increment primary key |
| `UserLoyaltyAccountId` | int (FK) | No | Which user's account |
| `LoyaltyPointActionId` | int (FK) | Yes | Which action (NULL for redeem/admin/bonus) |
| `LoyaltySeasonId` | int (FK) | Yes | Which season (NULL for redeem/admin) |
| `TransactionType` | int | No | Enum: 1=Earn, 2=Redeem, 3=Expired, 4=AdminAdjust, 5=SeasonBonus |
| `Points` | int | No | + for earn/bonus, - for redeem/expire |
| `BalanceAfter` | int | No | Snapshot of balance after transaction |
| `ReferenceType` | nvarchar(100) | Yes | "ChargingPoint", "ServiceProvider", "Rate", "SharedLink" |
| `ReferenceId` | int | Yes | The specific entity Id |
| `Note` | nvarchar(500) | Yes | Human-readable note |
| `ExpiresAt` | datetime | Yes | NULL = never expires |
| `CreatedAt` | datetime | No | BaseAuditableEntity |
| `CreatedBy` | int | Yes | BaseAuditableEntity |
| `ModifiedAt` | datetime | Yes | BaseAuditableEntity |
| `ModifiedBy` | int | Yes | BaseAuditableEntity |
| `IsDeleted` | bit | No | Soft delete (BaseAuditableEntity) |

**Indexes:**
- `IX_LoyaltyPointTransaction_UserLoyaltyAccountId` — fast lookup by account
- `IX_LoyaltyPointTransaction_LoyaltySeasonId` — filter by season
- `IX_LoyaltyPointTransaction_CreatedAt` — history pagination
- `IX_LoyaltyPointTransaction_TransactionType` — filter by type
- `IX_LoyaltyPointTransaction_ReferenceType_ReferenceId` — prevent duplicate awards

---

### 18.7 `dbo.LoyaltyReward` — Provider-linked rewards (UPDATED)

> **KEY CHANGE:** Rewards can now be linked to specific ChargingPoints or ServiceProviders.

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `Id` | int (PK, Identity) | No | Auto-increment primary key |
| `Name` | nvarchar(255) | No | Reward display name |
| `Description` | nvarchar(1000) | Yes | Details about the reward |
| `PointsCost` | int | No | How many points to redeem |
| `RewardType` | int | No | Enum: 1=Discount, 2=FreeCharge, 3=FreeService, 4=PriorityAccess, 5=Badge |
| `RewardValue` | nvarchar(500) | Yes | Type-dependent value (e.g., "10%", "1 free wash") |
| `ProviderType` | nvarchar(50) | Yes | **NEW:** "ChargingPoint", "ServiceProvider", NULL = system-wide |
| `ProviderId` | int | Yes | **NEW:** Specific provider Id, NULL = any provider of that type |
| `ServiceCategoryId` | int (FK) | Yes | **NEW:** If set, reward valid for any provider in this category |
| `MaxRedemptions` | int | Yes | Total available (NULL = unlimited) |
| `CurrentRedemptions` | int | No | How many have been redeemed |
| `ImageUrl` | nvarchar(500) | Yes | Reward image for mobile app |
| `IsActive` | bit | No | |
| `ValidFrom` | datetime | No | When reward becomes available |
| `ValidTo` | datetime | Yes | When reward expires (NULL = no expiry) |
| `CreatedAt` | datetime | No | BaseAuditableEntity |
| `CreatedBy` | int | Yes | BaseAuditableEntity |
| `ModifiedAt` | datetime | Yes | BaseAuditableEntity |
| `ModifiedBy` | int | Yes | BaseAuditableEntity |
| `IsDeleted` | bit | No | Soft delete (BaseAuditableEntity) |

**Indexes:**
- `IX_LoyaltyReward_IsActive_ValidFrom_ValidTo` — available rewards
- `IX_LoyaltyReward_RewardType` — filter by type
- `IX_LoyaltyReward_ProviderType_ProviderId` — rewards for specific provider
- `IX_LoyaltyReward_ServiceCategoryId` — rewards for a category

**Foreign Keys:**
- `FK_LoyaltyReward_ServiceCategory` -> `dbo.ServiceCategory(Id)` (optional)

**Provider Linking Logic:**

| ProviderType | ProviderId | ServiceCategoryId | Meaning |
|-------------|------------|-------------------|---------|
| NULL | NULL | NULL | System-wide reward (badge, priority access) |
| "ChargingPoint" | 42 | NULL | Specific charging station |
| "ChargingPoint" | NULL | NULL | Any charging station |
| "ServiceProvider" | 15 | NULL | Specific service provider (e.g., SparkClean) |
| "ServiceProvider" | NULL | 1 | Any provider in category (e.g., any Car Wash) |
| "ServiceProvider" | NULL | NULL | Any service provider |

**Example Rewards:**

| Name | PointsCost | RewardType | ProviderType | ProviderId | CategoryId | Meaning |
|------|-----------|------------|-------------|------------|------------|---------|
| Free Charge at Kuwait EV Hub | 200 | FreeCharge | ChargingPoint | 42 | NULL | Specific station |
| 10% off any charge | 100 | Discount | ChargingPoint | NULL | NULL | Any station |
| Free wash at SparkClean | 150 | FreeService | ServiceProvider | 15 | NULL | Specific car wash |
| 10% off any car wash | 100 | Discount | ServiceProvider | NULL | 1 | Any car wash provider |
| 50% off any tire service | 200 | Discount | ServiceProvider | NULL | 2 | Any tire service |
| Gold Badge | 500 | Badge | NULL | NULL | NULL | System-wide |

---

### 18.8 `dbo.UserRewardRedemption` — Tracks each redemption

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `Id` | int (PK, Identity) | No | Auto-increment primary key |
| `UserId` | int (FK -> UserAccount) | No | Who redeemed |
| `LoyaltyRewardId` | int (FK -> LoyaltyReward) | No | What was redeemed |
| `LoyaltyPointTransactionId` | int (FK) | No | The debit transaction |
| `PointsSpent` | int | No | Points deducted |
| `Status` | int | No | Enum: 1=Pending, 2=Fulfilled, 3=Cancelled |
| `RedemptionCode` | nvarchar(50) | Yes | **NEW:** Unique code user shows at provider |
| `ProviderType` | nvarchar(50) | Yes | **NEW:** Copied from reward for quick reference |
| `ProviderId` | int | Yes | **NEW:** Copied from reward for quick reference |
| `RedeemedAt` | datetime | No | When user clicked redeem |
| `FulfilledAt` | datetime | Yes | When reward was delivered |
| `CreatedAt` | datetime | No | BaseAuditableEntity |
| `CreatedBy` | int | Yes | BaseAuditableEntity |
| `ModifiedAt` | datetime | Yes | BaseAuditableEntity |
| `ModifiedBy` | int | Yes | BaseAuditableEntity |
| `IsDeleted` | bit | No | Soft delete (BaseAuditableEntity) |

**Indexes:**
- `IX_UserRewardRedemption_UserId` — user's history
- `IX_UserRewardRedemption_LoyaltyRewardId` — redemption count
- `IX_UserRewardRedemption_Status` — filter by status
- `IX_UserRewardRedemption_RedemptionCode` — lookup by code
- `IX_UserRewardRedemption_ProviderType_ProviderId` — provider's redemptions

---

## 19. Loyalty Domain Entities

### Entities (8 files in `Domain/Enitites/`)
1. `LoyaltyPointAction.cs` — BaseAuditableEntity
2. `LoyaltyTier.cs` — Simple lookup (no BaseAuditableEntity)
3. `LoyaltySeason.cs` — BaseAuditableEntity
4. `UserSeasonProgress.cs` — BaseAuditableEntity
5. `UserLoyaltyAccount.cs` — BaseAuditableEntity (wallet, NO tier)
6. `LoyaltyPointTransaction.cs` — BaseAuditableEntity (includes LoyaltySeasonId?)
7. `LoyaltyReward.cs` — BaseAuditableEntity (includes ProviderType, ProviderId, ServiceCategoryId?)
8. `UserRewardRedemption.cs` — BaseAuditableEntity (includes RedemptionCode, ProviderType, ProviderId)

### Update `UserAccount.cs` — Add loyalty navigation properties
```csharp
public virtual UserLoyaltyAccount? LoyaltyAccount { get; set; }
public virtual ICollection<UserSeasonProgress> SeasonProgresses { get; set; } = new List<UserSeasonProgress>();
public virtual ICollection<UserRewardRedemption> RewardRedemptions { get; set; } = new List<UserRewardRedemption>();
```

---

## 20. Loyalty Enums

New enums in `Cable.Core/Enums/`:

### `TransactionType.cs`
```csharp
public enum TransactionType
{
    Earn = 1,
    Redeem = 2,
    Expired = 3,
    AdminAdjust = 4,
    SeasonBonus = 5
}
```

### `RewardType.cs` (UPDATED — added FreeService)
```csharp
public enum RewardType
{
    Discount = 1,
    FreeCharge = 2,
    FreeService = 3,
    PriorityAccess = 4,
    Badge = 5
}
```

### `RedemptionStatus.cs`
```csharp
public enum RedemptionStatus
{
    Pending = 1,
    Fulfilled = 2,
    Cancelled = 3
}
```

---

## 21. Loyalty EF Configurations

8 configuration files in `Infrastructrue/Persistence/Configurations/`:

| File | Key Configuration |
|------|-------------------|
| `LoyaltyPointActionConfiguration.cs` | Unique index on ActionCode |
| `LoyaltyTierConfiguration.cs` | Simple mapping, no audit columns |
| `LoyaltySeasonConfiguration.cs` | Index on IsActive |
| `UserSeasonProgressConfiguration.cs` | Unique on (UserId, LoyaltySeasonId) |
| `UserLoyaltyAccountConfiguration.cs` | Unique on UserId |
| `LoyaltyPointTransactionConfiguration.cs` | FKs + indexes on AccountId, SeasonId, CreatedAt |
| `LoyaltyRewardConfiguration.cs` | Indexes on ProviderType+ProviderId, ServiceCategoryId, IsActive |
| `UserRewardRedemptionConfiguration.cs` | FKs + index on RedemptionCode |

### Update `IApplicationDbContext.cs` — Add 8 DbSets:
```csharp
DbSet<LoyaltyPointAction> LoyaltyPointActions { get; set; }
DbSet<LoyaltyTier> LoyaltyTiers { get; set; }
DbSet<LoyaltySeason> LoyaltySeasons { get; set; }
DbSet<UserSeasonProgress> UserSeasonProgresses { get; set; }
DbSet<UserLoyaltyAccount> UserLoyaltyAccounts { get; set; }
DbSet<LoyaltyPointTransaction> LoyaltyPointTransactions { get; set; }
DbSet<LoyaltyReward> LoyaltyRewards { get; set; }
DbSet<UserRewardRedemption> UserRewardRedemptions { get; set; }
```

---

## 22. Loyalty Application Layer (CQRS)

### 22.1 Core Service: `ILoyaltyPointService`

```csharp
public interface ILoyaltyPointService
{
    // Standard: fixed points from LoyaltyPointAction table
    Task<int> AwardPointsAsync(
        int userId,
        string actionCode,
        string? referenceType = null,
        int? referenceId = null,
        string? note = null,
        CancellationToken cancellationToken = default);

    // Offer-based: dynamic points calculated from transaction amount
    Task<int> AwardPointsFromOfferAsync(
        int userId,
        int calculatedPoints,
        string providerType,
        int providerId,
        int offerTransactionId,
        string? note = null,
        CancellationToken cancellationToken = default);
}
```

### 22.2 Commands

| Command | Description |
|---------|-------------|
| `AwardPointsCommand` | Awards points for an action (season-aware) |
| `RedeemRewardCommand` | User redeems a provider-linked reward (generates RedemptionCode) |
| `CancelRedemptionCommand` | Admin cancels & refunds |
| `FulfillRedemptionCommand` | Admin/Owner marks redemption as delivered |
| `AdminAdjustPointsCommand` | Admin manually adjusts wallet points |
| `CreateSeasonCommand` | Admin creates a new season |
| `EndSeasonCommand` | Admin ends current season (awards bonuses) |
| `CreateRewardCommand` | Admin creates a reward (with optional provider link) |
| `UpdateRewardCommand` | Admin updates a reward |

### 22.3 Queries

| Query | Returns |
|-------|---------|
| `GetMyLoyaltyAccountRequest` | Wallet + current season tier |
| `GetMyPointsHistoryRequest` | Paginated transactions (filterable by season) |
| `GetAvailableRewardsRequest` | Active rewards (filterable by ProviderType, CategoryId) |
| `GetRewardsForProviderRequest` | **NEW:** Rewards for specific ChargingPoint or ServiceProvider |
| `GetMyRedemptionsRequest` | User's redemption history |
| `GetLeaderboardRequest` | Top users by SeasonPointsEarned |
| `GetCurrentSeasonRequest` | Current season + user's progress |
| `GetSeasonHistoryRequest` | Past seasons with final tier + bonus |
| `GetProviderRedemptionsRequest` | **NEW:** Admin/Owner sees redemptions at their provider |

---

## 23. Loyalty Integration with Existing Features

### Point-Earning Actions (expanded for services)

| Action Code | Trigger | Points |
|-------------|---------|--------|
| `RATE_STATION` | Rate a ChargingPoint | 10 |
| `RATE_SERVICE` | **NEW:** Rate a ServiceProvider | 10 |
| `SHARE_LINK` | Share a station link | 15 |
| `SHARE_SERVICE` | **NEW:** Share a service provider link | 15 |
| `ADD_FAVORITE` | Favorite a ChargingPoint | 5 |
| `ADD_FAVORITE_SERVICE` | **NEW:** Favorite a ServiceProvider | 5 |
| `USE_OFFER` | **NEW (Phase 0.5):** Complete an offer transaction | Dynamic (calculated from amount × rate) |
| `COMPLETE_PROFILE` | Complete profile (one-time) | 50 |
| `VERIFY_PHONE` | Verify phone (one-time) | 30 |
| `DAILY_LOGIN` | Daily login | 3 |
| `REFER_FRIEND` | Refer a new user | 100 |
| `FIRST_RATE` | First-ever rating (one-time) | 25 |

> **Note on `USE_OFFER`:** Unlike other actions that have fixed points, `USE_OFFER` points are **dynamically calculated** from the offer's `PointsRewardPercentage` × `TransactionAmount` × `PointsConversionRate`. The `LoyaltyPointAction.Points` field for `USE_OFFER` is set to 0 (base points) since the actual amount comes from the offer transaction calculation. The `ILoyaltyPointService` has a special overload for this.

### Integration Points

| Handler | Calls |
|---------|-------|
| `AddToFavoritesCommand` (existing) | `AwardPoints("ADD_FAVORITE", "ChargingPoint", cpId)` |
| `AddToFavoriteServiceCommand` (new) | `AwardPoints("ADD_FAVORITE_SERVICE", "ServiceProvider", spId)` |
| `RateChargingPoint` (existing) | `AwardPoints("RATE_STATION", "ChargingPoint", cpId)` |
| `RateServiceProviderCommand` (new) | `AwardPoints("RATE_SERVICE", "ServiceProvider", spId)` |
| `VerifyUserPhoneCommand` (existing) | `AwardPoints("VERIFY_PHONE")` |
| `ConfirmOfferTransactionCommand` **(Phase 0.5)** | `AwardPointsFromOffer(userId, "USE_OFFER", calculatedPoints, providerType, providerId)` |

---

## 24. Loyalty API Routes

### `WebApi/Routes/LoyaltyRoutes.cs`

```
/api/loyalty (route group, tag: "Loyalty")

  User Endpoints (RequireAuthorization):
  =======================================
  GET  /my-account              -> wallet + season tier
  GET  /my-history?seasonId=    -> paginated transactions
  GET  /rewards                 -> all available rewards
  GET  /rewards?providerType=ServiceProvider&providerId=15
                                -> rewards for specific provider
  GET  /rewards?categoryId=1    -> rewards for any car wash
  POST /rewards/{id}/redeem     -> redeem (returns redemptionCode)
  GET  /my-redemptions          -> user's redemptions
  GET  /current-season          -> current season + progress
  GET  /season-history          -> past seasons
  GET  /leaderboard?top=10      -> season leaderboard

  Admin Endpoints:
  ================
  POST /admin/seasons           -> create season
  POST /admin/seasons/end       -> end current season
  POST /admin/adjust-points     -> adjust user wallet
  POST /admin/rewards           -> create reward (with provider link)
  PUT  /admin/rewards/{id}      -> update reward
  GET  /admin/rewards           -> all rewards (including inactive)
  PATCH /admin/redemptions/{id}/fulfill -> mark redemption fulfilled
  PATCH /admin/redemptions/{id}/cancel  -> cancel & refund
  GET  /admin/redemptions?providerType=&providerId= -> redemptions at provider
```

---

## 25. Notification Integration

Uses existing `NotificationType` entries:

| NotificationTypeId | Name | When Used |
|--------------------|------|-----------|
| 7 | `achievement_unlocked` | Tier upgrade within season |
| 10 | `point_received` | Points earned for any action |
| 1 | `System_announcement` | Season start/end |

### Notification Examples

**Points from Service Rating:**
```json
{
  "NotificationTypeId": 10,
  "Title": "Points Earned! +10",
  "Body": "You earned 10 points for rating SparkClean Car Wash.",
  "Data": "{\"points\": 10, \"actionCode\": \"RATE_SERVICE\", \"providerType\": \"ServiceProvider\"}"
}
```

**Reward Redeemed (to user):**
```json
{
  "NotificationTypeId": 1,
  "Title": "Reward Redeemed!",
  "Body": "Your redemption code for Free Wash at SparkClean: ABC123. Show this at the service.",
  "Data": "{\"redemptionCode\": \"ABC123\", \"providerType\": \"ServiceProvider\", \"providerId\": 15}"
}
```

**Redemption received (to provider owner):**
```json
{
  "NotificationTypeId": 1,
  "Title": "New Reward Redemption!",
  "Body": "User redeemed 'Free Wash' at your service. Code: ABC123.",
  "Data": "{\"redemptionCode\": \"ABC123\", \"rewardName\": \"Free Wash\"}"
}
```

---

## 26. Seed Data

### ServiceCategory
| Id | Name | NameAr | SortOrder |
|----|------|--------|-----------|
| 1 | Car Wash | غسيل سيارات | 1 |
| 2 | Tire Service | خدمة إطارات | 2 |
| 3 | Oil Change | تغيير زيت | 3 |
| 4 | Towing | سحب سيارات | 4 |
| 5 | Car Detailing | تنظيف وتلميع | 5 |
| 6 | Battery Service | خدمة بطارية | 6 |

### PointsConversionRate
| Id | Name | CurrencyCode | PointsPerUnit | IsDefault | IsActive |
|----|------|-------------|---------------|-----------|----------|
| 1 | Default Rate | KWD | 10.0 | true | true |

### LoyaltyPointAction (expanded for services + offers)
| ActionCode | Name | Points | MaxPerDay | MaxPerLifetime |
|------------|------|--------|-----------|----------------|
| RATE_STATION | Rate a charging station | 10 | 5 | NULL |
| RATE_SERVICE | Rate a service provider | 10 | 5 | NULL |
| SHARE_LINK | Share a station link | 15 | 3 | NULL |
| SHARE_SERVICE | Share a service link | 15 | 3 | NULL |
| ADD_FAVORITE | Add station to favorites | 5 | 10 | NULL |
| ADD_FAVORITE_SERVICE | Add service to favorites | 5 | 10 | NULL |
| USE_OFFER | Use an offer at a provider | 0 (dynamic) | NULL | NULL |
| COMPLETE_PROFILE | Complete your profile | 50 | NULL | 1 |
| VERIFY_PHONE | Verify phone number | 30 | NULL | 1 |
| DAILY_LOGIN | Daily app login | 3 | 1 | NULL |
| REFER_FRIEND | Refer a new user | 100 | NULL | NULL |
| FIRST_RATE | First-ever rating | 25 | NULL | 1 |

> **`USE_OFFER` note:** Points = 0 because actual points are dynamically calculated from `TransactionAmount × PointsRewardPercentage × ConversionRate`. MaxPerDay/Lifetime = NULL because there's no limit on how many offers a user can use.

### LoyaltyTier
| Name | MinPoints | Multiplier | BonusPoints |
|------|-----------|------------|-------------|
| Bronze | 0 | 1.0 | 0 |
| Silver | 500 | 1.5 | 50 |
| Gold | 2000 | 2.0 | 150 |
| Platinum | 5000 | 3.0 | 300 |

---

## 27. Full Implementation Order

### Phase 0: Service Provider Foundation

| Step | Task | Files |
|------|------|-------|
| **0.1** | Create `ServiceCategory` entity | `Domain/Enitites/ServiceCategory.cs` |
| **0.2** | Create `ServiceProvider` entity | `Domain/Enitites/ServiceProvider.cs` |
| **0.3** | Create `ServiceProviderAttachment` entity | `Domain/Enitites/ServiceProviderAttachment.cs` |
| **0.4** | Create `ServiceProviderRate` entity | `Domain/Enitites/ServiceProviderRate.cs` |
| **0.5** | Create `UserFavoriteServiceProvider` entity | `Domain/Enitites/UserFavoriteServiceProvider.cs` |
| **0.6** | Update `UserAccount.cs` | Add service provider navigation properties |
| **0.7** | Create EF Configurations (5 files) | `Infrastructrue/Persistence/Configurations/` |
| **0.8** | Update `IApplicationDbContext` + `ApplicationDbContext` | Add 5 DbSets |
| **0.9** | Create Migration | `dotnet ef migrations add AddServiceProviders` |
| **0.10** | Run Migration on Dev DB | `dotnet ef database update` |
| **0.11** | Seed ServiceCategory data | Insert 6 categories |
| **0.12** | Create Service Provider Commands | Create, Update, Delete, Verify, Rate, Favorite |
| **0.13** | Create Service Provider Queries | GetAll, GetById, ByCategory, Nearby, Favorites, Ratings |
| **0.14** | Create `ServiceProviderRoutes.cs` | Register in `Program.cs` |
| **0.15** | Test Service Providers on Dev DB | CRUD + ratings + favorites |

### Phase 0.5: Offers & Transactions

| Step | Task | Files |
|------|------|-------|
| **0.5.1** | Create Enums | `OfferApprovalStatus.cs`, `OfferTransactionStatus.cs`, `SettlementStatus.cs` |
| **0.5.2** | Create `PointsConversionRate` entity | `Domain/Enitites/PointsConversionRate.cs` |
| **0.5.3** | Create `ProviderOffer` entity | `Domain/Enitites/ProviderOffer.cs` |
| **0.5.4** | Create `OfferTransaction` entity | `Domain/Enitites/OfferTransaction.cs` |
| **0.5.5** | Create `ProviderSettlement` entity | `Domain/Enitites/ProviderSettlement.cs` |
| **0.5.6** | Update `UserAccount.cs` | Add offer navigation properties |
| **0.5.7** | Create EF Configurations (4 files) | `Infrastructrue/Persistence/Configurations/` |
| **0.5.8** | Update `IApplicationDbContext` + `ApplicationDbContext` | Add 4 DbSets |
| **0.5.9** | Create Migration | `dotnet ef migrations add AddOffersAndTransactions` |
| **0.5.10** | Run Migration on Dev DB | `dotnet ef database update` |
| **0.5.11** | Seed PointsConversionRate data | Insert default rate (1 KWD = 10 points) |
| **0.5.12** | Create Offer Commands | Propose, Approve, Reject, Update, Deactivate |
| **0.5.13** | Create Transaction Commands | Initiate, Confirm, Cancel, Expire |
| **0.5.14** | Create Settlement Commands | Generate, UpdateStatus |
| **0.5.15** | Create ConversionRate Commands | Create, Update |
| **0.5.16** | Create Offer Queries | ActiveOffers, ById, ForProvider, Pending |
| **0.5.17** | Create Transaction Queries | MyTransactions, ProviderTransactions, ByCode |
| **0.5.18** | Create Settlement Queries | GetSettlements, ProviderSettlement, Summary |
| **0.5.19** | Create `OfferRoutes.cs` + `ConversionRateRoutes.cs` | Register in `Program.cs` |
| **0.5.20** | Setup HangFire job for code expiry | Expire initiated transactions past CodeExpiresAt |
| **0.5.21** | Test Offers on Dev DB | Full flow: propose → approve → use → confirm → settlement |

### Phase 1: Loyalty System

| Step | Task | Files |
|------|------|-------|
| **1.1** | Create Enums | `TransactionType.cs`, `RewardType.cs`, `RedemptionStatus.cs` |
| **1.2** | Create Domain Entities (8 files) | Loyalty entities + update `UserAccount.cs` |
| **1.3** | Create EF Configurations (8 files) | Loyalty configurations |
| **1.4** | Update DbContext | Add 8 DbSets |
| **1.5** | Create Migration | `dotnet ef migrations add AddLoyaltySystem` |
| **1.6** | Run Migration on Dev DB | `dotnet ef database update` |
| **1.7** | Seed Data | LoyaltyPointAction (12 actions incl. USE_OFFER) + LoyaltyTier (4 tiers) |
| **1.8** | Create `ILoyaltyPointService` | Interface (both overloads) + Implementation + DI registration |
| **1.9** | Create Loyalty Commands | AwardPoints, Redeem, Cancel, Fulfill, AdminAdjust, CreateSeason, EndSeason, CreateReward |
| **1.10** | Create Loyalty Queries | MyAccount, History, Rewards, Redemptions, Leaderboard, Season, SeasonHistory, ProviderRewards |
| **1.11** | Create `LoyaltyRoutes.cs` | Register in `Program.cs` |
| **1.12** | Integrate with existing handlers | Inject ILoyaltyPointService into Favorites, Rate, SharedLink, VerifyPhone |
| **1.13** | Integrate with new service handlers | Inject into RateServiceProvider, FavoriteService |
| **1.14** | Integrate with offer transactions | Inject ILoyaltyPointService into ConfirmOfferTransactionCommand |
| **1.15** | Setup HangFire jobs | Auto-end season + code expiry (if not done in 0.5) |
| **1.16** | Test full system on Dev DB | End-to-end: earn from actions, earn from offers, redeem at providers, season lifecycle |
| **1.17** | Deploy to Production | Run all 3 migrations |

---

## Summary — Total New Items

| Category | Count | Details |
|----------|-------|---------|
| **New Database Tables** | **17** | 5 service provider + 4 offers/transactions + 8 loyalty |
| **New Domain Entities** | **17** | 5 service provider + 4 offers/transactions + 8 loyalty |
| **New EF Configurations** | **17** | 5 + 4 + 8 |
| **New DbSets** | **17** | 5 + 4 + 8 |
| **New Enums** | **6** | OfferApprovalStatus, OfferTransactionStatus, SettlementStatus + TransactionType, RewardType, RedemptionStatus |
| **New Commands** | **~30** | 9 service provider + 13 offers/transactions + 8 loyalty |
| **New Queries** | **~27** | 7 service provider + 11 offers/transactions + 9 loyalty |
| **New Route Files** | **4** | ServiceProviderRoutes, ServiceCategoryRoutes, OfferRoutes, LoyaltyRoutes |
| **Updated Existing Files** | **~10** | UserAccount (3 phases), IApplicationDbContext, ApplicationDbContext, Program.cs, existing handlers |
| **EF Migrations** | **3** | AddServiceProviders + AddOffersAndTransactions + AddLoyaltySystem |
| **HangFire Jobs** | **2-3** | Code expiry, season end, (optional) monthly settlement |

---

### Phase Dependency Diagram

```
Phase 0: Service Providers          Phase 0.5: Offers & Transactions
(ServiceCategory, ServiceProvider,   (ProviderOffer, OfferTransaction,
 Attachments, Rates, Favorites)       ProviderSettlement, ConversionRate)
         |                                      |
         | Depends on ServiceProvider            | Depends on ProviderType+ProviderId
         | for ProviderType="ServiceProvider"    | from both ChargingPoint & ServiceProvider
         |                                      |
         +------------------+-------------------+
                            |
                            v
                   Phase 1: Loyalty System
                   (LoyaltyPointAction, Tiers, Seasons,
                    Wallet, Transactions, Rewards, Redemptions)

                   Integrates with:
                   - Phase 0: Rate/Favorite service → earn points
                   - Phase 0.5: Offer transactions → earn points from spending
                   - Existing: Rate/Favorite charging → earn points
```

---

> **Next Step:** Review this plan. Once you say "okay", we start with Phase 0, Step 0.1 (ServiceCategory entity).
