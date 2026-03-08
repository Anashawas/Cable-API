# Cable - Complete Feature Plan & Workflow Documentation

## What Was Built

This document explains everything we planned and implemented across **4 phases** + **Service Provider enhancements**. It covers the full workflow, how features connect, and how data flows between users, providers, and admins.

---

# Phase 0: Service Providers & Categories

## What Was Built
A full service provider ecosystem where owners can register their businesses (charging stations, car wash, tire shops, etc.), categorized under service categories.

### Entities Created
| Entity | Purpose |
|--------|---------|
| `ServiceCategory` | Categories like "EV Charging", "Car Wash", etc. (bilingual) |
| `ServiceProvider` | The actual business listing with location, pricing, hours |
| `ServiceProviderRating` | User ratings and comments on providers |
| `FavoriteService` | User's favorite providers |
| `ServiceProviderAttachment` | Images/files attached to a provider |

### Endpoints Built (17 total)

**Public (No Auth):**
| # | Endpoint | What It Does |
|---|----------|-------------|
| 1 | `GET /api/service-categories/GetAllServiceCategories` | List all categories |
| 2 | `GET /api/service-providers/GetAllServiceProviders` | List all providers (optional category filter) |
| 3 | `GET /api/service-providers/GetServiceProviderById/{id}` | Get provider details + increment visitor count |
| 4 | `GET /api/service-providers/GetByCategory/{categoryId}` | Providers in a category |
| 5 | `GET /api/service-providers/GetNearby` | Find nearby providers (lat/lng/radius) |
| 6 | `GET /api/service-providers/GetRatings/{id}` | Get ratings for a provider |
| 7 | `GET /api/service-providers/GetAttachments/{id}` | Get images/files for a provider |

**Authenticated User:**
| # | Endpoint | What It Does |
|---|----------|-------------|
| 8 | `POST /api/service-providers/CreateServiceProvider` | Register a new provider (user becomes owner) |
| 9 | `PUT /api/service-providers/UpdateServiceProvider/{id}` | Update provider details |
| 10 | `DELETE /api/service-providers/DeleteServiceProvider/{id}` | Soft delete |
| 11 | `POST /api/service-providers/RateServiceProvider/{id}` | Rate + comment (awards loyalty points) |
| 12 | `POST /api/service-providers/AddToFavorites/{id}` | Favorite a provider (awards loyalty points) |
| 13 | `DELETE /api/service-providers/RemoveFromFavorites/{id}` | Remove from favorites |
| 14 | `POST /api/service-providers/UploadServiceProviderIcon/{id}` | Upload/replace provider icon |
| 15 | `POST /api/service-providers/AddAttachments/{id}` | Upload multiple images/files |
| 16 | `DELETE /api/service-providers/DeleteAttachments/{id}` | Delete all attachments |

**Admin:**
| # | Endpoint | What It Does |
|---|----------|-------------|
| 17 | `PUT /api/service-providers/VerifyServiceProvider/{id}` | Admin verifies a provider |

**Admin (Categories):**
| # | Endpoint | What It Does |
|---|----------|-------------|
| 18 | `POST /api/service-categories/CreateServiceCategory` | Create category |
| 19 | `PUT /api/service-categories/UpdateServiceCategory/{id}` | Update category |

### Workflow
```
Owner registers → CreateServiceProvider → Provider listed (unverified)
                                            ↓
Admin verifies → VerifyServiceProvider → Provider is verified ✓
                                            ↓
Owner uploads icon → UploadServiceProviderIcon
Owner uploads images → AddAttachments
                                            ↓
Users browse → GetAllServiceProviders / GetNearby / GetByCategory
Users view details → GetServiceProviderById (visitor count ++)
Users rate → RateServiceProvider → 10 loyalty points ✓
Users favorite → AddToFavorites → 5 loyalty points ✓
```

---

# Phase 0.5: Offers & Transactions System

## What Was Built
A marketplace offer system where providers create special deals, users redeem them with offer codes, and the platform earns commission on each transaction. Includes full settlement management.

### Entities Created
| Entity | Purpose |
|--------|---------|
| `ProviderOffer` | An offer created by a provider (title, discount, commission rate, validity period) |
| `OfferTransaction` | A single use of an offer (code generated, confirmed, or expired) |
| `ProviderSettlement` | Monthly commission settlement records per provider |
| `PointsConversionRate` | Currency-to-points conversion rates (e.g., 1 KWD = 10 points) |

