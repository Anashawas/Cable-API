# Loyalty Admin — Phases 1–3 Response (Backend → Admin Portal)

**In response to:** `LOYALTY_ADMIN_BE_REQUIREMENTS.md` (2026-07-05)
**Date:** 2026-07-07
**Status:** Phase 1 (P0 + G), Phase 2 (P1) **and Phase 3 (P2)** implemented on **DEV**. All admin loyalty endpoints — new and pre-existing — are now hard-gated to the Admin role.

---

## What's implemented (P0 + G)

All new endpoints are under `/api/loyalty/admin/...`, require a Bearer token from
an **Admin-role user** (Role id 2 — others get `403`), are **not owner-gated**,
and follow your cross-cutting rules: paged lists return
`{ items, totalCount, page, pageSize }`, `from`/`to` are optional ISO-8601,
rows carry `userId` + `userName`, and admin-initiated rows carry
`performedByUserId` + `performedByUserName`. Empty result = empty page, not 404.

### A1 ✅ `GET /api/loyalty/admin/GetUserLoyaltyAccount/{userId}`

Returns the existing `LoyaltyAccountDto` for **any** user:
`totalPointsEarned`, `totalPointsRedeemed`, `currentBalance`, `currentTierName`,
`currentMultiplier`, `seasonPointsEarned`, `seasonName`, `isBlocked`,
`blockedUntil`, `blockReason`.
`404` only when the **user id** doesn't exist; a user with no loyalty account yet
returns the zero/Bronze snapshot (same as the self endpoint).

### A2 ✅ `GET /api/loyalty/admin/GetUserPointsHistory/{userId}`

Query: `transactionType?`, `seasonId?`, `from?`, `to?`, `page` (default 1), `pageSize` (default 20, max 200).

```json
{
  "items": [
    {
      "id": 9001,
      "userId": 45,
      "userName": "Ali",
      "transactionType": 4,
      "points": 5000,
      "balanceAfter": 5000,
      "referenceType": null,
      "referenceId": null,
      "note": "Welcome bonus",
      "actionName": null,
      "providerName": null,
      "performedByUserId": 2,
      "performedByUserName": "Admin",
      "createdAt": "2026-04-04T10:54:22"
    }
  ],
  "totalCount": 137,
  "page": 1,
  "pageSize": 20
}
```

- `performedByUserId/Name` are set whenever the row was created by someone other
  than the account owner (admin adjustments, season bonuses, admin cancellations);
  `null` for the user's own activity.

### C1 ✅ `GET /api/loyalty/admin/GetAllPointsTransactions`

The global ledger. Same row shape and paging as A2.
Query: `transactionType?`, `userId?`, `seasonId?`, `providerType?`
(`ChargingPoint` | `ServiceProvider`), `providerId?`, `from?`, `to?`, `page`, `pageSize`.

### B3 ✅ `GET /api/loyalty/admin/GetTransactionDetail?activityType={Offer|Partner|Redemption}&id={int}`

Full single-transaction view, exactly the proposed shape:

```json
{
  "activityType": "Partner",
  "transactionId": 123,
  "status": 2,
  "statusName": "Completed",
  "user":     { "userId": 45, "userName": "Ali", "phone": "+9627..." },
  "provider": { "providerType": "ServiceProvider", "providerId": 17, "providerName": "Cable Cafe" },
  "code": "PTR-XXXXXX",
  "amount": 3.500,
  "currencyCode": "JOD",
  "commissionAmount": 0.350,
  "points": 35,
  "note": null,
  "performedByUserId": 8,
  "performedByUserName": "Cafe Staff",
  "createdAt": "2026-07-02T14:20:00",
  "completedAt": "2026-07-02T14:21:00"
}
```

Notes on semantics:
- `points` is **signed**: Partner = `+pointsAwarded`, Offer = `-pointsDeducted`, Redemption = `-pointsSpent`.
- `performedBy` = the staff member who initiated/confirmed (Offer/Partner: `confirmedByUser`; Redemption: the admin who fulfilled/cancelled it).
- Redemption rows: `amount`/`commissionAmount` are `null`, `note` carries the reward name, `completedAt` = `fulfilledAt`.
- Unknown `activityType` → `400`; unknown id → `404`.

