# Settlement API Guide — Mobile & Admin

## Overview

The settlement system generates **monthly** or **weekly** financial summaries per provider, aggregating two transaction types:

- **Offer Transactions** — User redeems loyalty points → Cable pays the provider
- **Partner Transactions** — User pays provider for a service → Cable takes a commission

---

## Settlement Period Types

| Value | Type | Description |
|-------|------|-------------|
| 1 | Monthly | Calendar month (default) |
| 2 | Weekly | ISO week (Monday–Sunday) |

## Settlement Statuses

| Value | Status | Description |
|-------|--------|-------------|
| 1 | Pending | Generated, awaiting admin review |
| 2 | Invoiced | Admin sent invoice to provider |
| 3 | Paid | Payment completed |
| 4 | Disputed | Under dispute |

```
Pending(1) ──→ Invoiced(2) ──→ Paid(3)
                    │
                    ↓
               Disputed(4) ──→ Paid(3)
```

---

## Net Amount Formula

```
NetAmountDueToProvider = (PartnerTransactionAmount - PartnerCommissionAmount) + OfferPaymentAmount
```

---

## Endpoints

### 1. Generate Settlement (Admin)

Generates settlement records for all providers with completed transactions in the given period.

```
POST /api/offers/GenerateSettlement
Authorization: Required (Admin)
```

**Request Body — Monthly (default):**
```json
{
  "year": 2026,
  "month": 3,
  "periodType": 1
}
```