### Enums Created
| Enum | Values |
|------|--------|
| `OfferApprovalStatus` | Pending=1, Approved=2, Rejected=3 |
| `OfferTransactionStatus` | Initiated=1, Completed=2, Cancelled=3, Expired=4 |
| `SettlementStatus` | Pending=1, Invoiced=2, Paid=3, Disputed=4 |

### Endpoints Built (22 total)

**User Endpoints:**
| # | Endpoint | What It Does |
|---|----------|-------------|
| 1 | `GET /api/offers/GetActiveOffers` | Browse approved active offers (filter by providerType, providerId, categoryId) |
| 2 | `GET /api/offers/GetOfferById/{id}` | View offer details |
| 3 | `POST /api/offers/UseOffer/{offerId}` | Generate an offer code (starts transaction) |
| 4 | `GET /api/offers/GetMyTransactions` | View my transaction history |
| 5 | `POST /api/offers/CancelTransaction/{id}` | Cancel before provider confirms |

**Provider Endpoints (Station Partner App):**
| # | Endpoint | What It Does |
|---|----------|-------------|
| 6 | `POST /api/offers/provider/ProposeOffer` | Create a new offer (goes to Pending) |
| 7 | `GET /api/offers/provider/GetOffersForProvider` | View my offers (all statuses) |
| 8 | `GET /api/offers/provider/LookupTransaction/{code}` | Look up a transaction by user's code |
| 9 | `POST /api/offers/provider/ConfirmTransaction` | Confirm transaction with amount → calculates commission + awards loyalty points |
| 10 | `GET /api/offers/provider/GetProviderTransactions` | View confirmed transactions |
| 11 | `GET /api/offers/provider/GetProviderSettlement` | View monthly settlement records |

**Admin Endpoints:**
| # | Endpoint | What It Does |
|---|----------|-------------|
| 12 | `PUT /api/offers/admin/ApproveOffer/{id}` | Approve a pending offer |
| 13 | `PUT /api/offers/admin/RejectOffer/{id}` | Reject with reason note |
| 14 | `PUT /api/offers/admin/UpdateOffer/{id}` | Modify any offer |
| 15 | `PUT /api/offers/admin/DeactivateOffer/{id}` | Deactivate an active offer |
| 16 | `GET /api/offers/admin/GetPendingOffers` | View all pending approval offers |
| 17 | `GET /api/offers/admin/GetSettlements` | View all settlements (filter by status/month/year) |
| 18 | `GET /api/offers/admin/GetSettlementSummary` | Dashboard summary of all settlements |
| 19 | `PUT /api/offers/admin/UpdateSettlementStatus/{id}` | Mark as Invoiced/Paid/Disputed |
| 20 | `POST /api/offers/admin/GenerateSettlement` | Generate monthly settlement for all providers |

**Conversion Rates (Support):**
| # | Endpoint | What It Does |
|---|----------|-------------|
| 21 | `GET /api/conversion-rates/GetAllConversionRates` | List all rates |
| 22 | `POST /api/conversion-rates/CreateConversionRate` | Admin creates rate (e.g., 1 KWD = 10 points) |
| 23 | `PUT /api/conversion-rates/UpdateConversionRate/{id}` | Admin updates rate |

### Full Offer Lifecycle Workflow

```
┌─────────────────────────────────────────────────────────────────┐
│                     OFFER CREATION FLOW                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Provider (Partner App)              Admin                      │
│  ─────────────────────              ─────                       │
│  ProposeOffer ──────────→ Offer (Pending)                       │
│                                    │                            │
│                           ApproveOffer ──→ Offer (Approved) ✓   │
│                           or                                    │
│                           RejectOffer ──→ Offer (Rejected) ✗   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                    TRANSACTION FLOW                              │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  User (App)                    Provider (Partner App)           │
│  ──────────                    ──────────────────────           │
│  Browse offers                                                  │
│  GetActiveOffers                                                │
│       │                                                         │
│  UseOffer/{offerId}                                             │
│       │                                                         │
│       ▼                                                         │
│  CODE GENERATED ─────→ User shows code on phone screen          │
│  (e.g., "XK9M2P")     to the provider                          │
│  Expires in X minutes                                           │
│                              │                                  │
│                        LookupTransaction/{code}                 │
│                              │                                  │
│                        Sees user name, offer details             │
│                              │                                  │
│                        ConfirmTransaction                       │
│                        (code + amount + currency)               │
│                              │                                  │
│                              ▼                                  │
│                    CALCULATIONS HAPPEN:                          │
│                    ┌──────────────────────────┐                 │
│                    │ commission = amount × %   │                 │
│                    │ eligible = amount × rwd%  │                 │
│                    │ points = eligible × rate   │                 │
│                    └──────────────────────────┘                 │
│                              │                                  │
│  User gets loyalty points ←──┘                                  │
│  (with tier multiplier applied)                                 │
│                                                                 │
│  Transaction status: Completed ✓                                │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                    SETTLEMENT FLOW (Monthly)                     │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Admin                                                          │
│  ─────                                                          │
│  GenerateSettlement(year, month)                                │
│       │                                                         │
│       ▼                                                         │
│  System aggregates all completed transactions for that month    │
│  Groups by provider → creates ProviderSettlement records        │
│  Each record contains:                                          │
│    - Total transactions count                                   │
│    - Total transaction amount                                   │
│    - Total commission amount (platform revenue)                 │
│    - Total points awarded                                       │
│       │                                                         │
│  Pending → Invoiced → Paid                                      │
│  (admin updates status as payment progresses)                   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Commission Calculation Example
```
Offer Configuration:
  commissionPercentage: 10%
  pointsRewardPercentage: 5%
  pointsConversionRate: 10 (from ConversionRate table)

