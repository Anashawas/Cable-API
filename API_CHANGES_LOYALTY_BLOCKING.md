# API Changes - Loyalty System Enhancements

**Date:** 2026-02-26
**Base URL:** `/api/loyalty`

---

## 1. Updated Endpoints

### 1.1 GET `/api/loyalty/GetMyLoyaltyAccount`

**What changed:** 3 new fields added to the response.

**Response (LoyaltyAccountDto):**

| Field | Type | New? | Description |
|-------|------|------|-------------|
| totalPointsEarned | int | | Total points earned all-time |
| totalPointsRedeemed | int | | Total points redeemed all-time |
| currentBalance | int | | Current available points balance |
| currentTierName | string? | | Current tier name (Bronze, Silver, Gold, Platinum) |
| currentMultiplier | double? | | Current tier multiplier |
| seasonPointsEarned | int? | | Points earned in current season |
| seasonName | string? | | Current season name |
| **isBlocked** | **bool** | **NEW** | Whether user is blocked from loyalty system |
| **blockedUntil** | **datetime?** | **NEW** | Block expiry date (null = permanent) |
| **blockReason** | **string?** | **NEW** | Reason for blocking |

**Flutter action items:**
- Display a blocked banner/message when `isBlocked == true`
- If `blockedUntil` is not null, show "Blocked until {date}"
- If `blockedUntil` is null and `isBlocked` is true, show "Your loyalty account is blocked"
- Show `blockReason` if available
- When blocked, disable point earning/spending UI elements

---

### 1.2 GET `/api/loyalty/GetMyPointsHistory`

**What changed:**
- New `providerName` field in response
- New `transactionType` query parameter for filtering

**Request query parameters:**

| Parameter | Type | New? | Description |
|-----------|------|------|-------------|
| seasonId | int? | | Filter by season ID |
| **transactionType** | **int?** | **NEW** | Filter by type: `1` = Earn, `2` = Redeem, `3` = Expired, `4` = AdminAdjust, `5` = SeasonBonus |
| page | int | | Page number (default: 1) |
| pageSize | int | | Page size (default: 20) |

**Response (PointsHistoryDto):**

| Field | Type | New? | Description |
|-------|------|------|-------------|
| id | int | | Transaction ID |
| transactionType | int | | 1=Earn, 2=Redeem, 3=Expired, 4=AdminAdjust, 5=SeasonBonus |
| points | int | | Points amount (positive=earned, negative=deducted) |
| balanceAfter | int | | Balance after this transaction |
| referenceType | string? | | "ChargingPoint" or "ServiceProvider" |
| referenceId | int? | | ID of the provider |
| note | string? | | Transaction note |
| actionName | string? | | Loyalty action name (e.g. "Partner Scan", "Offer Purchase") |
| **providerName** | **string?** | **NEW** | Provider name (resolved from referenceType + referenceId) |
| createdAt | datetime | | Transaction timestamp |

**Flutter action items:**
- Display `providerName` in the transaction history list (e.g. "Al-Salam Charging Station")
- Add filter tabs or dropdown: "All", "Earned", "Spent" using `transactionType` parameter
  - "Earned" = `transactionType=1`
  - "Spent" = `transactionType=2`
  - "All" = omit the parameter

---

## 2. New Admin Endpoints

> These endpoints require admin authorization.

### 2.1 POST `/api/loyalty/admin/BlockUser`

**Purpose:** Admin blocks a user from earning/spending loyalty points.

**Request body:**

