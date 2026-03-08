# API Changes - Single-Device Login, Transaction Polling & GetAllUsers Enhancement

**Date:** 2026-03-04

---

## 1. Single-Device Login Enforcement

### Overview

Users are now limited to **one active session at a time**. When a user logs in on a new device, all previous sessions are automatically invalidated — the old device gets a `401` on the next API call.

Uses a **SecurityStamp** pattern (same approach as ASP.NET Identity): a GUID stored on the user record, embedded as a JWT claim, and validated on every authenticated request.

### How It Works

1. User logs in on **Device A** → new SecurityStamp generated, embedded in JWT
2. User logs in on **Device B** → SecurityStamp rotated, new JWT issued
3. **Device A** makes any API call → middleware detects stamp mismatch → returns `401`
4. **Device A** tries to refresh token → stamp validated → returns `401`

### Affected Login Endpoints (All rotate SecurityStamp)

| Endpoint | Description |
|----------|-------------|
| `POST /api/users/authenticate` | Email/password login |
| `POST /api/users/login-by-google` | Google OAuth login |
| `POST /api/users/verify-otp` | Phone OTP login |
| `POST /api/provider/authenticate` | Provider email/password login |
| `POST /api/provider/verify-otp` | Provider 2FA OTP |

**Exception:** `POST /api/users/login-by-token` does **NOT** rotate the stamp — it re-validates the existing session, so it won't kick out the current device.

### New Endpoints

#### 1.1 POST `/api/users/logout`

**Purpose:** Logs out the user by invalidating all active sessions (rotates SecurityStamp).

**Authorization:** Required

**Request body:** None

**Response:** `200 OK` (no body)

**Effect:** All existing access tokens and refresh tokens for this user become invalid immediately.

---

#### 1.2 POST `/api/provider/logout`

**Purpose:** Same as above, for provider users.

**Authorization:** Required

**Request body:** None

**Response:** `200 OK` (no body)

---

### Error Response (Session Expired)

When a user's session is invalidated (logged in on another device), all API calls return:

**Status:** `401 Unauthorized`

```json
{
  "title": "Not Authorized Access",
  "status": 401,
  "detail": "Session expired. You have been logged in on another device."
}
```

**Flutter action items:**
- Handle `401` with message containing "logged in on another device"
- Show user-friendly message: "Your session has expired because you logged in on another device"
- Redirect to login screen
- Same handling applies when `POST /api/users/refresh-access` returns `401`

### Backward Compatibility

- Old tokens issued before this update (without SecurityStamp claim) will continue to work until they naturally expire
- After the user's first login post-update, single-device enforcement activates

---

## 2. Transaction ID in CreateTransaction Response

### Overview

`POST /api/partners/provider/CreateTransaction` now returns the transaction `id` in the response, enabling the provider app to **poll for operation completion**.

### Modified Endpoint

#### POST `/api/partners/provider/CreateTransaction`

**Response (updated):**

```json
{
  "id": 123,
  "transactionCode": "PTR-AB3K9X",
  "expiresAt": "2026-03-04T11:00:00Z",
  "commissionAmount": 5.000,
  "pointsToBeAwarded": 3,
  "transactionAmount": 50.000
}
```

| Field | Type | Description | **New?** |
|-------|------|-------------|----------|
| **id** | **int** | **Transaction database ID** | **✅ NEW** |
| transactionCode | string | QR code value (PTR-XXXXXX) | Existing |
| expiresAt | datetime | When the code expires | Existing |
| commissionAmount | decimal | Cable's commission | Existing |
| pointsToBeAwarded | int | Points user will receive | Existing |
| transactionAmount | decimal | Original amount | Existing |

### Polling Flow for Provider App

```
1. POST /api/partners/provider/CreateTransaction
   → Response: { id: 123, transactionCode: "PTR-AB3K9X", ... }

2. Show QR code to user (transactionCode)

3. Poll every 3 seconds:
   GET /api/partners/provider/GetTransactionById/123
   → Check response.status:
      - 1 (Initiated) → keep polling
      - 2 (Completed) → ✅ user scanned, show success
      - 3 (Expired)   → code expired, show error
      - 4 (Cancelled) → cancelled, show error

4. Stop polling when status != 1 OR when expiresAt is reached
```

### Existing Endpoint for Polling