Transaction: User pays 100 KWD at the provider

Calculations:
  commissionAmount    = 100 × 10% = 10 KWD (platform keeps this)
  pointsEligibleAmount = 100 × 5% = 5 KWD (basis for points)
  pointsAwarded        = floor(5 × 10) = 50 base points

  If user is Gold tier (2.0x multiplier):
  actualPointsAwarded  = floor(50 × 2.0) = 100 points ✓
```

---

# Phase 1: Loyalty System

## What Was Built
A complete seasonal tier-based loyalty system with points wallet, rewards catalog, and redemption management.

### Entities Created
| Entity | Purpose |
|--------|---------|
| `LoyaltyPointAction` | Defines actions that earn points (e.g., RATE_STATION = 10pts) |
| `LoyaltyTier` | Tier definitions (Bronze/Silver/Gold/Platinum) with multipliers |
| `LoyaltySeason` | Time-bound seasons (e.g., "Summer 2025") |
| `UserLoyaltyAccount` | User's points wallet (balance, totals) |
| `UserSeasonProgress` | User's progress in a specific season (points earned, tier level) |
| `LoyaltyPointTransaction` | Full audit trail of every point earn/spend |
| `LoyaltyReward` | Rewards catalog (items users can buy with points) |
| `UserRewardRedemption` | Record of a user redeeming a reward |

### Enums Created
| Enum | Values |
|------|--------|
| `TransactionType` | Earn=1, Redeem=2, Expired=3, AdminAdjust=4, SeasonBonus=5 |
| `RewardType` | Discount=1, FreeCharge=2, FreeService=3, PriorityAccess=4, Badge=5 |
| `RedemptionStatus` | Pending=1, Fulfilled=2, Cancelled=3 |

### Tier System (Seeded in DB)
| Tier | Min Season Points | Multiplier | End-of-Season Bonus |
|------|-------------------|-----------|---------------------|
| Bronze | 0 | 1.0x | 0 points |
| Silver | 500 | 1.5x | 100 points |
| Gold | 2,000 | 2.0x | 500 points |
| Platinum | 5,000 | 3.0x | 1,500 points |

### Point Actions (Seeded in DB)
| Action Code | Base Points | Daily Limit | Lifetime Limit |
|------------|------------|-------------|---------------|
| `ADD_FAVORITE` | 5 | - | - |
| `RATE_STATION` | 10 | - | - |
| `FIRST_CHARGE` | 50 | - | 1 |
| `DAILY_LOGIN` | 2 | 1 | - |
| `REFER_FRIEND` | 100 | - | 10 |
| `COMPLETE_PROFILE` | 25 | - | 1 |
| `ADD_VEHICLE` | 15 | - | 5 |
| `SHARE_STATION` | 5 | 3 | - |
| `ADD_FAVORITE_SERVICE` | 5 | - | - |
| `RATE_SERVICE` | 10 | - | - |
| `USE_SERVICE` | 20 | 5 | - |
| `WRITE_REVIEW` | 15 | 3 | - |

### Endpoints Built (17 total)

**User Endpoints:**
| # | Endpoint | What It Does |
|---|----------|-------------|
| 1 | `GET /api/loyalty/GetMyLoyaltyAccount` | View wallet balance, tier, season progress |
| 2 | `GET /api/loyalty/GetMyPointsHistory` | Paginated transaction history (filter by season) |
| 3 | `GET /api/loyalty/GetAvailableRewards` | Browse rewards catalog (filter by provider/category) |
| 4 | `GET /api/loyalty/GetRewardsForProvider/{type}/{id}` | Rewards at a specific provider |
| 5 | `POST /api/loyalty/RedeemReward/{rewardId}` | Spend points → get RWD-XXXXXX code |
| 6 | `GET /api/loyalty/GetMyRedemptions` | View my redeemed rewards |
| 7 | `GET /api/loyalty/GetCurrentSeason` | Current season + my progress + next tier info |
| 8 | `GET /api/loyalty/GetSeasonHistory` | Past seasons with final tier and bonus received |
| 9 | `GET /api/loyalty/GetLeaderboard` | Top users by season points |

**Admin Endpoints:**
| # | Endpoint | What It Does |
|---|----------|-------------|
| 10 | `POST /api/loyalty/admin/CreateSeason` | Create a new season (optional: activate immediately) |
| 11 | `POST /api/loyalty/admin/EndSeason/{id}` | End season → awards tier bonuses to all users |
| 12 | `POST /api/loyalty/admin/AdjustPoints` | Manually add/deduct points for a user |
| 13 | `POST /api/loyalty/admin/CreateReward` | Add reward to catalog (optional provider link) |
| 14 | `PUT /api/loyalty/admin/UpdateReward/{id}` | Modify reward details |
| 15 | `PATCH /api/loyalty/admin/FulfillRedemption/{id}` | Mark redemption as fulfilled |
| 16 | `PATCH /api/loyalty/admin/CancelRedemption/{id}` | Cancel redemption → refund points |
| 17 | `GET /api/loyalty/admin/GetProviderRedemptions` | View redemptions at a provider |

### Automatic Point Integrations (5 existing endpoints modified)
These existing handlers now automatically award loyalty points:

| Existing Action | Action Code | Points |
|----------------|-------------|--------|
| Add charging point to favorites | `ADD_FAVORITE` | 5 |
| Rate a charging point | `RATE_STATION` | 10 |
| Rate a service provider | `RATE_SERVICE` | 10 |
| Favorite a service provider | `ADD_FAVORITE_SERVICE` | 5 |
| Confirm offer transaction | Dynamic (from offer calculation) | varies |

### Full Loyalty Lifecycle Workflow

```
┌─────────────────────────────────────────────────────────────────┐
│                    EARNING POINTS                                │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  User does an action                                            │
│  (rate, favorite, complete offer, etc.)                         │
│       │                                                         │
│       ▼                                                         │
│  ILoyaltyPointService.AwardPointsAsync()                        │
│       │                                                         │
│       ├─→ Check: Is action active?                              │
│       ├─→ Check: Daily limit reached?                           │
│       ├─→ Check: Lifetime limit reached?                        │
│       │                                                         │
│       ▼ (if all checks pass)                                    │
│  Find or create UserLoyaltyAccount (wallet)                     │
│  Find or create UserSeasonProgress (if season active)           │
│       │                                                         │
│       ▼                                                         │
│  Get current tier multiplier                                    │
│  actualPoints = floor(basePoints × multiplier)                  │
│       │                                                         │
│       ├─→ Wallet.CurrentBalance += actualPoints                 │
│       ├─→ Wallet.TotalPointsEarned += actualPoints              │
│       ├─→ SeasonProgress.SeasonPointsEarned += actualPoints     │
│       ├─→ Create LoyaltyPointTransaction (audit)                │
│       ├─→ Recalculate tier (maybe promote!)                     │
│       │                                                         │
│       ▼                                                         │
│  User: "You earned 20 points! (Gold 2.0x applied)"             │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                    REDEEMING REWARDS                             │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  User (App)                          Admin                      │
│  ──────────                          ─────                      │
│  Browse rewards                                                 │
│  GetAvailableRewards                                            │
│       │                                                         │
│  Select reward (e.g., "Free Charge" = 500 pts)                  │
│       │                                                         │
│  RedeemReward/{rewardId}                                        │
│       │                                                         │
│       ▼                                                         │
│  System checks:                                                 │
│  ├─ Reward active & within dates?                               │
│  ├─ Max redemptions not reached?                                │
│  └─ User has enough balance?                                    │
│       │                                                         │
│       ▼ (all pass)                                              │
│  Wallet.CurrentBalance -= 500                                   │
│  Generate code: "RWD-A3F8K2"                                    │
│  Create UserRewardRedemption (Pending)                          │
│       │                                                         │
│  User shows RWD code                                            │
│  to provider/admin                                              │
│                              │                                  │
│                        FulfillRedemption/{id}                   │
│                        Status: Pending → Fulfilled ✓            │
│                                                                 │
│  OR if cancelled:                                               │
│                        CancelRedemption/{id}                    │
│                        Status: Pending → Cancelled              │
│                        Points refunded back to wallet ↩         │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                    SEASON LIFECYCLE                              │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Admin: CreateSeason("Summer 2025", Jun 1 - Aug 31)             │
│       │                                                         │
│       ▼                                                         │
│  Season starts (IsActive = true)                                │
│  All users start at Bronze tier (0 season points)               │
│       │                                                         │
│       ▼ (users earn points over time)                           │
│                                                                 │
│  User earns 500 pts → promoted to Silver (1.5x) ↑              │
│  User earns 2000 pts → promoted to Gold (2.0x) ↑               │
│  User earns 5000 pts → promoted to Platinum (3.0x) ↑           │
│                                                                 │
│  NOTE: Wallet balance does NOT reset.                           │
│        Only season progress (tier) resets each season.          │
│       │                                                         │
│       ▼ (season ends)                                           │
│                                                                 │
│  Admin: EndSeason/{id}                                          │
│       │                                                         │
│       ▼                                                         │
│  System processes ALL users:                                    │
│  ├─ Bronze users → 0 bonus                                     │
│  ├─ Silver users → 100 bonus points                             │
│  ├─ Gold users → 500 bonus points                               │
│  └─ Platinum users → 1500 bonus points                          │
│       │                                                         │
│  Season deactivated. Points added to wallet.                    │
│  Admin creates next season.                                     │
│                                                                 │
│  KEY: Wallet balance carries over forever.                      │
│       Tier resets to Bronze in new season.                      │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

