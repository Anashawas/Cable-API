# Settlement System Redesign

## Overview

Complete redesign of Cable's settlement and provider wallet system. Two major changes:

1. **Fixed business logic** — Cable never collects money from users. Users pay providers directly. Cable only earns commission.
2. **Unified wallet** — Merged two separate balance systems (`LoyaltyCurrentBalance` + `WalletBalance`) into one `WalletBalance` with real-time per-transaction deduction.

---

## Business Logic

| Flow | Who Pays Whom |
|------|---------------|
| User charges EV at provider | User pays **provider directly** — Cable never touches this money |
| Cable's commission | **Provider owes Cable** a commission (`PartnerCommissionAmount`) |
| User redeems loyalty offer | **Cable owes provider** for the redeemed offer (`OfferPaymentAmount`) |

### NetBalance Formula

```
NetBalance = OfferPaymentAmount - PartnerCommissionAmount
```

- **Positive** = Cable owes the provider (offers exceeded commissions)
- **Negative** = Provider owes Cable (commissions exceeded offers)

---

## Unified Wallet System

Previously two separate systems tracked provider debt:

| Old System | How It Worked |
|------------|---------------|
| `LoyaltyCurrentBalance` + `LoyaltyCreditLimit` | Deducted per-transaction in real-time, with credit limit check |
| `WalletBalance` (PreCredit) | Deducted when admin marks settlement as Paid |

Both tracked the same thing — **provider owes Cable commission**. Now merged into **one unified wallet**:

| Old Field | New Field | Notes |
|-----------|-----------|-------|
| `LoyaltyCreditLimit` | `WalletCreditLimit` | Max debt allowed before blocking transactions |
| `LoyaltyCurrentBalance` | **REMOVED** | Balance merged into `WalletBalance` |
| `PreCreditBalance` / `WalletBalance` | `WalletBalance` | Single unified balance |
| `RecordProviderPayment` endpoint | **REMOVED** | Use `AddWalletDeposit` instead |

### How the Unified Wallet Works

| Event | What Happens to WalletBalance |
|-------|-------------------------------|
| Partner transaction initiated | `-= commission` (real-time, with credit limit check) |
| Transaction cancelled/expired | `+= commission` (refund) |
| Admin deposits money | `+= amount` (via `AddWalletDeposit`) |
| Settlement marked Paid | **NO deduction** — just status change, wallet already deducted |

### Credit Limit Check (per transaction)

```csharp
if (cp.WalletCreditLimit.HasValue)
{
    var newBalance = cp.WalletBalance - commissionAmount;
    if (newBalance < -cp.WalletCreditLimit.Value)
        throw "Provider credit limit reached";
}
walletCoveredAmount = Min(Max(WalletBalance, 0), commissionAmount);
cp.WalletBalance -= commissionAmount;
```

### Wallet Balance States

- **Positive** = provider has credit
- **Negative** = provider owes Cable (debt)
- **Credit limit** controls how far negative it can go before blocking new transactions
- `null` credit limit = unlimited debt allowed

### New WalletTransactionType Values

```csharp
CommissionDeduction = 5,  // Auto-deducted per transaction (system-only)
CommissionRefund = 6      // Refunded on cancel/expire (system-only)
```

---

## WalletApplied — Per-Transaction Accumulation

`WalletApplied` tracks how much of a settlement's commission was covered by the wallet's **positive** balance. It is accumulated in real-time as transactions complete, NOT calculated when marking settlement as Paid.

### How It Works

1. `InitiatePartnerTransaction` calculates `walletCovered = Min(Max(WalletBalance, 0), commission)` **before** deducting
2. Sets `transaction.WalletCoveredAmount = walletCovered`
3. `SettlementService.UpsertSettlement` atomically accumulates `WalletApplied += walletCoveredAmount`
4. When settlement is marked Paid, `WalletApplied` is already correct — no recalculation needed

### Example

