# Partner Transactions - Endpoints & Workflow Guide

> Base URL: `/api/partners`
> All amounts are pre-calculated at transaction creation time (snapshot values).
> Transaction codes follow the format: `PTR-XXXXXX`

---

## How It Works (Real-World Flow)

```
 ADMIN                    PROVIDER STAFF               USER (Mobile App)
  |                            |                            |
  |  1. Create Partnership     |                            |
  |  (set commission %,        |                            |
  |   points %, expiry)        |                            |
  |--------------------------->|                            |
  |                            |                            |
  |                     2. Customer pays at                 |
  |                        provider location                |
  |                            |                            |
  |                     3. Staff opens app,                 |
  |                        enters total amount,             |
  |                        gets QR code (PTR-XXXXXX)        |
  |                            |                            |
  |                     4. Staff shows QR to customer       |
  |                            |          ----------------->|
  |                            |                            |
  |                            |          5. User opens app |
  |                            |             scans QR code  |
  |                            |                            |
  |                            |          6. System auto:   |
  |                            |             - Completes TX |
  |                            |             - Awards points|
  |                            |             - Shows result |
  |                            |                            |
```

### Step-by-Step:

1. **Admin creates a partnership** with a ChargingPoint or ServiceProvider. Sets commission %, points reward %, and QR code expiry time.

2. **Customer visits the partner location** and makes a purchase/payment (pays the provider directly - outside the app).