# Phase 2: Partner Transactions (Permanent Partnerships)

## What Was Built
A permanent, always-on partnership system where the Cable admin registers providers as Cable partners. Users visit partner locations, generate a transaction code, the provider confirms with the actual amount, Cable takes commission, and the user earns loyalty points. Unlike Offers (which are temporary promotions), Partners are permanent and require no approval flow.

### Key Difference: Partners vs Offers
| | **Offers** | **Partners** |
|---|---|---|
| Duration | Temporary (start date → end date) | **Permanent** (always on) |
| Max uses | Limited (e.g., 100 uses) | **Unlimited** |
| Who creates | Provider proposes, admin approves | **Admin creates directly** |
| Approval | Needs admin approval first | **No approval needed** |
| Code prefix | `CBL-XXXXXX` | `PTR-XXXXXX` |
| Purpose | Promotional campaigns | **Ongoing revenue partnerships** |

### Entities Created
| Entity | Purpose |
|--------|---------|
| `PartnerAgreement` | Defines the partnership terms (commission %, points %, code expiry, linked to a provider) |
| `PartnerTransaction` | A single use: provider generates code with amount → user scans QR → commission + points calculated |

### Enums Created
| Enum | Values |
|------|--------|
| `PartnerTransactionStatus` | Initiated=1, Completed=2, Expired=3, Cancelled=4 |