#### GET `/api/partners/provider/GetTransactionById/{id}`

**Response:**

```json
{
  "id": 123,
  "userId": 45,
  "userName": "Ahmed",
  "transactionCode": "PTR-AB3K9X",
  "status": 2,
  "transactionAmount": 50.000,
  "currencyCode": "KWD",
  "commissionAmount": 5.000,
  "pointsAwarded": 3,
  "codeExpiresAt": "2026-03-04T11:00:00Z",
  "completedAt": "2026-03-04T10:45:30Z",
  "createdAt": "2026-03-04T10:30:00Z"
}
```

| Status | Meaning |
|--------|---------|
| 1 | Initiated — waiting for user to scan |
| 2 | Completed — user scanned successfully |
| 3 | Expired — code expired before scan |
| 4 | Cancelled — provider cancelled |

**Flutter action items:**
- Store the `id` from CreateTransaction response
- Implement 3-second polling using `GetTransactionById/{id}`
- Show loading/waiting screen with QR code while status is `1`
- On status `2`: show success screen with user name and points awarded
- On status `3` or `4`: show appropriate error and stop polling
- Stop polling when `codeExpiresAt` is reached (fallback timeout)

---

## 3. GetAllUsers Enhancement

### Overview

`GET /api/users/GetAllUsers` now includes `createdAt` (user registration date) and `userCars` (list of user's registered vehicles with car type, model, plug type, and registration date).

### Modified Endpoint

#### GET `/api/users/GetAllUsers`

**Response (updated):**

```json
[
  {
    "id": 1,
    "name": "Ahmed",
    "phone": "+96512345678",
    "userName": "Ahmed",
    "email": "ahmed@example.com",
    "isPhoneVerified": true,
    "createdAt": "2025-06-15T10:30:00Z",
    "role": {
      "id": 1,
      "name": "User"
    },
    "userCars": [
      {
        "id": 5,
        "carTypeName": "Tesla",
        "carModelName": "Model 3",
        "plugTypeName": "Type 2",
        "createdAt": "2025-07-01T08:00:00Z"
      },
      {
        "id": 8,
        "carTypeName": "BMW",
        "carModelName": "iX3",
        "plugTypeName": "CCS2",
        "createdAt": "2025-08-15T14:20:00Z"
      }
    ]
  }
]
```

### New Fields

| Field | Type | Description | **New?** |
|-------|------|-------------|----------|
| **createdAt** | **datetime** | **User registration date** | **✅ NEW** |
| **userCars** | **array** | **List of user's registered vehicles** | **✅ NEW** |
| **userCars[].id** | **int** | **UserCar record ID** | **✅ NEW** |
| **userCars[].carTypeName** | **string** | **Car brand/type (e.g., "Tesla")** | **✅ NEW** |
| **userCars[].carModelName** | **string** | **Car model (e.g., "Model 3")** | **✅ NEW** |
| **userCars[].plugTypeName** | **string?** | **Plug type (e.g., "Type 2")** | **✅ NEW** |
| **userCars[].createdAt** | **datetime** | **When the car was registered** | **✅ NEW** |

### Existing Fields (unchanged)

| Field | Type | Description |
|-------|------|-------------|
| id | int | User ID |
| name | string? | User name |
| phone | string? | Phone number |
| userName | string? | User name (duplicate of name) |
| email | string? | Email address |
| isPhoneVerified | bool | Phone verification status |
| role | object | Role with id and name |

**Flutter action items:**
- Update user list UI to display `createdAt` date
- Show user's cars in the user detail/list view
- Users with no cars will have an empty `userCars: []` array

---

## Database Changes

### New Column

```sql
ALTER TABLE dbo.UserAccount ADD SecurityStamp NVARCHAR(50) NULL;
```

**Already executed** — no migration needed.

---

## Summary of All Changes

| Change | Type | Endpoint |
|--------|------|----------|
| Single-device login enforcement | Security | All login endpoints |
| User logout | New endpoint | `POST /api/users/logout` |
| Provider logout | New endpoint | `POST /api/provider/logout` |
| Transaction ID in create response | Modified response | `POST /api/partners/provider/CreateTransaction` |
| CreatedAt + UserCars in GetAllUsers | Modified response | `GET /api/users/GetAllUsers` |
