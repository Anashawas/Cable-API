# Settlement Auto-Update System

## Overview

Replaced the manual `GenerateSettlement` admin endpoint with a **real-time auto-update** system. Settlements are now automatically created/updated the moment a transaction is completed (user scans QR code), eliminating the need for manual regeneration.

---

## What Changed

### Problem (Before)
- Settlements were only created when an admin manually called `POST /api/offers/GenerateSettlement`
- Settlement data was always stale until the admin regenerated
- A Hangfire job ran monthly to auto-generate, but data was still outdated between runs

### Solution (After)
- Settlements are updated **in real-time** when any PartnerTransaction or OfferTransaction is completed
- Uses `ExecuteUpdateAsync` for **atomic SQL-level increments** (concurrency-safe)
- Only updates `Pending` settlements (never modifies Invoiced/Paid/Disputed ones)
- No manual intervention needed

---

## New Files Created (2)

### 1. `Application/Common/Interfaces/ISettlementService.cs`

```csharp
public interface ISettlementService
{
    Task UpsertSettlementForPartnerTransactionAsync(
        PartnerTransaction transaction, CancellationToken cancellationToken = default);

    Task UpsertSettlementForOfferTransactionAsync(
        OfferTransaction transaction, CancellationToken cancellationToken = default);
}
```

- Does NOT call SaveChanges -- caller is responsible (same DB transaction)

### 2. `Infrastructrue/Services/SettlementService.cs`

**Public Methods:**
- `UpsertSettlementForPartnerTransactionAsync` -- extracts year/month from `CompletedAt`, resolves ownerId, increments partner amounts
- `UpsertSettlementForOfferTransactionAsync` -- extracts year/month from `CompletedAt`, resolves ownerId from `ProviderOffers.ProposedByUserId`, increments offer amounts

**Private Methods:**
- `UpsertSettlement` -- Core logic using `ExecuteUpdateAsync`:
  - Finds existing Pending settlement by `(ProviderType, ProviderId, PeriodType=Monthly, PeriodYear, PeriodMonth, PeriodWeek=0, !IsDeleted, Status=Pending)`
  - If found: atomically increments all counters and recalculates `NetAmountDueToProvider`
  - If not found: creates a new settlement with `Status=Pending`
- `ResolveProviderOwnerId` -- queries ChargingPoints/ServiceProviders for OwnerId

**Concurrency Safety:**
- `ExecuteUpdateAsync` generates SQL: `SET column = column + value` (atomic, no lost updates)
- `SettlementStatus == Pending` filter prevents modifying finalized settlements
- Filtered unique index prevents duplicate settlement creation

---

## Modified Files (4)

### 3. `Application/Offers/Commands/ConfirmOfferTransaction/ConfirmOfferTransactionCommand.cs`

- Added `ISettlementService settlementService` to constructor
- Added settlement upsert call after marking transaction as Completed, before SaveChanges:

```csharp
// Auto-update settlement for this transaction
await settlementService.UpsertSettlementForOfferTransactionAsync(transaction, cancellationToken);
```

### 4. `Application/Partners/Commands/ConfirmPartnerTransaction/ConfirmPartnerTransactionCommand.cs`

- Added `ISettlementService settlementService` to constructor
- Added settlement upsert call after marking transaction as Completed, before SaveChanges:

```csharp
// Auto-update settlement for this transaction
await settlementService.UpsertSettlementForPartnerTransactionAsync(transaction, cancellationToken);
```

### 5. `Infrastructrue/DependencyInjection.cs`

- Added DI registration in `RegisterBackgroundJobServices`:

```csharp
services.AddScoped<ISettlementService, Services.SettlementService>();
```

### 6. `Infrastructrue/Persistence/Configurations/ProviderSettlementConfiguration.cs`

- Added filtered unique index (only enforces uniqueness on active records):