### PartnerAgreement Fields
| Field | Purpose |
|-------|---------|
| `ProviderType` | "ChargingPoint" or "ServiceProvider" |
| `ProviderId` | Links to the actual provider entity |
| `CommissionPercentage` | % Cable takes from each transaction (e.g., 10%) |
| `PointsRewardPercentage` | % of amount used to calculate points (e.g., 5%) |
| `PointsConversionRateId` | Optional FK to conversion rate (falls back to default) |
| `CodeExpiryMinutes` | How long a generated code is valid (default: 30 min) |
| `IsActive` | Admin can activate/deactivate anytime |
| `Note` | Admin notes about the agreement |

### PartnerTransaction Fields
| Field | Purpose |
|-------|---------|
| `PartnerAgreementId` | Links to which partner agreement |
| `UserId` | The user who scanned the QR code (set on scan) |
| `TransactionCode` | Unique code (e.g., `PTR-7X9K2M`) |
| `Status` | Initiated → Completed/Expired/Cancelled |
| `TransactionAmount` | Actual amount the user paid (set by provider on creation) |
| `CurrencyCode` | Currency (e.g., "KWD") |
| `CommissionPercentage` | Snapshot of % at time of initiation |
| `CommissionAmount` | Calculated: amount × commission% |
| `PointsRewardPercentage` | Snapshot of % at time of initiation |
| `PointsConversionRate` | Snapshot of rate at time of initiation |
| `PointsEligibleAmount` | Calculated: amount × rewardPercentage% |
| `PointsAwarded` | Calculated: eligibleAmount × conversionRate |
| `ConfirmedByUserId` | The provider staff who created the transaction |
| `CodeExpiresAt` | When the code becomes invalid |
| `CompletedAt` | When the user scanned the QR code |