3. **Provider staff opens the partner app**, enters the transaction total amount and currency, and taps "Generate Code". The system creates a `PTR-XXXXXX` code and calculates everything upfront:
   - Commission amount (Cable's cut)
   - Points eligible amount
   - Points to be awarded to the user

4. **Staff shows the QR code** (containing `PTR-XXXXXX`) to the customer.

5. **User opens Cable app**, goes to the QR scanner screen, and scans the code.

6. **System automatically**:
   - Finds the transaction by code
   - Checks if code has expired
   - Assigns the user to the transaction
   - Marks it as Completed
   - Awards loyalty points to the user
   - Returns a summary (provider name, amount, commission, points earned)

---

## Transaction Status Values

| Status | Value | Meaning |
|--------|-------|---------|
| Initiated | 1 | Provider created the code, waiting for user to scan |
| Completed | 2 | User scanned the code, points awarded |
| Expired | 3 | Code expired before user scanned it |
| Cancelled | 4 | Provider cancelled the code before user scanned it |

---

## Endpoints Overview

| # | Method | Path | Who | Auth | Description |
|---|--------|------|-----|------|-------------|
| 1 | GET | `/GetActivePartners` | User | No | List all active partner locations |
| 2 | GET | `/GetPartnerById/{id}` | User | No | Get partner details |
| 3 | POST | `/ScanPartnerCode` | User | Yes | Scan QR code to earn points |
| 4 | GET | `/GetMyTransactions` | User | Yes | My transaction history |
| 5 | GET | `/GetMyTransactionById/{id}` | User | Yes | Get single transaction details |
| 6 | POST | `/provider/CreateTransaction` | Provider | Yes | Generate QR code with amount |
| 7 | POST | `/provider/CancelTransaction/{id}` | Provider | Yes | Cancel before user scans |
| 8 | GET | `/provider/GetProviderTransactions` | Provider | Yes | View their transactions |
| 9 | GET | `/provider/GetTransactionById/{id}` | Provider | Yes | Get single transaction details |
| 10 | GET | `/provider/GetMyAgreement` | Provider | Yes | View their partnership details |
| 11 | POST | `/admin/CreatePartnerAgreement` | Admin | Yes | Register a provider as partner |
| 12 | PUT | `/admin/UpdatePartnerAgreement/{id}` | Admin | Yes | Update partnership settings |
| 13 | PUT | `/admin/DeactivatePartnerAgreement/{id}` | Admin | Yes | Deactivate a partnership |
| 14 | GET | `/admin/GetAllPartnerAgreements` | Admin | Yes | List all partnerships |

---

## User Endpoints

### 1. Get Active Partners

Browse all active partner locations. No login needed.

```
GET /api/partners/GetActivePartners?providerType=ChargingPoint
```

**Query Parameters:**
| Param | Type | Required | Description |
|-------|------|----------|-------------|
| providerType | string | No | Filter by `"ChargingPoint"` or `"ServiceProvider"` |

**Response:** `200 OK`
```json
[
  {
    "id": 1,
    "providerType": "ChargingPoint",
    "providerId": 5,
    "providerName": "Station Alpha",
    "commissionPercentage": 10.0,
    "pointsRewardPercentage": 5.0,
    "codeExpiryMinutes": 30,
    "note": "Weekend partner"
  }
]
```

---

### 2. Get Partner By Id

Get full details of a specific partnership. No login needed.

```
GET /api/partners/GetPartnerById/1
```

**Route Parameters:**
| Param | Type | Required | Description |
|-------|------|----------|-------------|
| id | int | Yes | The partner agreement ID |

**Response:** `200 OK`
```json
{
  "id": 1,
  "providerType": "ChargingPoint",
  "providerId": 5,
  "providerName": "Station Alpha",
  "commissionPercentage": 10.0,
  "pointsRewardPercentage": 5.0,
  "pointsConversionRateId": 2,
  "conversionRateName": "Standard Rate",
  "pointsPerUnit": 1.5,
  "codeExpiryMinutes": 30,
  "isActive": true,
  "note": "Weekend partner",
  "createdAt": "2025-01-15T10:00:00Z"
}
```

**Errors:** `404` if not found.

---

### 3. Scan Partner Code (QR Scan)

User scans the QR code shown by the provider staff. This is the main action - it completes the transaction and awards loyalty points.

```
POST /api/partners/ScanPartnerCode?code=PTR-AB3K9X
```

**Requires:** Authorization (JWT token)

**Query Parameters:**
| Param | Type | Required | Description |
|-------|------|----------|-------------|
| code | string | Yes | The `PTR-XXXXXX` code from the QR |

**What happens internally:**
1. Finds the transaction by code (must be `Initiated` status)
2. Checks if the code has expired -> if expired, marks as `Expired` and returns error
3. Sets `UserId` to the current user
4. Marks transaction as `Completed`
5. Awards loyalty points (if > 0) via the loyalty system
6. Returns a summary with provider name, amounts, and points

**Response:** `200 OK`
```json
{
  "providerName": "Station Alpha",
  "transactionAmount": 50.00,
  "currencyCode": "KWD",
  "commissionAmount": 5.00,
  "pointsAwarded": 37
}
```

**Errors:**
- `401` Unauthorized (not logged in)
- `404` Code not found or already used
- `422` Code has expired

---

### 4. Get My Transactions

View the current user's partner transaction history. Only shows transactions where the user scanned the code.

```
GET /api/partners/GetMyTransactions?status=2
```

**Requires:** Authorization (JWT token)

**Query Parameters:**
| Param | Type | Required | Description |
|-------|------|----------|-------------|
| status | int | No | Filter by status: `1`=Initiated, `2`=Completed, `3`=Expired, `4`=Cancelled |

**Response:** `200 OK`
```json
[
  {
    "id": 10,
    "partnerAgreementId": 1,
    "providerName": "Station Alpha",
    "transactionCode": "PTR-AB3K9X",
    "status": 2,
    "providerType": "ChargingPoint",
    "providerId": 5,
    "transactionAmount": 50.00,
    "currencyCode": "KWD",
    "commissionPercentage": 10.0,
    "commissionAmount": 5.00,
    "pointsAwarded": 37,
    "codeExpiresAt": "2025-01-15T10:30:00Z",
    "completedAt": "2025-01-15T10:05:00Z",
    "createdAt": "2025-01-15T10:00:00Z"
  }
]
```

---

### 5. Get My Transaction By Id

Get a single partner transaction by its ID. Only returns transactions belonging to the current user.

```
GET /api/partners/GetMyTransactionById/10
```

**Requires:** Authorization (JWT token)

**Route Parameters:**
| Param | Type | Required | Description |
|-------|------|----------|-------------|
| id | int | Yes | The partner transaction ID |

**Response:** `200 OK`
```json
{
  "id": 10,
  "partnerAgreementId": 1,
  "providerName": "Station Alpha",
  "transactionCode": "PTR-AB3K9X",
  "status": 2,
  "providerType": "ChargingPoint",
  "providerId": 5,
  "transactionAmount": 50.00,
  "currencyCode": "KWD",
  "commissionPercentage": 10.0,
  "commissionAmount": 5.00,
  "pointsAwarded": 37,
  "codeExpiresAt": "2025-01-15T10:30:00Z",
  "completedAt": "2025-01-15T10:05:00Z",
  "createdAt": "2025-01-15T10:00:00Z"
}
```

**Errors:**
- `401` Unauthorized (not logged in)
- `404` Transaction not found or doesn't belong to this user

---

## Provider Endpoints

### 6. Create Transaction (Generate QR Code)

Provider staff creates a new transaction by entering the purchase amount. The system generates a `PTR-XXXXXX` code that the staff shows to the customer as a QR code.

```
POST /api/partners/provider/CreateTransaction
```

**Requires:** Authorization (JWT token)

**Request Body:**
```json
{
  "partnerAgreementId": 1,
  "transactionAmount": 50.00,
  "currencyCode": "KWD"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| partnerAgreementId | int | Yes | The partnership agreement ID |
| transactionAmount | decimal | Yes | The total amount the customer paid |
| currencyCode | string | Yes | Currency code (e.g. `"KWD"`, `"USD"`) |

**What happens internally:**
1. Loads the partner agreement and its conversion rate
2. Calculates:
   - `commissionAmount` = transactionAmount x (commissionPercentage / 100)
   - `pointsEligibleAmount` = transactionAmount x (pointsRewardPercentage / 100)
   - `pointsToBeAwarded` = floor(pointsEligibleAmount x conversionRate)
3. Generates a unique `PTR-XXXXXX` code
4. Sets `codeExpiresAt` based on agreement's `codeExpiryMinutes`
5. Stores `ConfirmedByUserId` = the staff member who created it
6. Does NOT set `UserId` yet (set when user scans)

**Calculation Example:**
```
Transaction Amount:          50.00 KWD
Commission %:                10%
Points Reward %:             5%
Conversion Rate:             1.5 points per unit

Commission Amount:           50.00 x 10/100 = 5.00 KWD
Points Eligible Amount:      50.00 x 5/100  = 2.50 KWD
Points Awarded:              floor(2.50 x 1.5) = 3 points
```

**Response:** `200 OK`
```json
{
  "transactionCode": "PTR-AB3K9X",
  "expiresAt": "2025-01-15T10:30:00Z",
  "commissionAmount": 5.00,
  "pointsToBeAwarded": 3,
  "transactionAmount": 50.00
}
```

**Errors:**
- `401` Unauthorized
- `404` Agreement not found or not active

---

### 7. Cancel Transaction

Provider staff cancels a transaction before the user scans it. Only the staff member who created it can cancel.

```
POST /api/partners/provider/CancelTransaction/10
```

**Requires:** Authorization (JWT token)

**Route Parameters:**
| Param | Type | Required | Description |
|-------|------|----------|-------------|
| id | int | Yes | The transaction ID to cancel |

**Rules:**
- Only works on `Initiated` (status = 1) transactions
- Only the staff who created it (`ConfirmedByUserId`) can cancel
- Once a user scans it (status = `Completed`), it cannot be cancelled

**Response:** `200 OK` (no body)

**Errors:**
- `401` Unauthorized
- `404` Transaction not found, already completed/cancelled, or not owned by this staff

---

### 8. Get Provider Transactions

View all transactions for a specific provider location. Can filter by month/year.

```
GET /api/partners/provider/GetProviderTransactions?providerType=ChargingPoint&providerId=5&month=1&year=2025
```

**Requires:** Authorization (JWT token)

**Query Parameters:**
| Param | Type | Required | Description |
|-------|------|----------|-------------|
| providerType | string | Yes | `"ChargingPoint"` or `"ServiceProvider"` |
| providerId | int | Yes | The provider entity ID |
| month | int | No | Filter by month (1-12) |
| year | int | No | Filter by year |

**Response:** `200 OK`
```json
[
  {
    "id": 10,
    "userId": 42,
    "userName": "Ahmed Ali",
    "transactionCode": "PTR-AB3K9X",
    "status": 2,
    "transactionAmount": 50.00,
    "currencyCode": "KWD",
    "commissionAmount": 5.00,
    "pointsAwarded": 3,
    "codeExpiresAt": "2025-01-15T10:30:00Z",
    "completedAt": "2025-01-15T10:05:00Z",
    "createdAt": "2025-01-15T10:00:00Z"
  }
]
```

---

### 9. Get Provider Transaction By Id

Get a single partner transaction by its ID. Only returns transactions created by the current provider staff.

```
GET /api/partners/provider/GetTransactionById/10
```

**Requires:** Authorization (JWT token)

**Route Parameters:**
| Param | Type | Required | Description |
|-------|------|----------|-------------|
| id | int | Yes | The partner transaction ID |

**Response:** `200 OK`
```json
{
  "id": 10,
  "userId": 42,
  "userName": "Ahmed Ali",
  "transactionCode": "PTR-AB3K9X",
  "status": 2,
  "transactionAmount": 50.00,
  "currencyCode": "KWD",
  "commissionAmount": 5.00,
  "pointsAwarded": 3,
  "codeExpiresAt": "2025-01-15T10:30:00Z",
  "completedAt": "2025-01-15T10:05:00Z",
  "createdAt": "2025-01-15T10:00:00Z"
}
```

**Errors:**
- `401` Unauthorized
- `404` Transaction not found or not created by this staff member

---

### 10. Get My Agreement

Provider views their own partnership agreement details.

```
GET /api/partners/provider/GetMyAgreement?providerType=ChargingPoint&providerId=5
```

**Requires:** Authorization (JWT token)

**Query Parameters:**
| Param | Type | Required | Description |
|-------|------|----------|-------------|
| providerType | string | Yes | `"ChargingPoint"` or `"ServiceProvider"` |
| providerId | int | Yes | The provider entity ID |

**Response:** `200 OK`
```json
{
  "id": 1,
  "commissionPercentage": 10.0,
  "pointsRewardPercentage": 5.0,
  "conversionRateName": "Standard Rate",
  "pointsPerUnit": 1.5,
  "codeExpiryMinutes": 30,
  "isActive": true,
  "note": "Weekend partner",
  "createdAt": "2025-01-15T10:00:00Z"
}
```

**Errors:** `404` if no active agreement found.

---

## Admin Endpoints

### 11. Create Partner Agreement

Register a provider (ChargingPoint or ServiceProvider) as a Cable partner.

```
POST /api/partners/admin/CreatePartnerAgreement
```

**Requires:** Authorization (JWT token)

**Request Body:**
```json
{
  "providerType": "ChargingPoint",
  "providerId": 5,
  "commissionPercentage": 10.0,
  "pointsRewardPercentage": 5.0,
  "pointsConversionRateId": 2,
  "codeExpiryMinutes": 30,
  "note": "Weekend partner"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| providerType | string | Yes | `"ChargingPoint"` or `"ServiceProvider"` |
| providerId | int | Yes | The provider entity ID |
| commissionPercentage | double | Yes | Cable's commission (0-100%) |
| pointsRewardPercentage | double | Yes | % of amount eligible for points (0-100%) |
| pointsConversionRateId | int | No | Specific conversion rate. If null, uses default |
| codeExpiryMinutes | int | Yes | How long QR codes stay valid |
| note | string | No | Admin note |

**Validation Rules:**
- `providerType` must be `"ChargingPoint"` or `"ServiceProvider"`
- `providerId` must be > 0
- `commissionPercentage` must be > 0 and <= 100
- `pointsRewardPercentage` must be >= 0 and <= 100
- `codeExpiryMinutes` must be > 0
- Cannot create a duplicate active agreement for the same provider

**Response:** `200 OK` - Returns the new agreement ID (int)

**Errors:**
- `401` Unauthorized
- `403` Forbidden (not admin)
- `422` Validation error or duplicate active agreement

---

### 12. Update Partner Agreement

Update the settings of an existing partnership.

```
PUT /api/partners/admin/UpdatePartnerAgreement/1
```

**Requires:** Authorization (JWT token)

**Route Parameters:**
| Param | Type | Required | Description |
|-------|------|----------|-------------|
| id | int | Yes | The partner agreement ID |

**Request Body:**
```json
{
  "commissionPercentage": 12.0,
  "pointsRewardPercentage": 8.0,
  "pointsConversionRateId": 3,
  "codeExpiryMinutes": 45,
  "note": "Updated rates",
  "isActive": true
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| commissionPercentage | double | Yes | New commission % |
| pointsRewardPercentage | double | Yes | New points reward % |
| pointsConversionRateId | int | No | New conversion rate ID |
| codeExpiryMinutes | int | Yes | New expiry time |
| note | string | No | Updated note |
| isActive | bool | Yes | Active/inactive toggle |

**Important:** Updating rates only affects NEW transactions. Already-created transactions keep their original snapshot values.

**Response:** `200 OK` (no body)

**Errors:**
- `401` Unauthorized
- `403` Forbidden
- `404` Agreement not found

---

### 13. Deactivate Partner Agreement

Quick deactivation - sets `isActive = false`. Simpler than a full update when you just need to turn off a partnership.

```
PUT /api/partners/admin/DeactivatePartnerAgreement/1
```

**Requires:** Authorization (JWT token)

**Route Parameters:**
| Param | Type | Required | Description |
|-------|------|----------|-------------|
| id | int | Yes | The partner agreement ID |

**Response:** `200 OK` (no body)

**Errors:**
- `401` Unauthorized
- `403` Forbidden
- `404` Agreement not found

---

### 14. Get All Partner Agreements

List all partnerships (for admin dashboard). Can filter by active status.

```
GET /api/partners/admin/GetAllPartnerAgreements?isActive=true
```

**Requires:** Authorization (JWT token)

**Query Parameters:**
| Param | Type | Required | Description |
|-------|------|----------|-------------|
| isActive | bool | No | Filter: `true` = active only, `false` = inactive only, omit = all |

**Response:** `200 OK`
```json
[
  {
    "id": 1,
    "providerType": "ChargingPoint",
    "providerId": 5,
    "providerName": "Station Alpha",
    "commissionPercentage": 10.0,
    "pointsRewardPercentage": 5.0,
    "pointsConversionRateId": 2,
    "conversionRateName": "Standard Rate",
    "codeExpiryMinutes": 30,
    "isActive": true,
    "note": "Weekend partner",
    "createdAt": "2025-01-15T10:00:00Z"
  }
]
```

---

## Settlement Integration

Partner transactions are automatically included in the monthly settlement process alongside offer transactions.

```
Monthly Settlement:
  For each provider that has completed partner transactions:
    - Total Commission = SUM of all commissionAmount for Completed transactions
    - Total Transactions = COUNT of Completed transactions
    - Combined with offer transaction settlements (if any) into one settlement per provider per month
```

---

## Business Rules Summary

1. **One active agreement per provider** - Cannot have two active partnerships for the same ChargingPoint/ServiceProvider
2. **Snapshot values** - Commission %, points %, conversion rate are locked at transaction creation time. Admin changes only affect future transactions
3. **Code expiry** - Each code has a TTL set by the agreement's `codeExpiryMinutes`. Expired codes return an error and the transaction is auto-marked as `Expired`
4. **Only Initiated codes can be scanned** - Completed, Expired, and Cancelled codes cannot be scanned
5. **Only the creating staff can cancel** - The `ConfirmedByUserId` (staff who generated the code) must match the current user
6. **Points are awarded instantly** - As soon as the user scans, points go to their loyalty balance
7. **No user ID at creation** - When provider creates the transaction, `UserId` is null. It's set only when a user scans the code