```csharp
builder.HasIndex(e => new { e.ProviderType, e.ProviderId, e.PeriodType, e.PeriodYear, e.PeriodMonth, e.PeriodWeek })
    .IsUnique()
    .HasFilter("IsDeleted = 0")  // <-- ADDED
    .HasDatabaseName("IX_ProviderSettlement_Provider_Period");
```

---

## Removed Files & Code

### Deleted Files (2)
| File | What it was |
|------|-------------|
| `Application/Offers/Commands/GenerateSettlement/GenerateSettlementCommand.cs` | MediatR command + handler for manual settlement generation |
| `WebApi/Requests/Offers/GenerateSettlementRequest.cs` | Request DTO for GenerateSettlement endpoint |

### Removed from `WebApi/Routes/OfferRoutes.cs`
- `POST /api/offers/GenerateSettlement` endpoint (entire route definition)
- `using Application.Offers.Commands.GenerateSettlement;` import

### Removed from `Application/Common/Interfaces/IBackgroundJobService.cs`
- `GenerateMonthlySettlementsAsync(int year, int month, CancellationToken)` method
- `GenerateWeeklySettlementsAsync(int year, int week, CancellationToken)` method
- Settlement section comment block

### Removed from `Infrastructrue/BackgroundJobs/BackgroundJobService.cs`
- `GenerateMonthlySettlementsAsync` method (~100 lines)
- `GenerateWeeklySettlementsAsync` method (~100 lines)
- `UpsertSettlement` private helper method (~60 lines)
- `ResolveProviderOwnerId` private helper method (~20 lines)
- `using System.Globalization;` import (was only for `ISOWeek`)

### Removed from `WebApi/Program.cs`
- Hangfire recurring job `"generate-monthly-settlements"` (ran on 1st of each month at 00:30 UTC)

---

## How It Works (Flow)

### Partner Transaction Completion
```
User scans QR code
  -> ScanPartnerCodeCommand.Handle()
    -> transaction.Status = Completed
    -> settlementService.UpsertSettlementForPartnerTransactionAsync()
      -> ExecuteUpdateAsync (atomic SQL increment)
      -> OR create new Pending settlement if none exists
    -> SaveChanges()
```

### Offer Transaction Completion
```
User scans QR code
  -> ScanOfferCodeCommand.Handle()
    -> Deduct loyalty points
    -> transaction.Status = Completed
    -> settlementService.UpsertSettlementForOfferTransactionAsync()
      -> ExecuteUpdateAsync (atomic SQL increment)
      -> OR create new Pending settlement if none exists
    -> SaveChanges()
```

---

## Net Amount Calculation

```
NetAmountDueToProvider = (PartnerTransactionAmount - PartnerCommissionAmount) + OfferPaymentAmount
```

- **PartnerTransactionAmount**: Total value of partner transactions
- **PartnerCommissionAmount**: Commission deducted from partner transactions
- **OfferPaymentAmount**: Total monetary value of redeemed offers

---

## Concurrency Issues Fixed

| Issue | Fix |
|-------|-----|
| **Lost updates** (two concurrent transactions overwrite each other) | `ExecuteUpdateAsync` generates atomic SQL `SET column = column + value` |
| **Duplicate settlement creation** | Filtered unique index `WHERE IsDeleted = 0` on composite key |
| **Modifying finalized settlements** | `SettlementStatus == Pending` filter in WHERE clause |

---

## Database Migration Required

Run this SQL to update the unique index:

```sql
DROP INDEX IX_ProviderSettlement_Provider_Period ON ProviderSettlement;

CREATE UNIQUE INDEX IX_ProviderSettlement_Provider_Period
ON ProviderSettlement (ProviderType, ProviderId, PeriodType, PeriodYear, PeriodMonth, PeriodWeek)
WHERE IsDeleted = 0;
```

---

## Settlement Lifecycle

```
Pending  ->  Invoiced  ->  Paid
                |
                v
            Disputed
```

- Auto-update only touches **Pending** settlements
- If a settlement is already Invoiced/Paid/Disputed, a **new Pending** settlement is created for that period
- Settlement status transitions are managed by the admin via `UpdateSettlementStatus` endpoint
