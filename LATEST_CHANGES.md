# Latest Changes (March 2026)

Two new features added to the settlement and partner system:

1. **Offer transactions now affect WalletBalance** (same as partner transactions but in reverse)
2. **Minimum transaction amount** added to partner agreements

---

## 1. Offer Transactions Affect WalletBalance

### Before
Offer transactions only tracked amounts in the settlement (`OfferPaymentAmount`) but **never touched** the provider's `WalletBalance`. The provider had to wait for manual settlement to receive their money.

### After
When a user scans an offer code and completes the transaction, the provider's `WalletBalance` **increases immediately** by the offer's `MonetaryValue`.

### How It Works

| Event | WalletBalance Effect | Direction |
|-------|---------------------|-----------|
| **Partner transaction** initiated | `-= commission` | Provider owes Cable (decreases) |
| **Partner transaction** cancelled/expired | `+= commission` (refund) | Refund (increases) |
| **Offer transaction** completed (user scans) | `+= monetaryValue` | Cable pays provider (increases) |

### Per-Transaction Flow

```
User scans offer code (CBL-XXXXXX)
  |
  v
1. User's loyalty points are deducted
2. Provider's WalletBalance += MonetaryValue (immediate credit)
3. transaction.WalletCreditedAmount = MonetaryValue
4. Settlement upserted:
   - OfferTransactionCount += 1
   - OfferPaymentAmount += MonetaryValue
   - WalletApplied += WalletCreditedAmount (accumulated atomically)
5. ProviderWalletTransaction audit record created (OfferPaymentCredit)
```

### What Does NOT Change

- **Initiate offer** — no wallet change at initiation (credit only at completion)
- **Cancel offer** — only cancels `Initiated` status (no wallet credit to refund)
- **Expire offer (background job)** — only expires `Initiated`, no wallet involved
- **Credit limit check** — not needed for offers (wallet goes UP, not down)

### New WalletTransactionType Enum Values

```csharp
public enum WalletTransactionType
{
    Deposit = 1,              // Manual deposit by admin
    SettlementDeduction = 2,  // Legacy (system-only)
    Refund = 3,               // Manual refund by admin
    Adjustment = 4,           // Manual adjustment by admin
    CommissionDeduction = 5,  // Partner transaction commission (system-only)
    CommissionRefund = 6,     // Partner cancel/expire refund (system-only)
    OfferPaymentCredit = 7,   // Offer completed - Cable pays provider (system-only)
    OfferPaymentRefund = 8    // Reserved for future use (system-only)
}
```

### New Field: OfferTransaction.WalletCreditedAmount

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `WalletCreditedAmount` | `decimal(18,3)` | `0` | How much of MonetaryValue was credited to provider wallet at completion |

Mirrors `PartnerTransaction.WalletCoveredAmount` for symmetry and audit.

### Settlement WalletApplied Now Includes Both

```
WalletApplied = sum(PartnerTransaction.WalletCoveredAmount) + sum(OfferTransaction.WalletCreditedAmount)
```

Both accumulated atomically in `SettlementService` via `ExecuteUpdateAsync`.

### Example Scenario

```
Week starts: WalletBalance = -20 (debt from last week)

Mon: Partner transaction, commission = 15
     → walletCovered = 0 (balance negative, nothing to cover)
     → WalletBalance = -35

Tue: User redeems offer, monetaryValue = 50
     → WalletBalance = -35 + 50 = 15 (now positive!)

Wed: Partner transaction, commission = 10
     → walletCovered = 10 (positive balance covers it)
     → WalletBalance = 15 - 10 = 5

Settlement:
  PartnerCommissionAmount = 25
  OfferPaymentAmount = 50
  WalletApplied = 0 + 50 + 10 = 60
  NetBalance = 50 - 25 = 25 (Cable owes provider)
  OutstandingAmount = 25 - 60 = 0 (fully covered via wallet)
```

### AddWalletDeposit Validation Updated

The following types are now blocked from manual use (system-only):
- `SettlementDeduction (2)`
- `CommissionDeduction (5)`
- `CommissionRefund (6)`
- `OfferPaymentCredit (7)` — **NEW**
- `OfferPaymentRefund (8)` — **NEW**

---

## 2. Minimum Transaction Amount for Partner Agreements

### Overview

Admin can now set a **minimum transaction amount** on each partner agreement. When a provider creates a transaction below this minimum, it is rejected.

### New Field: PartnerAgreement.MinimumTransactionAmount