**Request Body — Weekly:**
```json
{
  "year": 2026,
  "month": 0,
  "periodType": 2,
  "week": 11
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `year` | int | Yes | Settlement year |
| `month` | int | Yes | Settlement month (1-12 for monthly, 0 for weekly) |
| `periodType` | int | No | 1=Monthly (default), 2=Weekly |
| `week` | int? | No | ISO week number (1-53), required when periodType=2 |

**Response:** `int` — count of new settlements created

**Notes:**
- Only transactions with `Status = Completed(2)` are included
- Idempotent: running twice for the same period accumulates values, doesn't duplicate
- One settlement per provider per period (unique constraint)
- Weekly uses ISO 8601 week numbering (Monday–Sunday)

---

### 2. Get All Settlements (Admin)

Returns list of all settlements with optional filters.

```
GET /api/offers/GetSettlements?status={status}&month={month}&year={year}&periodType={periodType}&week={week}
Authorization: Required (Admin)
```

**Query Parameters (all optional):**

| Param | Type | Description |
|-------|------|-------------|
| `status` | int | Filter by status: 1=Pending, 2=Invoiced, 3=Paid, 4=Disputed |
| `month` | int | Filter by period month (1-12) |
| `year` | int | Filter by period year |
| `periodType` | int | Filter by type: 1=Monthly, 2=Weekly |
| `week` | int | Filter by ISO week number (1-53) |

**Response:** `List<ProviderSettlementDto>`

```json
[
  {
    "id": 4,
    "providerType": "ChargingPoint",
    "providerId": 100,
    "providerOwnerId": 3324,
    "providerOwnerName": "Ahmad",
    "periodYear": 2026,
    "periodMonth": 2,
    "periodType": 1,
    "periodWeek": 0,
    "partnerTransactionCount": 20,
    "partnerTransactionAmount": 1084858.540,
    "partnerCommissionAmount": 108485.854,
    "totalPointsAwarded": 542426,
    "offerTransactionCount": 0,
    "offerPaymentAmount": 0.000,
    "totalPointsDeducted": 0,
    "netAmountDueToProvider": 976372.686,
    "settlementStatus": 1,
    "invoicedAt": null,
    "paidAt": null,
    "paidAmount": null,
    "adminNote": null,
    "createdAt": "2026-03-02T10:37:42.777Z"
  }
]
```

**Ordering:** PeriodYear DESC, PeriodMonth DESC, PeriodWeek DESC (newest first)

**Examples:**
- Get all monthly: `?periodType=1`
- Get all weekly: `?periodType=2`
- Get weekly for specific week: `?periodType=2&year=2026&week=11`

---

### 3. Get Settlement Summary (Admin)

Returns aggregated dashboard totals across all providers.

```
GET /api/offers/GetSettlementSummary?month={month}&year={year}&periodType={periodType}&week={week}
Authorization: Required (Admin)
```

**Query Parameters (all optional):**

| Param | Type | Description |
|-------|------|-------------|
| `month` | int | Filter by period month |
| `year` | int | Filter by period year |
| `periodType` | int | Filter by type: 1=Monthly, 2=Weekly |
| `week` | int | Filter by ISO week number |

**Response:** `SettlementSummaryDto`

```json
{
  "totalSettlements": 6,
  "totalPartnerTransactions": 85,
  "totalPartnerTransactionAmount": 1086120.540,
  "totalPartnerCommissionAmount": 108612.054,
  "totalPointsAwarded": 543045,
  "totalOfferTransactions": 26,
  "totalOfferPaymentAmount": 309.000,
  "totalPointsDeducted": 27230,
  "totalNetAmountDueToProviders": 977817.486,
  "pendingCount": 6,
  "invoicedCount": 0,
  "paidCount": 0,
  "disputedCount": 0
}
```

---

### 4. Get Provider Settlement (Provider / Admin)

Returns a single settlement for a specific provider and period.

```
GET /api/offers/GetProviderSettlement?providerType={type}&providerId={id}&year={year}&month={month}&periodType={periodType}&week={week}
Authorization: Required
```

**Query Parameters:**

| Param | Type | Required | Description |
|-------|------|----------|-------------|
| `providerType` | string | Yes | `"ChargingPoint"` or `"ServiceProvider"` |
| `providerId` | int | Yes | ID of the provider |
| `year` | int | Yes | Settlement period year |
| `month` | int | Yes | Settlement month (1-12 for monthly, 0 for weekly) |
| `periodType` | int | Yes | 1=Monthly, 2=Weekly |
| `week` | int | Yes | ISO week number (0 for monthly, 1-53 for weekly) |

**Response:** `ProviderSettlementDto` (same structure as above)

**Error:** `404 Not Found` if no settlement exists for this provider/period

**Examples:**
- Monthly: `?providerType=ChargingPoint&providerId=100&year=2026&month=3&periodType=1&week=0`
- Weekly: `?providerType=ChargingPoint&providerId=100&year=2026&month=0&periodType=2&week=11`

---

### 5. Update Settlement Status (Admin)

Updates the status of a settlement (e.g., mark as Invoiced, Paid, or Disputed).

```
PUT /api/offers/UpdateSettlementStatus/{id}
Authorization: Required (Admin)
```

**Route Parameters:**

| Param | Type | Description |
|-------|------|-------------|
| `id` | int | Settlement ID |

**Request Body:**
```json
{
  "status": 2,
  "paidAmount": null,
  "note": "Invoice sent to provider"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `status` | int | Yes | New status: 1=Pending, 2=Invoiced, 3=Paid, 4=Disputed |
| `paidAmount` | decimal? | No | Amount paid (set when status = 3/Paid) |
| `note` | string? | No | Admin note (max 1000 chars) |

**Response:** `200 OK` (no body)

**Status-specific behavior:**
- **Invoiced (2):** Sets `invoicedAt = UTC now`
- **Paid (3):** Sets `paidAt = UTC now` and `paidAmount = request.paidAmount`
- **Disputed (4):** Stores `adminNote` for dispute reason

**Error:** `404 Not Found` if settlement doesn't exist

---

## Response DTO Reference

### ProviderSettlementDto

| Field | Type | Description |
|-------|------|-------------|
| `id` | int | Settlement ID |
| `providerType` | string | `"ChargingPoint"` or `"ServiceProvider"` |
| `providerId` | int | Provider ID |
| `providerOwnerId` | int | Owner user ID |
| `providerOwnerName` | string? | Owner display name |
| `periodYear` | int | Settlement year |
| `periodMonth` | int | Settlement month (0 for weekly) |
| `periodType` | int | 1=Monthly, 2=Weekly |
| `periodWeek` | int | ISO week number (0 for monthly) |
| `partnerTransactionCount` | int | Number of completed partner transactions |
| `partnerTransactionAmount` | decimal | Total amount users paid to provider |
| `partnerCommissionAmount` | decimal | Total commission Cable retained |
| `totalPointsAwarded` | int | Loyalty points users earned |
| `offerTransactionCount` | int | Number of completed offer redemptions |
| `offerPaymentAmount` | decimal | Total Cable paid provider for offer redemptions |
| `totalPointsDeducted` | int | Loyalty points users spent |
| `netAmountDueToProvider` | decimal | Net amount Cable owes provider |
| `settlementStatus` | int | Current status (1-4) |
| `invoicedAt` | datetime? | When marked as invoiced |
| `paidAt` | datetime? | When marked as paid |
| `paidAmount` | decimal? | Actual amount paid |
| `adminNote` | string? | Admin notes |
| `createdAt` | datetime | Record creation date |

### SettlementSummaryDto

| Field | Type | Description |
|-------|------|-------------|
| `totalSettlements` | int | Total settlement count |
| `totalPartnerTransactions` | int | Sum of all partner transaction counts |
| `totalPartnerTransactionAmount` | decimal | Sum of all user payments |
| `totalPartnerCommissionAmount` | decimal | Sum of all Cable commissions |
| `totalPointsAwarded` | int | Sum of all points awarded |
| `totalOfferTransactions` | int | Sum of all offer redemption counts |
| `totalOfferPaymentAmount` | decimal | Sum of all Cable payments for offers |
| `totalPointsDeducted` | int | Sum of all points deducted |
| `totalNetAmountDueToProviders` | decimal | Sum of all net amounts |
| `pendingCount` | int | Settlements with status Pending |
| `invoicedCount` | int | Settlements with status Invoiced |
| `paidCount` | int | Settlements with status Paid |
| `disputedCount` | int | Settlements with status Disputed |

---

## Endpoints Quick Reference

| # | Method | Route | Role | Purpose |
|---|--------|-------|------|---------|
| 1 | POST | `/api/offers/GenerateSettlement` | Admin | Generate monthly/weekly settlements |
| 2 | GET | `/api/offers/GetSettlements` | Admin | List all settlements (filterable) |
| 3 | GET | `/api/offers/GetSettlementSummary` | Admin | Dashboard summary totals |
| 4 | GET | `/api/offers/GetProviderSettlement` | Provider/Admin | Single provider settlement |
| 5 | PUT | `/api/offers/UpdateSettlementStatus/{id}` | Admin | Update status |

---

## Money Flow Explanation

```
Provider: Yahia (ServiceProvider)

Offer Redemptions:  12 transactions
Cable pays provider:        150.000 JOD

Partner Transactions: 45 transactions
Total user payments:      1,000.000 JOD
Cable commission (20%):    -200.000 JOD

Net due to provider:        950.000 JOD
Paid so far:                  0.000 JOD
Remaining:                  950.000 JOD
```

- **Offer side:** Cable OWES provider (users spent points, Cable pays cash)
- **Partner side:** Users paid provider, Cable TAKES commission
- **Net:** What Cable owes after deducting commission from partner revenue + adding offer payments