```
Week starts: WalletBalance = 50, WalletCreditLimit = 30

Mon: commission 20 → walletCovered=20, WalletBalance=30
Tue: commission 25 → walletCovered=25, WalletBalance=5
Wed: commission 15 → walletCovered=5,  WalletBalance=-10 (partially covered)
Thu: commission 10 → walletCovered=0,  WalletBalance=-20 (all debt)
Fri: commission 10 → walletCovered=0,  WalletBalance=-30 (limit reached, next blocked)

Settlement: WalletApplied = 20+25+5+0+0 = 50 (what positive wallet covered)
            OutstandingAmount = 80 - 50 = 30 (debt portion)
```

---

## Settlement Period: Weekly (Sunday–Saturday)

Changed from monthly to weekly settlements using Sunday–Saturday boundaries.

```csharp
private static (int Year, int Week) GetSundaySaturdayWeek(DateTime date)
{
    var calendar = CultureInfo.InvariantCulture.Calendar;
    var week = calendar.GetWeekOfYear(date, CalendarWeekRule.FirstDay, DayOfWeek.Sunday);
    return (date.Year, week);
}
```

- `PeriodType = 2` (Weekly), `PeriodMonth = 0`, `PeriodWeek = weekNumber`

---

## Settlement Statuses

| Value | Status | Description |
|-------|--------|-------------|
| 1 | Pending | Week active or awaiting admin review |
| 3 | Paid | Admin confirmed, **LOCKED** — no further changes allowed |
| 4 | Disputed | Provider disagrees with amounts |

**Removed:** Invoiced (2) — no longer needed.

### Rules

- **Locked once Paid** — no status changes allowed after Paid
- **Blocked until week ends** — admin cannot mark Paid while the settlement's week is still active
- **No wallet deduction at Paid** — commission already deducted per-transaction in real-time

### Removed Fields

- `InvoicedAt` — no Invoiced status
- `PaidAmount` — debt tracked in `WalletBalance`, per-settlement debt visible as `OutstandingAmount`

---

## Settlement Field Reference

| Field | Meaning |
|-------|---------|
| `PartnerTransactionAmount` | Total value of transactions at provider (user paid provider directly) |
| `PartnerCommissionAmount` | Cable's commission — **provider owes Cable** |
| `OfferPaymentAmount` | Redeemed offer value — **Cable owes provider** |
| `NetBalance` | `OfferPaymentAmount - PartnerCommissionAmount` — historical snapshot |
| `WalletApplied` | How much was covered by wallet's positive balance (accumulated per-transaction) |
| `OutstandingAmount` | `CommissionAmount - WalletApplied` (computed in DTO) — debt portion |

### Provider Wallet Fields (ChargingPoint / ServiceProvider)

| Field | Meaning |
|-------|---------|
| `WalletBalance` | Unified balance. Positive = credit. Negative = debt. Deducted per-transaction in real-time. |
| `WalletCreditLimit` | Max debt allowed (how negative wallet can go). `null` = unlimited. |

---

## Settlement Lifecycle

```
Transaction initiated
  → WalletBalance -= commission (real-time)
  → walletCovered = Min(Max(balance, 0), commission)
  → ProviderWalletTransaction created (CommissionDeduction)
         |
         v
Transaction completed (user scans QR)
  → SettlementService.UpsertSettlement
  → WalletApplied += walletCovered (atomic SQL update)
  → All amounts accumulated in settlement
         |
         v
      Pending (1)  ← amounts accumulate throughout the week
         |
   Week ends (Saturday)
         |
   Admin reviews
         |
    ┌────┴────┐
    v         v
 Paid (3)  Disputed (4)
 LOCKED      Can be resolved later
```

### Transaction Cancel/Expire Flow

```
Transaction cancelled or expired
  → WalletBalance += commission (refund)
  → ProviderWalletTransaction created (CommissionRefund)
  → Settlement NOT updated (transaction never completed)
```