| Field | Type | Nullable | Description |
|-------|------|----------|-------------|
| `MinimumTransactionAmount` | `decimal(18,3)` | Yes (null) | Minimum amount for partner transactions. `null` = no minimum. |

### Validation

- Must be greater than 0 if provided
- `null` = no minimum (any amount accepted)
- Enforced at `InitiatePartnerTransaction` — before any commission calculation or wallet deduction

### Error Response

If a transaction amount is below the minimum:
```json
{
    "field": "TransactionAmount",
    "message": "Transaction amount must be at least 5.000 JOD"
}
```

### Included In All Endpoints

| Endpoint | DTO Field Added |
|----------|----------------|
| `POST /api/partners/admin/CreatePartnerAgreement` | `minimumTransactionAmount` (request + command) |
| `PUT /api/partners/admin/UpdatePartnerAgreement/{id}` | `minimumTransactionAmount` (request + command) |
| `GET /api/partners/admin/GetAllPartnerAgreements` | `minimumTransactionAmount` (response) |
| `GET /api/partners/GetActivePartners` | `minimumTransactionAmount` (response) |
| `GET /api/partners/GetPartnerById/{id}` | `minimumTransactionAmount` (response) |
| `GET /api/partners/provider/GetMyAgreement` | `minimumTransactionAmount` (response) |

### Example: Create Agreement with Minimum

```json
POST /api/partners/admin/CreatePartnerAgreement
{
    "providerType": "ChargingPoint",
    "providerId": 43,
    "commissionPercentage": 10,
    "pointsRewardPercentage": 5,
    "pointsConversionRateId": null,
    "codeExpirySeconds": 120,
    "minimumTransactionAmount": 5.000,
    "note": "Standard agreement"
}
```

---

## Files Modified

### Change 1: Offer Wallet Integration

| File | Change |
|------|--------|
| `Cable.Core/Enums/WalletTransactionType.cs` | Added `OfferPaymentCredit = 7`, `OfferPaymentRefund = 8` |
| `Domain/Enitites/OfferTransaction.cs` | Added `WalletCreditedAmount` property |
| `Infrastructrue/Persistence/Configurations/OfferTransactionConfiguration.cs` | Added `WalletCreditedAmount` column config |
| `Application/Offers/Commands/ConfirmOfferTransaction/ConfirmOfferTransactionCommand.cs` | Credits provider WalletBalance on scan, creates audit record |
| `Infrastructrue/Services/SettlementService.cs` | Passes `WalletCreditedAmount` to settlement upsert |
| `Application/Offers/Commands/AddWalletDeposit/AddWalletDepositCommand.cs` | Blocks `OfferPaymentCredit`/`OfferPaymentRefund` from manual use |

### Change 2: Minimum Transaction Amount

| File | Change |
|------|--------|
| `Domain/Enitites/PartnerAgreement.cs` | Added `MinimumTransactionAmount` property |
| `Infrastructrue/Persistence/Configurations/PartnerAgreementConfiguration.cs` | Added column config |
| `WebApi/Requests/Partners/CreatePartnerAgreementRequest.cs` | Added field |
| `WebApi/Requests/Partners/UpdatePartnerAgreementRequest.cs` | Added field |
| `Application/Partners/Commands/CreatePartnerAgreement/CreatePartnerAgreementCommand.cs` | Added field + validation |
| `Application/Partners/Commands/UpdatePartnerAgreement/UpdatePartnerAgreementCommand.cs` | Added field |
| `Application/Partners/Commands/InitiatePartnerTransaction/InitiatePartnerTransactionCommand.cs` | Enforces minimum check |
| `Application/Partners/Queries/GetAllPartnerAgreements/GetAllPartnerAgreementsRequest.cs` | Added to DTO |
| `Application/Partners/Queries/GetProviderPartnerAgreement/GetProviderPartnerAgreementRequest.cs` | Added to DTO |
| `Application/Partners/Queries/GetPartnerById/GetPartnerByIdRequest.cs` | Added to DTO |
| `Application/Partners/Queries/GetActivePartners/GetActivePartnersRequest.cs` | Added to DTO |
| `WebApi/Routes/PartnerRoutes.cs` | Updated Create + Update route mappings |

---

## Database Migrations

```sql
-- Offer wallet integration
ALTER TABLE OfferTransaction ADD WalletCreditedAmount decimal(18,3) NOT NULL DEFAULT 0;

-- Minimum transaction amount
ALTER TABLE PartnerAgreement ADD MinimumTransactionAmount decimal(18,3) NULL;
```
