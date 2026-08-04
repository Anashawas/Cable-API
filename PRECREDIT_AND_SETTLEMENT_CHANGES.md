# Provider PreCredit (Advance Payment) & Settlement Response Enrichment

## Overview

Two features implemented in this release:

1. **Provider PreCredit System** — Providers pay Cable money in advance (deposit). When Cable processes settlements, it can deduct from the prepaid balance instead of paying cash. Running balance carries across all settlements.
2. **Settlement Response Enrichment** — Settlement API responses now include nested provider details, owner details, and grouped transaction summaries as separate objects.

---

## How PreCredit Works

```
1. Provider deposits 10,000 JOD with Cable    → PreCredit balance: 10,000
2. Settlement generated: Cable owes 3,000 JOD  → Admin applies PreCredit → balance: 7,000
3. Next settlement: Cable owes 5,000 JOD       → Admin applies PreCredit → balance: 2,000
```

- Separate from the existing Credit Limit / `LoyaltyCurrentBalance` system
- `LoyaltyCurrentBalance` tracks partner transaction commission debt
- `PreCreditBalance` tracks advance deposits for settlement deduction

---

## Files Created (6 new files)

### 1. `Cable.Core/Emuns/PreCreditTransactionType.cs`

New enum for PreCredit transaction types:

```csharp
namespace Cable.Core.Emuns;

public enum PreCreditTransactionType
{
    Deposit = 1,              // Provider pays Cable
    SettlementDeduction = 2,  // Deducted when settlement is paid
    Refund = 3,               // Cable returns money to provider
    Adjustment = 4            // Admin manual correction
}
```

### 2. `Domain/Enitites/ProviderPreCreditTransaction.cs`

New entity with running balance (follows `LoyaltyPointTransaction` pattern):

```csharp
public class ProviderPreCreditTransaction : BaseAuditableEntity
{
    public string ProviderType { get; set; } = null!;   // "ChargingPoint" or "ServiceProvider"
    public int ProviderId { get; set; }
    public int TransactionType { get; set; }             // PreCreditTransactionType enum
    public decimal Amount { get; set; }                  // positive=deposit, negative=deduction
    public decimal BalanceAfter { get; set; }            // running balance snapshot
    public string? ReferenceType { get; set; }           // "Settlement", "Manual", etc.
    public int? ReferenceId { get; set; }                // Settlement ID when type=SettlementDeduction
    public string? Note { get; set; }
    public int RecordedByUserId { get; set; }
    public virtual UserAccount RecordedByUser { get; set; } = null!;
}
```

### 3. `Infrastructrue/Persistence/Configurations/ProviderPreCreditTransactionConfiguration.cs`

EF Core configuration:
- `Amount` → `decimal(18,3)`
- `BalanceAfter` → `decimal(18,3)`
- `ProviderType` → `nvarchar(50)`, required
- `Note` → `nvarchar(1000)`
- `ReferenceType` → `nvarchar(50)`
- Index on `(ProviderType, ProviderId)` for fast lookups
- Index on `RecordedByUserId`
- FK to `UserAccount` via `RecordedByUserId`

### 4. `Application/Offers/Commands/AddPreCredit/AddPreCreditCommand.cs`

New command to record provider deposits, refunds, and adjustments:

```csharp
public record AddPreCreditResult(decimal NewBalance);

public record AddPreCreditCommand(
    string ProviderType,
    int ProviderId,
    decimal Amount,
    int TransactionType,
    string? Note
) : IRequest<AddPreCreditResult>;
```

**Handler logic:**
- Validates `ProviderType` is `"ChargingPoint"` or `"ServiceProvider"`
- Blocks `SettlementDeduction` type (reserved for settlement processing)
- Validates `Amount != 0`
- Updates provider's `PreCreditBalance`
- Creates `ProviderPreCreditTransaction` with `BalanceAfter` snapshot
- Returns the new balance

### 5. `Application/Offers/Queries/GetPreCreditBalance/GetPreCreditBalanceRequest.cs`

