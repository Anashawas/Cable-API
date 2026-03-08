# API Changes - Provider Credit Limit System

**Date:** 2026-02-28
**Base URL:** `/api/partners`

---

## Overview

Providers accumulate commission debt to Cable through partner transactions. This feature adds a **credit limit** system: when a provider's unpaid commission reaches the limit, they are blocked from creating new partner transactions until they pay. Providers can also **pay in advance** to build credit.

**Balance logic:**
- Balance starts at `0`
- Commission is **reserved (deducted)** when staff initiates a transaction
- Commission is **refunded** if the transaction is cancelled or expires
- Advance payments **increase** the balance
- When `balance - commission < -creditLimit` → transaction is **blocked**

---

## 1. New Admin Endpoints

### 1.1 POST `/api/partners/admin/RecordProviderPayment`

**Purpose:** Admin records a payment from a provider (advance payment or debt settlement).

**Request body:**

```json
{
  "providerType": "ChargingPoint",
  "providerId": 45,
  "amount": 50.000,
  "note": "Bank transfer ref #12345"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| providerType | string | Yes | `"ChargingPoint"` or `"ServiceProvider"` |
| providerId | int | Yes | Provider ID |
| amount | decimal | Yes | Payment amount (must be > 0) |
| note | string? | No | Optional note (e.g., payment reference) |

**Response:** `200 OK` (no body)

**Error responses:**
- `404` - Provider not found
- `400` - Amount must be greater than zero
- `400` - Invalid ProviderType

---

### 1.2 PUT `/api/partners/admin/SetCreditLimit`

**Purpose:** Admin sets or updates a provider's credit limit. Set to `null` for unlimited.

**Request body:**

```json
{
  "providerType": "ChargingPoint",
  "providerId": 45,
  "creditLimit": 20.000
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| providerType | string | Yes | `"ChargingPoint"` or `"ServiceProvider"` |
| providerId | int | Yes | Provider ID |
| creditLimit | decimal? | Yes | Credit limit amount. `null` = unlimited (no restriction) |

**Response:** `200 OK` (no body)

**Error responses:**
- `404` - Provider not found
- `400` - Credit limit must be positive or null
- `400` - Invalid ProviderType

---

### 1.3 GET `/api/partners/admin/GetProviderBalance`

**Purpose:** View a provider's credit limit, current balance, available credit, and recent payments.

**Query parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| providerType | string | Yes | `"ChargingPoint"` or `"ServiceProvider"` |
| providerId | int | Yes | Provider ID |

**Response (ProviderBalanceDto):**

```json
{
  "creditLimit": 20.000,
  "currentBalance": 5.000,
  "availableCredit": 25.000,
  "recentPayments": [
    {
      "id": 1,
      "amount": 50.000,
      "note": "Bank transfer ref #12345",
      "recordedByUserName": "Admin User",
      "createdAt": "2026-02-28T10:30:00Z"
    }
  ]
}
```

| Field | Type | Description |
|-------|------|-------------|
| creditLimit | decimal? | Max debt allowed. `null` = unlimited |
| currentBalance | decimal | Running balance. Positive = credit, Negative = debt |
| availableCredit | decimal? | `creditLimit + currentBalance`. `null` if no limit set |
| recentPayments | array | Last 10 payment records |
| recentPayments[].id | int | Payment ID |
| recentPayments[].amount | decimal | Payment amount |
| recentPayments[].note | string? | Payment note |
| recentPayments[].recordedByUserName | string? | Admin who recorded it |
| recentPayments[].createdAt | datetime | When payment was recorded |

**Error responses:**
- `404` - Provider not found
- `400` - Invalid ProviderType

---

## 2. Modified Existing Endpoints

### 2.1 POST `/api/partners/provider/CreateTransaction` (InitiatePartnerTransaction)

**What changed:** Now checks credit limit before creating the transaction and reserves the commission from the provider's balance.

**New error response:**
- `400` with key `"CreditLimit"` — Provider credit limit reached

**Error message format:**
```
"Provider credit limit reached. Available credit: 5.000 KWD"
```

**Flutter action items:**
- Handle the new `CreditLimit` validation error
- Display the available credit amount to the provider staff
- Consider showing current balance/limit info on the transaction creation screen

### 2.2 POST `/api/partners/provider/CancelTransaction/{id}` (CancelPartnerTransaction)

**What changed:** Now refunds the reserved commission back to the provider's balance when a transaction is cancelled.

**No API contract change** — same request/response as before.

### 2.3 POST `/api/partners/ScanPartnerCode` (ScanPartnerCode / ConfirmPartnerTransaction)

**What changed:** When a user scans an expired code, the reserved commission is now refunded to the provider's balance before returning the expiry error.

**No API contract change** — same request/response as before.

---

## 3. Background Job Changes

### ExpirePartnerTransactionCodes

**What changed:** When the background job batch-expires initiated transactions, it now also refunds the reserved commission to each provider's balance (grouped by provider for efficiency).

**No API change** — runs automatically.

---

## 4. Balance Flow Summary

| Event | Balance Effect |
|-------|----------------|
| Staff initiates transaction (commission = 5 KWD) | Balance -= 5 (reserved) |
| User scans code (completes transaction) | No change (already reserved) |
| Staff cancels transaction | Balance += 5 (refunded) |
| Code expires (user scan or background job) | Balance += 5 (refunded) |
| Admin records payment of 50 KWD | Balance += 50 |
| Admin sets credit limit to 20 KWD | No balance change, limit enforced on next transaction |

---

## 5. Example Scenario

1. Admin sets credit limit to **20 KWD** on ChargingPoint #45
2. Provider balance starts at **0**
3. Staff creates transaction (commission 8 KWD) → balance = **-8** ✅
4. Staff creates transaction (commission 7 KWD) → balance = **-15** ✅
5. Staff creates transaction (commission 6 KWD) → **BLOCKED** (would be -21, exceeds -20 limit)
   - Error: `"Provider credit limit reached. Available credit: 5.000 KWD"`
6. Provider pays 30 KWD → balance = **+15**
7. Staff creates transaction (commission 6 KWD) → balance = **+9** ✅

---

## 6. Error Messages Summary

| Error Key | Message | When |
|-----------|---------|------|
| `CreditLimit` | "Provider credit limit reached. Available credit: X.XXX {currency}" | Commission would exceed limit |
| `Amount` | "Payment amount must be greater than zero" | Recording payment with amount <= 0 |
| `CreditLimit` | "Credit limit must be a positive value or null for unlimited" | Setting negative credit limit |
| `ProviderType` | "ProviderType must be 'ChargingPoint' or 'ServiceProvider'" | Invalid provider type |