### G ✅ Route reconciliation — the answer sheet

| Portal calls | Verdict | Correct call |
|---|---|---|
| `GET /admin/GetAllSeasons` | **Was missing — now added** | `GET /api/loyalty/admin/GetAllSeasons` → `[{ id, name, description, startDate, endDate, isActive, createdAt }]` |
| `GET /admin/GetAllRedemptions` | **Was missing — now added (see D1, Phase 2)** | `GET /api/loyalty/admin/GetAllRedemptions` |
| `PUT /admin/EndSeason/{id}` | Wrong verb + shape | **`POST /api/loyalty/admin/EndSeason`** — no id, no body; it always ends the *currently active* season |
| `PUT /admin/FulfillRedemption/{id}` | Wrong verb | **`PATCH /api/loyalty/admin/FulfillRedemption/{id}`** |
| `PUT /admin/CancelRedemption/{id}` | Wrong verb | **`PATCH /api/loyalty/admin/CancelRedemption/{id}`** |

> Note: what you compared against ("production OpenAPI") may also be behind —
> several backend changes are on dev awaiting deploy. The verbs above are the
> source-of-truth from the code.

---

# Phase 2 (P1) — implemented

### B1 ✅ `GET /api/loyalty/admin/GetProviderActivity`

The unified activity feed. Query: `providerType` (`ChargingPoint`|`ServiceProvider`),
`providerId`, `activityType?` (`Offer` | `Partner` | `Redemption` — omit for all),
`from?`, `to?`, `page`, `pageSize`. Returns the standard paged shape; rows:

```json
{
  "activityType": "Partner",
  "transactionId": 123,
  "userId": 45,
  "userName": "Ali",
  "code": "PTR-XXXXXX",
  "status": 2,
  "statusName": "Completed",
  "points": 35,
  "amount": 3.500,
  "currencyCode": "JOD",
  "createdAt": "2026-07-02T14:20:00",
  "completedAt": "2026-07-02T14:21:00"
}
```
`points` signed (earned +, spent −). Rows from all three flows are merged and
sorted by `createdAt` desc; `totalCount` = combined count.

### D1 ✅ `GET /api/loyalty/admin/GetAllRedemptions`

Query: `status?`, `providerType?`, `providerId?`, `userId?`, `from?`, `to?`,
`page`, `pageSize`. Row = `RedemptionDto` + `userId`, `userName`, `providerName`.
Also: `ProviderRedemptionDto` (existing `GetProviderRedemptions`) now includes
**`userId`** as requested.

### I1 ✅ `GET /api/loyalty/admin/GetLoyaltySummary`

The program-health dashboard. Query: `seasonId?`, `from?`, `to?`.

```json
{
  "outstandingLiabilityPoints": 5107,
  "estimatedLiabilityValue": 102.140,
  "liabilityCurrencyCode": "JOD",
  "totalPointsIssued": 5107,
  "totalPointsRedeemed": 0,
  "totalPointsExpired": 0,
  "totalPointsAdminAdjusted": 5000,
  "redemptionRatePct": 0,
  "totalRedemptions": 0,
  "activeMembers": 3,
  "blockedUsers": 0,
  "issuedVsRedeemedDaily": [ { "day": "...", "issued": 1200, "redeemed": 400 } ],
  "topEarners": [ { "userId": 45, "userName": "Ali", "points": 3200 } ]
}
```
Semantics: **liability is always "now"** (sum of all current balances ÷ default
conversion rate) regardless of date filters; the other aggregates honor
`seasonId`/`from`/`to` (all-time when omitted); the daily series defaults to the
last 30 days when no window is given.

### H1 ✅ (read-only) `GET /api/loyalty/admin/GetAllTiers`

`[{ id, name, minPoints, multiplier, bonusPoints, iconUrl, isActive }]`, ordered
by `minPoints`. Tiers remain product-defined seed data — **decision still open**
whether they become business-editable (CRUD would be Phase 3).

All Phase 2 endpoints are **Admin-role gated** (403 for non-admins) like Phase 1.

---

# Phase 3 (P2) — implemented