New query to get provider's current PreCredit balance:

```csharp
public record PreCreditBalanceDto(
    string ProviderType,
    int ProviderId,
    string? ProviderOwnerName,
    decimal PreCreditBalance,
    decimal TotalDeposited,
    decimal TotalDeducted
);

public record GetPreCreditBalanceRequest(
    string ProviderType,
    int ProviderId
) : IRequest<PreCreditBalanceDto>;
```

**Handler logic:**
- Loads provider with Owner include
- Sums positive transactions → `TotalDeposited`
- Sums abs(negative transactions) → `TotalDeducted`

### 6. `Application/Offers/Queries/GetPreCreditHistory/GetPreCreditHistoryRequest.cs`

New query to get transaction history:

```csharp
public record PreCreditTransactionDto(
    int Id,
    string ProviderType,
    int ProviderId,
    int TransactionType,
    decimal Amount,
    decimal BalanceAfter,
    string? ReferenceType,
    int? ReferenceId,
    string? Note,
    string? RecordedByUserName,
    DateTime CreatedAt
);

public record GetPreCreditHistoryRequest(
    string ProviderType,
    int ProviderId
) : IRequest<List<PreCreditTransactionDto>>;
```

**Handler logic:**
- Loads all transactions for provider, ordered newest first
- Includes `RecordedByUser` for name display

### 7. `WebApi/Requests/Offers/AddPreCreditRequest.cs`

New API request model:

```csharp
public record AddPreCreditRequest(
    string ProviderType,
    int ProviderId,
    decimal Amount,
    int TransactionType,
    string? Note
);
```

---

## Files Modified (10 files)

### 1. `Domain/Enitites/ChargingPoint.cs`

Added:
```csharp
public decimal PreCreditBalance { get; set; }  // Current advance payment balance
```

### 2. `Domain/Enitites/ServiceProvider.cs`

Added:
```csharp
public decimal PreCreditBalance { get; set; }  // Current advance payment balance
```

### 3. `Domain/Enitites/ProviderSettlement.cs`

Added:
```csharp
public decimal PreCreditApplied { get; set; }  // How much PreCredit was deducted for this settlement
```

### 4. `Infrastructrue/Persistence/Configurations/ProviderSettlementConfiguration.cs`

Added `PreCreditApplied` column config → `decimal(18,3)`, default `0m`.

### 5. `Application/Common/Interfaces/IApplicationDbContext.cs`

Added:
```csharp
DbSet<ProviderPreCreditTransaction> ProviderPreCreditTransactions { get; set; }
```

### 6. `Infrastructrue/Persistence/ApplicationDbContext.cs`

Registered `ProviderPreCreditTransactions` DbSet.

### 7. `Application/Offers/Queries/GetSettlements/ProviderSettlementDto.cs`

Restructured from flat fields to nested sub-objects:

```csharp
public record ProviderDetailsDto(
    string ProviderType, int ProviderId,
    string? Name, string? Phone, string? Address, string? Icon
);

public record OwnerDetailsDto(
    int OwnerId, string? Name, string? Email, string? Phone
);

public record PartnerTransactionSummaryDto(
    int TransactionCount, decimal TransactionAmount,
    decimal CommissionAmount, int TotalPointsAwarded
);

public record OfferTransactionSummaryDto(
    int TransactionCount, decimal PaymentAmount, int TotalPointsDeducted
);

public record ProviderSettlementDto(
    int Id,
    ProviderDetailsDto ProviderDetails,
    OwnerDetailsDto OwnerDetails,
    int PeriodYear, int PeriodMonth, int PeriodType, int PeriodWeek,
    PartnerTransactionSummaryDto PartnerTransactions,
    OfferTransactionSummaryDto OfferTransactions,
    decimal NetAmountDueToProvider,
    decimal PreCreditApplied,
    int SettlementStatus,
    DateTime? InvoicedAt, DateTime? PaidAt, decimal? PaidAmount,
    string? AdminNote, DateTime CreatedAt
);
```