### Endpoints Built (12 total)

**User Endpoints (4):**
| # | Endpoint | What It Does |
|---|----------|-------------|
| 1 | `GET /api/partners/GetActivePartners` | Browse all active Cable partners (optional providerType filter) |
| 2 | `GET /api/partners/GetPartnerById/{id}` | View partner details + conversion rate info |
| 3 | `POST /api/partners/ScanPartnerCode?code=` | Scan QR code → system completes transaction + awards points |
| 4 | `GET /api/partners/GetMyTransactions` | View my partner transaction history (optional status filter) |

**Provider Endpoints (4):**
| # | Endpoint | What It Does |
|---|----------|-------------|
| 5 | `POST /api/partners/provider/CreateTransaction` | Create transaction with amount → get PTR-XXXXXX code → show as QR |
| 6 | `POST /api/partners/provider/CancelTransaction/{id}` | Cancel before user scans (own transactions only) |
| 7 | `GET /api/partners/provider/GetProviderTransactions` | View transactions at their location (optional month/year filter) |
| 8 | `GET /api/partners/provider/GetMyAgreement` | View their partnership terms |

**Admin Endpoints (4):**
| # | Endpoint | What It Does |
|---|----------|-------------|
| 9 | `POST /api/partners/admin/CreatePartnerAgreement` | Register a provider as a Cable partner (sets rates) |
| 10 | `PUT /api/partners/admin/UpdatePartnerAgreement/{id}` | Update commission %, points %, expiry, active status |
| 11 | `PUT /api/partners/admin/DeactivatePartnerAgreement/{id}` | Deactivate a partnership |
| 12 | `GET /api/partners/admin/GetAllPartnerAgreements` | View all agreements (optional isActive filter) |

### Full Partner Transaction Lifecycle Workflow

```
┌─────────────────────────────────────────────────────────────────┐
│                  PARTNERSHIP SETUP (One-time, Admin)             │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Cable Admin                                                    │
│  ───────────                                                    │
│  CreatePartnerAgreement                                         │
│       │                                                         │
│       ▼                                                         │
│  Partner Agreement Created:                                     │
│  ┌────────────────────────────────────────┐                     │
│  │ Provider: "Gas Station ABC"            │                     │
│  │ Type: ServiceProvider                  │                     │
│  │ Commission: 10%                        │                     │
│  │ Points Reward: 5%                      │                     │
│  │ Conversion Rate: 10 pts/KWD           │                     │
│  │ Code Expiry: 30 minutes               │                     │
│  │ Status: Active ✓                      │                     │
│  └────────────────────────────────────────┘                     │
│                                                                 │
│  No approval needed. Partnership is live immediately.           │
│  Admin can update rates or deactivate anytime.                  │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                  TRANSACTION FLOW (Repeatable)                   │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Provider (Partner App)              User (Cable App)           │
│  ──────────────────────              ──────────────             │
│                                                                 │
│  User pays 50 KWD at the location (normal payment)              │
│                                                                 │
│  Staff taps "New Transaction"                                   │
│  CreateTransaction                                              │
│  (agreementId + 50 KWD + "KWD")                                │
│       │                                                         │
│       ▼                                                         │
│  SYSTEM CALCULATES:                                             │
│  ┌──────────────────────────────┐                               │
│  │ commission = 50 × 10% = 5 KWD│                               │
│  │ eligible   = 50 × 5%  = 2.5  │                               │
│  │ points     = 2.5 × 10 = 25   │                               │
│  └──────────────────────────────┘                               │
│       │                                                         │
│       ▼                                                         │
│  QR CODE GENERATED                                              │
│  (PTR-7X9K2M)                                                   │
│  Expires in 30 min                                              │
│  Staff shows QR on screen ─────→ User opens "Scan QR"          │
│                                         │                       │
│                                  ScanPartnerCode                │
│                                  (code=PTR-7X9K2M)             │
│                                         │                       │
│                                         ▼                       │
│                                  Transaction Completed ✓        │
│                                  User gets 25 loyalty points    │
│                                  (tier multiplier applied)      │
│                                                                 │
│  ── OR ──                                                       │
│                                                                 │
│  Provider CancelTransaction (before user scans)                 │
│  Transaction status: Cancelled ✗                                │
│                                                                 │
│  ── OR ──                                                       │
│                                                                 │
│  Code expires (30 min passed, nobody scanned)                   │
│  Transaction status: Expired ⏰                                 │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│               SETTLEMENT (Combined with Offers)                  │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Admin runs: GenerateSettlement(year, month)                    │
│       │                                                         │
│       ▼                                                         │
│  System processes:                                              │
│  1. All completed OFFER transactions for that month             │
│  2. All completed PARTNER transactions for that month           │
│       │                                                         │
│       ▼                                                         │
│  Groups by (ProviderType + ProviderId)                          │
│  If same provider has both offer + partner transactions:        │
│  → Combined into ONE settlement record (totals added)           │
│       │                                                         │
│       ▼                                                         │
│  ProviderSettlement created/updated:                            │
│  ┌────────────────────────────────────┐                         │
│  │ Provider: "Gas Station ABC"        │                         │
│  │ Period: January 2025               │                         │
│  │ Total Transactions: 45             │                         │
│  │ Total Amount: 2,250 KWD            │                         │
│  │ Total Commission: 225 KWD          │                         │
│  │ Total Points Awarded: 1,125        │                         │
│  │ Status: Pending → Invoiced → Paid  │                         │
│  └────────────────────────────────────┘                         │
│                                                                 │
│  For partner transactions, owner ID is resolved from            │
│  ChargingPoint.OwnerId or ServiceProvider.OwnerId              │
│  (instead of from an offer's ProposedByUserId)                  │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Commission Calculation Example
```
Partner Agreement Configuration:
  commissionPercentage: 10%
  pointsRewardPercentage: 5%
  pointsConversionRate: 10 (from ConversionRate table)