### Wallet Debt Recovery Flow

```
Settlement Paid with insufficient wallet:
  Commission = 80, WalletApplied = 50
  WalletBalance = -30 (debt)
  OutstandingAmount = 30

Provider pays later:
  Admin calls AddWalletDeposit(amount: 40)
  WalletBalance = -30 + 40 = 10 (credit for next week)
```

---

## API Endpoints

### `PUT /api/offers/UpdateSettlementStatus/{id}`

**Request:**
```json
{
    "Status": 3,
    "Note": "Weekly settlement processed"
}
```

| Status | Action |
|--------|--------|
| Paid (3) | Checks week ended → sets `PaidAt = now` → **LOCKS** settlement |
| Disputed (4) | Sets status only |

**Blocked:**
- Already Paid → `"This settlement is already Paid and cannot be modified"`
- Current week active → `"Cannot mark settlement as Paid while the current week is still active"`

### `GET /api/offers/GetProviderSettlements`

Returns all settlements for a specific provider with filters.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `providerType` | string | Yes | `ChargingPoint` or `ServiceProvider` |
| `providerId` | int | Yes | Provider ID |
| `status` | int | No | Filter by status (1, 3, 4) |
| `year` | int | No | Filter by year |
| `week` | int | No | Filter by week number |
| `unpaidOnly` | bool | No | Show only Pending/Disputed settlements |
| `hasDebt` | bool | No | Paid settlements where `CommissionAmount - WalletApplied > 0` |

**Example:**
```
GET /api/offers/GetProviderSettlements?providerType=ChargingPoint&providerId=43&hasDebt=true
```

---

## Files Modified

### Domain Layer
| File | Change |
|------|--------|
| `Domain/Enitites/ChargingPoint.cs` | Removed `LoyaltyCreditLimit`, `LoyaltyCurrentBalance`. Added `WalletCreditLimit`. |
| `Domain/Enitites/ServiceProvider.cs` | Same as ChargingPoint |
| `Domain/Enitites/PartnerTransaction.cs` | Added `WalletCoveredAmount` |
| `Domain/Enitites/ProviderSettlement.cs` | Renamed `NetAmountDueToProvider` → `NetBalance`. Removed `InvoicedAt`, `PaidAmount`. |

### Enums
| File | Change |
|------|--------|
| `Cable.Core/Enums/WalletTransactionType.cs` | Added `CommissionDeduction=5`, `CommissionRefund=6` |

### EF Configurations
| File | Change |
|------|--------|
| `Infrastructrue/Persistence/Configurations/ChargingPointConfiguration.cs` | Removed Loyalty mappings, added `WalletCreditLimit` |
| `Infrastructrue/Persistence/Configurations/ServiceProviderConfiguration.cs` | Same as ChargingPoint |
| `Infrastructrue/Persistence/Configurations/PartnerTransactionConfiguration.cs` | Added `WalletCoveredAmount` mapping |
| `Infrastructrue/Persistence/Configurations/ProviderSettlementConfiguration.cs` | `PeriodType` default → Weekly, `NetBalance` column |

### Commands
| File | Change |
|------|--------|
| `Application/Partners/Commands/InitiatePartnerTransaction/` | Credit check via `WalletCreditLimit`, deducts `WalletBalance`, calculates `WalletCoveredAmount`, creates `ProviderWalletTransaction` |
| `Application/Partners/Commands/ConfirmPartnerTransaction/` | Refunds to `WalletBalance`, creates `ProviderWalletTransaction` (CommissionRefund) |
| `Application/Partners/Commands/CancelPartnerTransaction/` | Refunds to `WalletBalance`, creates `ProviderWalletTransaction` (CommissionRefund) |
| `Application/Partners/Commands/SetProviderCreditLimit/` | Uses `WalletCreditLimit` instead of `LoyaltyCreditLimit` |
| `Application/Offers/Commands/UpdateSettlementStatus/` | Removed wallet deduction logic. Paid = status change + lock only. |
| `Application/Offers/Commands/AddWalletDeposit/` | Blocks `CommissionDeduction`/`CommissionRefund` types (system-only) |
| `Application/Partners/Commands/RecordProviderPayment/` | **DELETED** — use `AddWalletDeposit` |