### 8. `Application/Offers/Queries/GetSettlements/GetSettlementsRequest.cs`

Updated projection to construct nested objects. Uses batch loading pattern:
- Collect distinct ChargingPoint/ServiceProvider IDs
- Load via `ToDictionaryAsync` in two queries
- Map to nested DTOs in `Select`

### 9. `Application/Offers/Queries/GetProviderSettlement/GetProviderSettlementRequest.cs`

Updated projection to construct nested objects. Uses single provider lookup.

### 10. `Application/Offers/Queries/GetSettlementSummary/GetSettlementSummaryRequest.cs`

Added `TotalPreCreditApplied` field to `SettlementSummaryDto`:

```csharp
public record SettlementSummaryDto(
    // ... existing fields ...
    decimal TotalPreCreditApplied,
    // ... status counts ...
);
```

Handler sums `PreCreditApplied` across all filtered settlements.

### 11. `Application/Offers/Commands/UpdateSettlementStatus/UpdateSettlementStatusCommand.cs`

Added `PreCreditAmount` parameter:

```csharp
public record UpdateSettlementStatusCommand(
    int Id, int Status, decimal? PaidAmount,
    decimal? PreCreditAmount, string? Note
) : IRequest;
```

**New handler logic when `Status = Paid` and `PreCreditAmount > 0`:**
1. Looks up provider (ChargingPoint or ServiceProvider)
2. Validates sufficient `PreCreditBalance`
3. Deducts `PreCreditAmount` from provider's balance
4. Creates `ProviderPreCreditTransaction` (type=SettlementDeduction, negative amount, with `BalanceAfter`)
5. Sets `settlement.PreCreditApplied = preCreditAmount`

### 12. `WebApi/Requests/Offers/UpdateSettlementStatusRequest.cs`

Added `PreCreditAmount` field:

```csharp
public record UpdateSettlementStatusRequest(
    int Status, decimal? PaidAmount, decimal? PreCreditAmount, string? Note
);
```

### 13. `WebApi/Routes/OfferRoutes.cs`

Added 3 new endpoints and updated 1 existing:

- **NEW** `POST /api/offers/AddPreCredit` — record deposit/refund/adjustment
- **NEW** `GET /api/offers/GetPreCreditBalance` — view current balance
- **NEW** `GET /api/offers/GetPreCreditHistory` — view transaction history
- **UPDATED** `PUT /api/offers/UpdateSettlementStatus/{id}` — passes `PreCreditAmount` to command

---

## API Endpoints

### POST `/api/offers/AddPreCredit` (Admin)

Records a deposit, refund, or adjustment to provider's PreCredit balance.

**Request:**
```json
{
  "providerType": "ChargingPoint",
  "providerId": 100,
  "amount": 5000.000,
  "transactionType": 1,
  "note": "Bank transfer ref #789"
}
```

| Field           | Type     | Required | Description                                                  |
|-----------------|----------|----------|--------------------------------------------------------------|
| providerType    | string   | Yes      | `"ChargingPoint"` or `"ServiceProvider"`                     |
| providerId      | int      | Yes      | Provider ID                                                  |
| amount          | decimal  | Yes      | Amount (positive for deposit, can be negative for adjustment)|
| transactionType | int      | Yes      | `1`=Deposit, `3`=Refund, `4`=Adjustment                     |
| note            | string?  | No       | Optional note                                                |

**Response:** `200 OK`
```json
{
  "newBalance": 5000.000
}
```

---

### GET `/api/offers/GetPreCreditBalance` (Admin/Provider)

Returns provider's current PreCredit balance with totals.

**Query Parameters:** `providerType`, `providerId`

**Response:**
```json
{
  "providerType": "ChargingPoint",
  "providerId": 100,
  "providerOwnerName": "Ahmad",
  "preCreditBalance": 5000.000,
  "totalDeposited": 10000.000,
  "totalDeducted": 5000.000
}
```

---