### A3 ✅ (covered by D1)
Per-user redemptions = `GET /admin/GetAllRedemptions?userId={id}` — no separate
endpoint was added to avoid a duplicate route.

### F1 ✅ `GET /api/loyalty/admin/GetBlockedUsers`
`[{ userId, userName, reason, blockedAt, blockedUntil, blockedByUserId, blockedByUserName }]`
— only *currently* blocked (expired temporary blocks excluded).

### F2 ✅ `GET /api/loyalty/admin/GetBlockedProviders`
`[{ providerType, providerId, providerName, reason, blockedAt, blockedUntil, blockedByUserId }]`
— charging points + service providers combined.

### I2 ✅ `GET /api/loyalty/admin/GetRewardPerformance?from=&to=`
`[{ rewardId, rewardName, isActive, pointsCost, redemptions, cancelledRedemptions, pointsSpent, remainingStock }]`
— `remainingStock` null = unlimited; includes zero-redemption rewards.

### H2 ✅ (visibility) `GET /api/loyalty/admin/GetUpcomingExpiries?from=&to=`
`{ fromUtc, toUtc, totalPointsExpiring, usersAffected, users: [{ userId, userName, pointsExpiring, earliestExpiry }] }`
— default window: next 90 days.
> ⚠️ **Expiry policy note:** today NO earn flow stamps an `ExpiresAt` on points, so
> this returns empty until an expiry policy is configured. The policy itself
> (months-to-expire, GET/PUT config) needs a product decision — tell us the rule
> and we'll wire it into the earn flow.

### K2 ✅ `GET /api/loyalty/admin/GetAdjustmentReasons`
Static catalog: `COMPENSATION, CORRECTION, PROMOTION, CAMPAIGN, FRAUD_CLAWBACK, SUPPORT_GOODWILL, OTHER`.
`POST /admin/AdjustPoints` now accepts an optional **`reasonCode`** — validated
against the catalog and stamped into the ledger note as `[CODE] …`.

### J1 ✅ `POST /api/loyalty/admin/BulkAwardPoints`
```json
{ "carTypeId": null, "carModelId": null, "city": "Amman", "tierId": null, "points": 100, "note": "Eid bonus" }
```
Returns `{ targetedUsers, awardedUsers, skippedBlockedUsers, pointsAwardedTotal }`.
- Same segment targeting as `SendNotificationByFilter` + current-season tier filter.
- **At least one filter is required** (guards against awarding the entire user base).
- Blocked loyalty accounts are skipped (and counted); wallets are created for
  users without one; every award is a row-locked AdminAdjust ledger entry
  recording the admin actor. `points` must be 1–100,000.

### K1 ✅ `POST /api/loyalty/admin/ReverseTransaction`
```json
{ "activityType": "Offer | Partner | Redemption | PointsAdjustment", "transactionId": 123, "reason": "duplicate scan" }
```
Returns `{ reversalTransactionId, pointsDelta, newBalance }`.
- Creates a compensating, **idempotent** ledger entry (a transaction can only be
  reversed once) referencing the reversed transaction — full audit trail with the
  admin actor.
- Rules: Offer/Partner must be **Completed**; **Pending** redemptions →
  use `CancelRedemption`; **Fulfilled** redemptions are cancelled + reward stock
  freed; a reversal entry cannot itself be reversed; reversals never push a
  balance below zero.
- **Scope:** reverses the **points side only** — provider wallet balances and
  settlements are NOT modified (correct those via the wallet endpoints).

### L1 ✅ `GET /api/loyalty/admin/GetFlaggedActivity?windowHours=24`
Heuristic review queue (v1): `EARN_VELOCITY` (≥10 earns/window),
`REDEMPTION_VELOCITY` (≥3 redemptions/window), `BALANCE_SWING` (≥1000 net
non-admin points/window). Rows: `{ ruleCode, ruleName, userId, userName, metric, windowHours }`.
Thresholds tunable via query parameters.

### Security sweep ✅
All **pre-existing** loyalty admin writes (`AdjustPoints`, `Block/UnblockUser`,
`Block/UnblockProvider`, `Create/EndSeason`, `Create/UpdateReward`,
`Fulfill/CancelRedemption`) are now **Admin-role gated** too — a non-admin token
gets `403` everywhere under `/admin/`.
(`GetProviderRedemptions` deliberately stays token-only because provider owners use it.)