### Services
| File | Change |
|------|--------|
| `Infrastructrue/Services/SettlementService.cs` | Fixed NetBalance formula. Monthly → Weekly. Accumulates `WalletApplied` from `WalletCoveredAmount`. |
| `Infrastructrue/BackgroundJobs/BackgroundJobService.cs` | Refunds to `WalletBalance`, creates `ProviderWalletTransaction` per provider group |

### Queries
| File | Change |
|------|--------|
| `Application/Partners/Queries/GetProviderBalance/` | Uses `WalletBalance`/`WalletCreditLimit`, shows `ProviderWalletTransactions` |
| `Application/Offers/Queries/GetWalletBalance/` | Added `WalletCreditLimit`, `AvailableCredit` to response |
| `Application/Offers/Queries/GetSettlements/ProviderSettlementDto.cs` | Added `OutstandingAmount`, renamed `NetAmountDueToProvider` → `NetBalance` |
| `Application/Offers/Queries/GetProviderSettlements/` | **NEW** — provider-specific settlements with `hasDebt` filter |

### Routes
| File | Change |
|------|--------|
| `WebApi/Routes/PartnerRoutes.cs` | Removed `RecordProviderPayment` route |
| `WebApi/Routes/OfferRoutes.cs` | Added `GetProviderSettlements` route |

### Deleted Files
| File | Reason |
|------|--------|
| `Application/Partners/Commands/RecordProviderPayment/RecordProviderPaymentCommand.cs` | Replaced by `AddWalletDeposit` |
| `WebApi/Requests/Partners/RecordProviderPaymentRequest.cs` | Replaced by `AddWalletDeposit` |

---

## Database Migrations

### Settlement Table
```sql
-- Rename NetAmountDueToProvider → NetBalance
EXEC sp_rename 'ProviderSettlement.NetAmountDueToProvider', 'NetBalance', 'COLUMN';

-- Remove unused fields
ALTER TABLE ProviderSettlement DROP COLUMN InvoicedAt;
ALTER TABLE ProviderSettlement DROP COLUMN PaidAmount;

-- Convert old Invoiced records to Pending
UPDATE ProviderSettlement SET SettlementStatus = 1 WHERE SettlementStatus = 2;

-- Delete old monthly test data
DELETE FROM ProviderSettlement;
```

### Unified Wallet Migration
```sql
-- Add WalletCreditLimit
ALTER TABLE ChargingPoint ADD WalletCreditLimit decimal(18,3) NULL;
ALTER TABLE ServiceProvider ADD WalletCreditLimit decimal(18,3) NULL;

-- Copy existing credit limits
UPDATE ChargingPoint SET WalletCreditLimit = LoyaltyCreditLimit;
UPDATE ServiceProvider SET WalletCreditLimit = LoyaltyCreditLimit;

-- Merge LoyaltyCurrentBalance into WalletBalance
UPDATE ChargingPoint SET WalletBalance = WalletBalance + LoyaltyCurrentBalance;
UPDATE ServiceProvider SET WalletBalance = WalletBalance + LoyaltyCurrentBalance;

-- Add WalletCoveredAmount to PartnerTransaction
ALTER TABLE PartnerTransaction ADD WalletCoveredAmount decimal(18,3) NOT NULL DEFAULT 0;

-- Drop old columns (remove default constraints first)
ALTER TABLE ChargingPoint DROP COLUMN LoyaltyCreditLimit, LoyaltyCurrentBalance;
ALTER TABLE ServiceProvider DROP COLUMN LoyaltyCreditLimit, LoyaltyCurrentBalance;
```