### GET `/api/offers/GetPreCreditHistory` (Admin/Provider)

Returns all PreCredit transactions for a provider, ordered newest first.

**Query Parameters:** `providerType`, `providerId`

**Response:**
```json
[
  {
    "id": 1,
    "providerType": "ChargingPoint",
    "providerId": 100,
    "transactionType": 1,
    "amount": 10000.000,
    "balanceAfter": 10000.000,
    "referenceType": "Manual",
    "referenceId": null,
    "note": "Initial deposit",
    "recordedByUserName": "Admin",
    "createdAt": "2026-03-13T10:00:00Z"
  },
  {
    "id": 2,
    "providerType": "ChargingPoint",
    "providerId": 100,
    "transactionType": 2,
    "amount": -3000.000,
    "balanceAfter": 7000.000,
    "referenceType": "Settlement",
    "referenceId": 4,
    "note": "Applied to settlement #4",
    "recordedByUserName": "Admin",
    "createdAt": "2026-03-13T12:00:00Z"
  }
]
```

---

### PUT `/api/offers/UpdateSettlementStatus/{id}` (Admin) — Updated

Added optional `preCreditAmount` field to deduct from provider's advance payment balance when marking as Paid.

**Request:**
```json
{
  "status": 3,
  "paidAmount": 7000.000,
  "preCreditAmount": 3000.000,
  "note": "Paid with PreCredit deduction"
}
```

| Field           | Type      | Required | Description                                              |
|-----------------|-----------|----------|----------------------------------------------------------|
| status          | int       | Yes      | `1`=Pending, `2`=Invoiced, `3`=Paid, `4`=Disputed       |
| paidAmount      | decimal?  | No       | Amount paid (when status=3)                              |
| preCreditAmount | decimal?  | No       | Amount to deduct from PreCredit (only when status=3)     |
| note            | string?   | No       | Admin note                                               |

**Validation:**
- `preCreditAmount` only applicable when `status = 3 (Paid)`
- Checks provider has sufficient `PreCreditBalance`
- Returns error if insufficient: `"Insufficient PreCredit balance. Available: X.XXX"`

---

### GET `/api/offers/GetSettlements` — Updated Response Structure

**Response now uses nested objects:**
```json
[
  {
    "id": 1,
    "providerDetails": {
      "providerType": "ChargingPoint",
      "providerId": 100,
      "name": "EV Station Downtown",
      "phone": "079XXXXXXX",
      "address": "Amman, Jordan",
      "icon": "https://..."
    },
    "ownerDetails": {
      "ownerId": 5,
      "name": "Ahmad",
      "email": "ahmad@email.com",
      "phone": "079XXXXXXX"
    },
    "periodYear": 2026,
    "periodMonth": 3,
    "periodType": 1,
    "periodWeek": 0,
    "partnerTransactions": {
      "transactionCount": 50,
      "transactionAmount": 5000.000,
      "commissionAmount": 500.000,
      "totalPointsAwarded": 100
    },
    "offerTransactions": {
      "transactionCount": 10,
      "paymentAmount": 2000.000,
      "totalPointsDeducted": 50
    },
    "netAmountDueToProvider": 6500.000,
    "preCreditApplied": 0.000,
    "settlementStatus": 1,
    "invoicedAt": null,
    "paidAt": null,
    "paidAmount": null,
    "adminNote": null,
    "createdAt": "2026-03-13T10:00:00Z"
  }
]
```

### GET `/api/offers/GetProviderSettlement` — Updated Response Structure

Same nested structure as `GetSettlements` (single object instead of array).

### GET `/api/offers/GetSettlementSummary` — Updated Response

Added `totalPreCreditApplied` field:
```json
{
  "totalSettlements": 15,
  "totalPartnerTransactions": 500,
  "totalPartnerTransactionAmount": 50000.000,
  "totalPartnerCommissionAmount": 5000.000,
  "totalPointsAwarded": 1000,
  "totalOfferTransactions": 100,
  "totalOfferPaymentAmount": 20000.000,
  "totalPointsDeducted": 500,
  "totalNetAmountDueToProviders": 65000.000,
  "totalPreCreditApplied": 8000.000,
  "pendingCount": 5,
  "invoicedCount": 3,
  "paidCount": 6,
  "disputedCount": 1
}
```