```json
{
  "userId": 123,
  "reason": "Suspicious activity detected",
  "blockUntil": "2026-06-01T00:00:00Z"  // optional, null = permanent
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| userId | int | Yes | User ID to block |
| reason | string | Yes | Reason for blocking |
| blockUntil | datetime? | No | Block expiry date. Null = permanent block |

**Response:** `200 OK` (no body)

**Error responses:**
- `404` - User not found
- `400` - User is already blocked

---

### 2.2 POST `/api/loyalty/admin/UnblockUser/{userId}`

**Purpose:** Admin unblocks a user from the loyalty system.

**Route parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| userId | int | User ID to unblock |

**Response:** `200 OK` (no body)

**Error responses:**
- `404` - User not found or no loyalty account
- `400` - User is not currently blocked

---

### 2.3 POST `/api/loyalty/admin/BlockProvider`

**Purpose:** Admin blocks a provider (ChargingPoint or ServiceProvider) from creating new loyalty transactions. Existing pending codes can still be scanned.

**Request body:**

```json
{
  "providerType": "ChargingPoint",
  "providerId": 45,
  "reason": "Under investigation",
  "blockUntil": "2026-04-01T00:00:00Z"  // optional, null = permanent
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| providerType | string | Yes | `"ChargingPoint"` or `"ServiceProvider"` |
| providerId | int | Yes | Provider ID to block |
| reason | string | Yes | Reason for blocking |
| blockUntil | datetime? | No | Block expiry date. Null = permanent block |

**Response:** `200 OK` (no body)

**Error responses:**
- `404` - Provider not found
- `400` - Provider is already blocked
- `400` - Invalid ProviderType (must be "ChargingPoint" or "ServiceProvider")

---

### 2.4 POST `/api/loyalty/admin/UnblockProvider/{providerType}/{providerId}`

**Purpose:** Admin unblocks a provider from the loyalty system.

**Route parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| providerType | string | `"ChargingPoint"` or `"ServiceProvider"` |
| providerId | int | Provider ID to unblock |

**Response:** `200 OK` (no body)

**Error responses:**
- `404` - Provider not found
- `400` - Provider is not currently blocked
- `400` - Invalid ProviderType

---

## 3. Behavioral Changes (No API change, but affects existing flows)

### 3.1 Blocked User Behavior

When a user's loyalty account is blocked (`isBlocked == true`):
- **Earn points** - Will fail with error: "Your loyalty account is currently blocked. Please contact support."
- **Spend points (offers/rewards)** - Will fail with same error
- **View balance/history** - Still works normally
- **Temporary blocks** - Auto-unblock after `blockedUntil` date passes (background job runs every 4 hours)

### 3.2 Blocked Provider Behavior

When a provider is blocked from loyalty:
- **Initiate new partner transaction** at blocked provider - Will fail with: "This provider is currently blocked from the loyalty system"
- **Initiate new offer transaction** at blocked provider - Will fail with same error
- **Scan existing pending codes** at blocked provider - Still works normally (no disruption to in-progress transactions)
- **Temporary blocks** - Auto-unblock after `blockedUntil` date passes (background job runs every 4 hours)

**Flutter action items:**
- Handle new error messages from partner/offer transaction initiation
- Display appropriate user-facing message when a provider is blocked

---

## 4. Error Messages Summary

| Error Message | When |
|---------------|------|
| "Your loyalty account is currently blocked. Please contact support." | User tries to earn/spend while blocked |
| "This provider is currently blocked from the loyalty system" | Staff tries to create partner/offer transaction at blocked provider |
| "This provider is already blocked from the loyalty system" | Admin tries to block already-blocked provider |
| "This provider is not currently blocked from the loyalty system" | Admin tries to unblock non-blocked provider |
| "User is already blocked from the loyalty system" | Admin tries to block already-blocked user |
| "User is not currently blocked from the loyalty system" | Admin tries to unblock non-blocked user |

---

## 5. TransactionType Enum Reference

| Value | Name | Description |
|-------|------|-------------|
| 1 | Earn | Points earned (partner scan, offer, etc.) |
| 2 | Redeem | Points spent (reward redemption) |
| 3 | Expired | Points expired |
| 4 | AdminAdjust | Admin manual adjustment |
| 5 | SeasonBonus | Season-end tier bonus |