Transaction: User pays 100 KWD at the partner location

Calculations:
  commissionAmount     = 100 × 10% = 10 KWD (Cable keeps this)
  pointsEligibleAmount = 100 × 5%  = 5 KWD (basis for points)
  pointsAwarded        = floor(5 × 10) = 50 base points

  If user is Gold tier (2.0x multiplier):
  actualPointsAwarded  = floor(50 × 2.0) = 100 points ✓
```

### Important: Snapshot Values
When the provider creates a transaction (`CreateTransaction`), the system **snapshots** the current rates from the agreement into the transaction:
- `CommissionPercentage` — locked at creation time
- `PointsRewardPercentage` — locked at creation time
- `PointsConversionRate` — locked at creation time
- `CommissionAmount`, `PointsAwarded` — **pre-calculated** at creation time

This means if the admin changes rates after a provider creates a transaction, the **old rates** still apply. All calculations are done upfront — the user scan simply completes the transaction and triggers point awarding.

---

# How Everything Connects

```
┌──────────────────────────────────────────────────────────────────┐
│                        FULL SYSTEM MAP                           │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  SERVICE PROVIDERS ←──── Categories                              │
│  CHARGING POINTS                                                 │
│       │                                                          │
│       ├─── Ratings ──────────────→ Awards loyalty points         │
│       ├─── Favorites ────────────→ Awards loyalty points         │
│       ├─── Attachments/Icon                                      │
│       │                                                          │
│       ├─── OFFERS (temporary promotions)                         │
│       │       │ linked by ProviderType + ProviderId              │
│       │       │                                                  │
│       │       ├─── Provider proposes → Admin approves            │
│       │       ├─── User generates CBL-XXXXXX code                │
│       │       ├─── Provider confirms with amount                 │
│       │       │       ├─→ Commission calculated                  │
│       │       │       └─→ Loyalty points awarded ──────┐         │
│       │       └─── Has start/end dates, max uses       │         │
│       │                                                │         │
│       └─── PARTNERS (permanent partnerships)           │         │
│               │ linked by ProviderType + ProviderId     │         │
│               │                                        │         │
│               ├─── Admin creates directly (no approval) │         │
│               ├─── Provider creates transaction + QR   │         │
│               ├─── User scans QR → instant completion  │         │
│               │       ├─→ Commission calculated        │         │
│               │       └─→ Loyalty points awarded ──────┤         │
│               └─── Always on, unlimited uses           │         │
│                                                        │         │
│       ┌────────────────────────────────────────────────┘         │
│       │                                                          │
│       ▼                                                          │
│  LOYALTY SYSTEM                                                  │
│       │                                                          │
│       ├─── Wallet (balance persists forever)                     │
│       ├─── Season Progress (tier resets each season)             │
│       ├─── Tier Multiplier (higher tier = more points)           │
│       │                                                          │
│       └─── REWARDS (optional provider link)                      │
│               │                                                  │
│               ├─── User redeems with points                      │
│               ├─── Gets RWD-XXXXXX code                          │
│               └─── Admin fulfills or cancels                     │
│                                                                  │
│  SETTLEMENTS (monthly, combines offers + partners)               │
│       │                                                          │
│       ├─── Groups by provider                                    │
│       ├─── Aggregates: total amount, commission, points          │
│       └─── Status: Pending → Invoiced → Paid                    │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘

Total Endpoints: ~72
  Service Providers & Categories: 19
  Offers & Transactions: 23
  Loyalty System: 17
  Conversion Rates: 3
  Partner Transactions: 12
```

---

# Important Business Rules

1. **ProviderType**: Either `"ChargingPoint"` or `"ServiceProvider"` — used across offers and rewards to link back to the actual provider.

2. **Offer Approval**: Providers can only **propose** offers. Admin must **approve** before they go live. This prevents spam/abuse.

3. **Offer Code Expiry**: Each generated code has a configurable expiry (minutes). If not confirmed in time, it auto-expires.

4. **Commission**: Platform takes a percentage of each confirmed transaction. This is the revenue model.

5. **Points Never Expire (Wallet)**: The `CurrentBalance` in `UserLoyaltyAccount` persists forever. Only redemptions reduce it.

6. **Tiers Reset Per Season**: When a new season starts, everyone starts at Bronze again. This keeps competition fresh.

7. **Daily/Lifetime Limits**: Point actions have optional limits to prevent abuse (e.g., DAILY_LOGIN can only be earned once per day).

8. **Reward Ownership**: Currently, only admins create rewards. Rewards can optionally be linked to a specific provider via `ProviderType` + `ProviderId`. If left null, the reward is available globally.

9. **Redemption Flow**: User redeems → gets code → presents to provider → admin/provider marks as fulfilled. If cancelled, points are refunded.

10. **Settlement Generation**: Monthly process. Admin runs `GenerateSettlement(year, month)` to aggregate all completed transactions (offers + partners) into per-provider settlement records. If a provider has both offer and partner transactions in the same month, they combine into one settlement.

11. **Partner Agreements are Permanent**: Unlike offers, partner agreements have no start/end date and no max usage. They are always-on until the admin deactivates them.

12. **Admin Controls Partners**: Only Cable admin can create, update, and deactivate partner agreements. The provider has no approval say — admin enters the terms directly.

13. **Rate Snapshots**: When a provider creates a partner transaction (`CreateTransaction`), the current commission %, points reward %, and conversion rate are **snapshotted** and all amounts are **pre-calculated**. Even if admin changes rates later, the generated code uses the old rates. The user scan simply completes and triggers point awarding.

14. **One Active Agreement Per Provider**: A provider can only have one active partner agreement at a time. If the admin wants to change a provider's terms, they update the existing agreement (or deactivate and create a new one).

15. **Code Format**:
    - Offer codes: `CBL-XXXXXX` (e.g., `CBL-XK9M2P`)
    - Partner codes: `PTR-XXXXXX` (e.g., `PTR-7X9K2M`)
    - Reward codes: `RWD-XXXXXX` (e.g., `RWD-A3F8K2`)

---

# What's NOT Implemented Yet (Future Considerations)

| Feature | Description |
|---------|-------------|
| Provider self-service rewards | Providers create their own rewards (with admin approval) |
| Provider fulfills redemptions | Provider scans RWD code and marks as fulfilled |
| Push notifications for loyalty | Notify users on tier promotion, season end, reward expiry |
| Points expiry | Auto-expire unused points after X months |
| Offer images upload | Dedicated upload endpoint for offer images |
| Referral tracking | Track which user referred whom for REFER_FRIEND points |
| Analytics dashboard | Provider analytics on offer performance |
| Partner transaction expiry job | Background job to auto-expire partner codes past CodeExpiresAt |
| Partner analytics for providers | Monthly stats dashboard for partner providers (transaction count, revenue, etc.) |
| Remaining point actions | Wire up unwired point actions: FIRST_CHARGE, DAILY_LOGIN, REFER_FRIEND, COMPLETE_PROFILE, ADD_VEHICLE, SHARE_STATION, USE_SERVICE |