---

## Database Migration SQL

Run these statements against the SQL Server database:

```sql
-- 1. Add PreCreditBalance to provider entities
ALTER TABLE ChargingPoint ADD PreCreditBalance DECIMAL(18,3) NOT NULL DEFAULT 0;
ALTER TABLE ServiceProvider ADD PreCreditBalance DECIMAL(18,3) NOT NULL DEFAULT 0;

-- 2. Add PreCreditApplied to settlement
ALTER TABLE ProviderSettlement ADD PreCreditApplied DECIMAL(18,3) NOT NULL DEFAULT 0;

-- 3. Create PreCredit transaction table
CREATE TABLE ProviderPreCreditTransaction (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    ProviderType NVARCHAR(50) NOT NULL,
    ProviderId INT NOT NULL,
    TransactionType INT NOT NULL,
    Amount DECIMAL(18,3) NOT NULL,
    BalanceAfter DECIMAL(18,3) NOT NULL,
    ReferenceType NVARCHAR(50) NULL,
    ReferenceId INT NULL,
    Note NVARCHAR(1000) NULL,
    RecordedByUserId INT NOT NULL,
    CreatedAt DATETIME NOT NULL DEFAULT GETUTCDATE(),
    ModifiedAt DATETIME NULL,
    IsDeleted BIT NOT NULL DEFAULT 0,
    FOREIGN KEY (RecordedByUserId) REFERENCES UserAccount(Id)
);

-- 4. Create indexes
CREATE INDEX IX_ProviderPreCreditTransaction_Provider
ON ProviderPreCreditTransaction (ProviderType, ProviderId);

CREATE INDEX IX_ProviderPreCreditTransaction_RecordedByUser
ON ProviderPreCreditTransaction (RecordedByUserId);
```

---

## Nested DTO Structure

The settlement response was restructured from flat fields to nested sub-objects:

| Sub-Object                     | Fields                                                        |
|-------------------------------|---------------------------------------------------------------|
| `providerDetails`             | providerType, providerId, name, phone, address, icon          |
| `ownerDetails`                | ownerId, name, email, phone                                   |
| `partnerTransactions`         | transactionCount, transactionAmount, commissionAmount, totalPointsAwarded |
| `offerTransactions`           | transactionCount, paymentAmount, totalPointsDeducted          |

---

## PreCreditTransactionType Values

| Value | Name                 | Description                                |
|-------|---------------------|--------------------------------------------|
| 1     | Deposit             | Provider pays Cable (advance payment)      |
| 2     | SettlementDeduction | Auto-deducted when settlement marked Paid  |
| 3     | Refund              | Cable returns money to provider            |
| 4     | Adjustment          | Admin manual balance correction            |

> **Note:** `SettlementDeduction (2)` is reserved and cannot be used via the `AddPreCredit` endpoint. It is only created automatically when a settlement is marked as Paid with `preCreditAmount > 0`.

---

## Verification Checklist

1. `dotnet build` — 0 errors
2. Run DB migration SQL
3. Test `POST /api/offers/AddPreCredit` — deposit 10,000 for a provider → verify balance
4. Test `GET /api/offers/GetPreCreditBalance` → shows correct balance
5. Test `GET /api/offers/GetPreCreditHistory` → shows deposit transaction
6. Test `PUT /api/offers/UpdateSettlementStatus` with `preCreditAmount` → verify balance decreases
7. Test insufficient balance → error returned
8. Test existing settlement flow without PreCredit → still works (backward compatible)
9. Verify `GET /api/offers/GetSettlements` returns nested objects
10. Verify `GET /api/offers/GetSettlementSummary` includes `totalPreCreditApplied`