---

## Remaining / needs product decisions

- **Tier CRUD (H1)** — tiers stay read-only until product decides they're business-editable.
- **Expiry policy (H2)** — months-to-expire rule needed before points start expiring.
- **Full money-side reversal** — K1 covers points; reversing provider wallet
  credits/settlement rows is a separate design (settlement locking rules apply).

---

## Testing on dev (Scalar) — client guide

### Setup
1. Open `https://<dev-host>/scalar/v1` → everything is under the **Loyalty** tag,
   endpoint names prefixed **"Admin …"**.
2. Log in with an **Admin account** via `POST /api/users/authenticate` → copy the
   `accessToken` → click Scalar's **Auth** button → Bearer → paste.
3. **Auth rules (same for every `/admin/` endpoint, old and new):**
   - No token → `401`
   - Valid token but non-admin (User/Provider/Worker role) → `403 "Admin role required."`
   - Admin (Role id 2) → works
   This includes the pre-existing writes (`AdjustPoints`, `BlockUser`, `CreateSeason`,
   `FulfillRedemption`, …) — they were opened up before and are now gated too.
   (Gating is role-based for now; it migrates to the privilege system when that is seeded.)

### Complete endpoint checklist (17 new)

| # | Method | Endpoint | Expect on dev today |
|---|--------|----------|---------------------|
| 1 | GET | `/admin/GetUserLoyaltyAccount/{userId}` | Try userId `13953` or `2345` — real balances; unknown id → 404 |
| 2 | GET | `/admin/GetUserPointsHistory/{userId}` | History rows for `13953` / `2345` / `8296` |
| 3 | GET | `/admin/GetAllPointsTransactions` | 5 ledger rows; the 5000-pt AdminAdjust shows `performedByUserId: 2` |
| 4 | GET | `/admin/GetTransactionDetail?activityType=&id=` | No transactions on dev yet → 404 is correct; wrong activityType → 400 |
| 5 | GET | `/admin/GetAllSeasons` | Seasons list |
| 6 | GET | `/admin/GetProviderActivity?providerType=ChargingPoint&providerId=219` | Paged feed (may be empty — no offer/partner txns on dev) |
| 7 | GET | `/admin/GetAllRedemptions` | Empty page `{items:[], totalCount:0}` — no redemptions on dev |
| 8 | GET | `/admin/GetLoyaltySummary` | Real numbers: ~5,107 pts liability ≈ 102 JOD, 3 members, top earners |
| 9 | GET | `/admin/GetAllTiers` | Bronze / Silver / Gold / Platinum ladder |
| 10 | GET | `/admin/GetBlockedUsers` | `[]` — nobody blocked on dev |
| 11 | GET | `/admin/GetBlockedProviders` | `[]` |
| 12 | GET | `/admin/GetRewardPerformance` | Rewards with zero redemptions |
| 13 | GET | `/admin/GetUpcomingExpiries` | Empty — see expiry policy note above |
| 14 | GET | `/admin/GetAdjustmentReasons` | The 7 reason codes |
| 15 | GET | `/admin/GetFlaggedActivity?windowHours=720` | Likely empty at 24h; widen the window to see signals |
| 16 | POST | `/admin/BulkAwardPoints` | Body with no filter → 400; `{"city":"NoSuchCity","points":10}` → 200 with `targetedUsers: 0` (safe test) |
| 17 | POST | `/admin/ReverseTransaction` | Full cycle: `AdjustPoints` +10 on a test user → reverse it (`PointsAdjustment` + the new tx id) → reverse again → 400 "already been reversed" |

Changed existing endpoints: `AdjustPoints` accepts `reasonCode`;
`GetProviderRedemptions` rows now include `userId`.

### Notes for testers
- Paged endpoints: `page`/`pageSize` optional (defaults 1/20, max 200); response is
  always `{ items, totalCount, page, pageSize }`. Empty result = empty page, never 404.
- Dates are returned in **Jordan local time** (existing global converter); the
  `from`/`to` filters are UTC ISO-8601.
- **No DB migration needed for these phases** — deploying the app is enough.
