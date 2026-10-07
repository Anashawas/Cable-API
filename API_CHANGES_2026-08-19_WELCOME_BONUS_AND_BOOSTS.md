# API Changes — 2026-08-19

Welcome bonus (first-charge double points) · loyalty boost campaigns · offer redemption
confirmation · two bug fixes

Everything below is implemented, built, and **verified end-to-end against the dev
database**. Every number in this document came from a real request during that run —
none of it is illustrative.

**Base URL (dev):** as configured per environment
**Auth:** `Authorization: Bearer <accessToken>` unless stated otherwise

---

## Summary

| # | Change | Endpoint | Status |
|---|---|---|---|
| 1 | Double points on a customer's first ever charge | *(none — intrinsic)* | New |
| 2 | Admin control for the welcome bonus | `GET/PUT /api/settings/welcome-bonus` | New |
| 3 | Time-limited points multipliers at chosen stations | `/api/loyalty/boosts` ×4 | New |
| 4 | Preview showed unboosted points | `GET /api/partners/PreviewPartnerCode` | **Fixed + changed** |
| 5 | Boost list 500'd without query params | `GET /api/loyalty/boosts` | Fixed |
| 6 | Confirmation sheet before spending points on an offer | `GET /api/offers/PreviewOfferCode` | New |
| 7 | Last-login and active-user tracking | `GET /api/users/activity-stats` | New |

### Action required before deploying

0. **Run `Scripts/UserActivityTracking.sql`** — adds `LastLoginAt` / `LastSeenAt`
   to `UserAccount`. Additive and nullable; safe before the code deploys.
1. **Run `Scripts/LoyaltyBoosts_Phase1.sql`** — creates `LoyaltyBoost`,
   `LoyaltyBoostProvider` and four `PartnerTransaction` columns.
2. **Then run `Scripts/LoyaltyBoosts_Phase2_WelcomeBonus.sql`** — no-op on a fresh
   database; only does work where the older schema was applied.
   ⚠️ **Its statement order is load-bearing** — see [§9](#9-deployment).
3. **Nothing to seed.** The welcome bonus is live the moment the code deploys.
4. **One changed response** — see [§4](#4-fixed-preview-showed-the-wrong-points).

Both scripts are already applied to **dev**. Neither has been run on production.

---

## 1. Welcome bonus — double points on a first ever charge

A customer installs the app and charges for the first time: **double points**.

**There is no row to create.** With an empty `LoyaltyBoost` table and an empty
`AppSetting` table, a first-time customer still gets double points. It is a property
of the app, not a campaign — a welcome gift that depends on an admin remembering to
seed a row is one that will eventually not be there.

- Applies at **every** provider, whether or not any campaign is running
- Never expires
- **Once per account, ever** — not once per station. A customer who charges at
  station A and then visits station B for the first time does **not** earn it again

Default multiplier is `2.0`, defined in `AppSettingsProvider`.

---

## 2. Admin control for the welcome bonus

### `GET /api/settings/welcome-bonus` 🔒 admin

```json
{ "multiplier": 2, "isEnabled": true, "isDefault": true }
```

| Field | Meaning |
|---|---|
| `multiplier` | What a first ever charge is multiplied by |
| `isEnabled` | Simply `multiplier > 1`. Derived, not stored |
| `isDefault` | `true` = nobody has overridden it; the shipped 2× is in force |

### `PUT /api/settings/welcome-bonus` 🔒 admin

```json
{ "multiplier": 3 }
```

| Value | Effect |
|---|---|
| `2` | double points (default) |
| `3` | triple points |
| **`1`** | **switches the bonus off** |
| below 1 or above 10 | `400` |

**There is no separate on/off flag — `1` is how you disable it.** One number cannot
contradict itself; a flag plus a multiplier can disagree, and then nobody can tell
from the row whether the bonus is running.

Takes effect **immediately** — no deploy, no restart. Disabling affects only future
charges; customers who already received it keep their points.

Returns `403` for non-admins.

---

## 3. Loyalty boost campaigns

Admin-created multipliers — "triple points at Yalla Charge, weekday evenings during
Ramadan".

| Method | Route |
|---|---|
| `GET` | `/api/loyalty/boosts?activeOnly=&currentOnly=` (both optional) |
| `GET` | `/api/loyalty/boosts/{id}` |
| `POST` | `/api/loyalty/boosts` |
| `PUT` | `/api/loyalty/boosts/{id}` |
| `PUT` | `/api/loyalty/boosts/{id}/deactivate` |

All admin-only.

**Create body**

```json
{
  "name": "Ramadan evenings",
  "nameAr": "أمسيات رمضان",
  "multiplier": 3,
  "startsAt": "2026-03-01T00:00:00Z",
  "endsAt": "2026-03-30T00:00:00Z",
  "dailyStartMinute": 1080,
  "dailyEndMinute": 1380,
  "daysOfWeekMask": 62,
  "appliesToAllProviders": false,
  "providers": [{ "providerType": "ChargingPoint", "providerId": 59 }],
  "priority": 0,
  "maxBonusPointsPerUser": null,
  "maxTotalBonusPoints": 50000
}
```

⚠️ **The two time fields use different clocks.** `startsAt`/`endsAt` are **UTC**;
`dailyStartMinute`/`dailyEndMinute` are minutes from midnight in **Jordan local
time**. A window of `540`–`600` is 09:00–10:00 in Amman, not UTC. This is the single
easiest thing to get wrong. `daysOfWeekMask` bit 0 = Sunday.

Start is inclusive, end exclusive. A daily start later than the end is a valid window
that crosses midnight (22:00–02:00).

**Once a campaign has started it can no longer be edited** — only deactivated. A
running campaign is a record of terms customers were given, and there is no
versioning to show what the original terms were.

The list response includes `bonusPointsSpent` and `boostedTransactions` per campaign.

---

## 4. Fixed — preview showed the wrong points

**This is the one client change to be aware of.**

`GET /api/partners/PreviewPartnerCode?code={code}` previously returned the
**unboosted** figure in `pointsToBeAwarded`. The confirmation sheet said 25, the
customer confirmed, and the scan awarded 50. The code is issued before the customer
is known, so no multiplier could be resolved at that point — and nothing resolved it
at preview time either.

It now runs the same resolution the scan uses. **Three new fields:**

```json
{
  "pointsToBeAwarded": 50,
  "basePoints": 25,
  "multiplier": 2.0,
  "isWelcomeBonus": true
}
```

| Field | Meaning |
|---|---|
| `pointsToBeAwarded` | **Now boosted.** Exactly what the scan will award |
| `basePoints` | Before the multiplier. Equal to `pointsToBeAwarded` when none applies |
| `multiplier` | **`null` when no multiplier applies.** `2.0` = double |
| `isWelcomeBonus` | `true` when it is the once-ever first-charge bonus |

Suggested UI: when `multiplier` is not null, show `~~25~~ **50** ×2`. Use
`isWelcomeBonus` to word it differently — "Welcome gift — double points!" versus
"Double points here today".

> If you were computing anything from `pointsToBeAwarded`, re-check it.

`POST /api/partners/ScanPartnerCode` is **unchanged in shape**. `pointsAwarded` is the
final boosted figure and now always matches the preview.

---

## 5. Fixed — boost list returned 500

`GET /api/loyalty/boosts` with no query string returned `500`:

```
Required parameter "bool activeOnly" was not provided from query string.
```

`[FromQuery] bool` binds as **required** in minimal APIs even when the underlying
record has a default. Both filters are now nullable, so a bare call returns `200`.

Two unrelated routes have the same defect and were **left alone** to keep this deploy
narrow — `OfferRoutes.cs:510` (`unpaidOnly`, `hasDebt`) and `WorkerRoutes.cs:72`
(`isActive`).

---

## 6. New — confirmation sheet before redeeming an offer

Charging had a preview step; **redeeming an offer did not**. The user scanned and
the points were gone. That gap matters more here than on the charging side: a
partner scan only ever *gives* points, while an offer scan *spends* them and can
fail outright — most obviously when the balance is short.

### `GET /api/offers/PreviewOfferCode?code={code}` 🔒

Read-only. Deducts nothing, completes nothing. Mirrors `PreviewPartnerCode`.

```json
{
  "transactionId": 4,
  "offerCode": "CBL-UXB5UN",
  "offerId": 4,
  "offerTitle": "Small Wash",
  "offerTitleAr": "غسيل",
  "offerImageUrl": "",
  "providerName": "test wash",
  "providerType": "ServiceProvider",
  "providerId": 2,
  "pointsToBeDeducted": 100,
  "monetaryValue": 2.0,
  "currencyCode": "JOD",
  "currentPointsBalance": 0,
  "pointsShortfall": 100,
  "codeExpiresAt": "2026-08-22T16:21:41.383",
  "expiresInSeconds": 58,
  "canConfirm": false,
  "blockReason": "Insufficient points balance. Required: 100, Available: 0"
}
```

`pointsShortfall` is `0` when affordable, otherwise how many points are missing —
so the app can say *"you need 50 more points"* instead of only refusing.

**`canConfirm: false` covers every rule `ScanOfferCode` enforces**, so nothing the
user can hit on confirm is invisible on the sheet:

| Cause | `blockReason` |
|---|---|
| Loyalty account blocked | "Your loyalty account is currently blocked…" |
| Not enough points | "Insufficient points balance. Required: N, Available: M" |
| Per-user limit reached | "You have reached the maximum usage limit for this offer" |

An expired code returns `400`, and — like the partner preview — **does not flip the
row to Expired**. A preview must not mutate; `ScanOfferCode` still does that if the
user confirms.

Flow is now **scan → preview → user confirms → `ScanOfferCode`**. `ScanOfferCode`
is unchanged; existing clients keep working if they skip the preview.

#### Verified on dev

| Case | Result |
|---|---|
| Balance 0, offer costs 100 | `canConfirm:false`, shortfall 100 ✅ |
| …and the scan then refuses | identical message, 400 ✅ |
| Balance 150, costs 100 | `canConfirm:true`, shortfall 0 → scan succeeds ✅ |
| Balance 50 after spending | `canConfirm:false`, shortfall 50 ✅ |
| `MaxUsesPerUser=1`, second attempt | `canConfirm:false`, limit message ✅ |
| Blocked account, sufficient balance | `canConfirm:false`, blocked message ✅ |
| Expired code | `400`, row still `Initiated` ✅ |

---

## 7. New — last login and active-user tracking

Nothing tracked sign-ins before this: no `LastLoginAt`, no session table, and
JWTs are stateless. The only proxy was `NotificationToken.UpdatedAt`, which
misses the users who never granted notification permission.

Two nullable columns on `UserAccount`:

| Column | Written when | Answers |
|---|---|---|
| `LastLoginAt` | Every successful sign-in | "when did this user last sign in" |
| `LastSeenAt` | Any authenticated request, **throttled to one write per user per 15 min** | "how many people use the app" |

Both are needed. Access tokens are long-lived, so somebody who opens the app
daily may not have re-authenticated in months — `LastLoginAt` alone would make
active users look dormant. `LastSeenAt` is the real DAU/MAU signal.

The write is stamped by `UserActivityTrackingMiddleware`, which runs **after** the
request: a failed or unauthorized request is not activity, and telemetry must
never be the reason a request fails.

### `GET /api/users/activity-stats` 🔒 admin

```json
{
  "totalUsers": 13996,
  "dailyActiveUsers": 1,
  "weeklyActiveUsers": 1,
  "monthlyActiveUsers": 1,
  "neverSeen": 13995,
  "newUsersToday": 0,
  "newUsersThisWeek": 4,
  "newUsersThisMonth": 6,
  "loggedInLast30Days": 1,
  "generatedAtUtc": "2026-08-22T23:33:23.624"
}
```

> **Tracking starts at deploy.** Every existing user reads `NULL` until they next
> use the app, so `neverSeen` starts at almost the total user count and falls over
> the following weeks. The first week of numbers understates reality — do not read
> them as a drop in usage.

`loggedInLast30Days` will always be far below `monthlyActiveUsers`, by design.

#### Verified on dev

| Test | Result |
|---|---|
| Login stamps both columns | ✅ |
| Plain authenticated request updates `LastSeenAt` | ✅ |
| **10 rapid requests → 0 extra writes** (throttle) | ✅ |
| Anonymous request | 200, no write, no error ✅ |
| Invalid token | 401, no write, no error ✅ |

---

## 8. How the multiplier is chosen

The welcome bonus and campaigns are evaluated independently, then **the higher
multiplier wins outright. They never stack.**

Worked examples, base 25 points — all captured on dev:

| Customer | Campaign | Awarded | Why |
|---|---|---|---|
| First ever charge | none | **50** | welcome ×2 |
| First ever charge | ×3 | **75** | ×3 beats ×2 — **not 150** |
| First ever charge | ×1.5 | **50** | ×2 beats ×1.5 |
| 2nd charge, new station | none | **25** | welcome already used |
| 2nd charge | ×3 | **75** | campaign |

A first-timer charging during a ×3 campaign gets ×3 and then never receives the
welcome bonus, because afterwards they are no longer first-time. Deliberate — they
received the larger of the two.

### Verified on dev

| Test | Result |
|---|---|
| First charge, no campaign, nothing configured | 50 ✅ |
| 2nd charge at a different station | 25 ✅ |
| Preview matches scan | 50 = 50 ✅ |
| First-timer during a ×3 campaign | 75, not 150 ✅ |
| `PUT {"multiplier":1}` then charge | 25 ✅ |
| Re-enabled, new customer | 50 ✅ |
| Bare `GET /boosts` | 200 ✅ |

---

## 9. Deployment

**Dev** — schema applied, verified, nothing seeded. Deploy the code and it is live.

**Production** — neither script has been run.

1. `Scripts/UserActivityTracking.sql`
2. `Scripts/LoyaltyBoosts_Phase1.sql`
3. `Scripts/LoyaltyBoosts_Phase2_WelcomeBonus.sql`

> ⚠️ **Phase 2's statement order is load-bearing**, documented in its header. An
> old-style first-charge boost row with `FirstTransactionOnly` dropped out from under
> it would silently become *"double points for every customer at every station,
> forever"* — no end date, no cap. The script retires those rows **before** dropping
> the column. Do not reorder it.

Both scripts are guarded and idempotent.

**Still blocking any production publish, unrelated to this work:**
`Scripts/ProviderSessionStamp.sql` — production has no `ProviderWebSecurityStamp`
column, and without it every authenticated request returns 500.

---

## 10. Schema changes

**New:** `LoyaltyBoost`, `LoyaltyBoostProvider`

**`PartnerTransaction`** — four nullable/defaulted columns, additive only. Existing
rows read as unboosted, so no backfill:

| Column | |
|---|---|
| `BasePoints` | points before the multiplier |
| `BoostMultiplier` | what was applied |
| `AppliedBoostId` | the campaign — **null for the welcome bonus** |
| `IsWelcomeBonus` | true when it was the welcome bonus |

**Removed:** `LoyaltyBoost.FirstTransactionOnly`. The welcome bonus was briefly built
as a boost row carrying this flag; it is now intrinsic, and two mechanisms answering
"is this their first charge?" could disagree.

Bonus cost is always `PointsAwarded - BasePoints` — derived from the transactions, so
there is no counter that can fall out of step.

```sql
-- What has the welcome bonus cost us?
SELECT COUNT(*) AS Charges, SUM(PointsAwarded - BasePoints) AS BonusPoints
FROM PartnerTransaction
WHERE IsWelcomeBonus = 1 AND IsDeleted = 0 AND Status = 2;
```

---

## 11. Known gaps

- **No admin UI** for either feature. API only; Cable-Admin has nothing wired up.
- **No unit tests** for the resolution matrix — verified end to end, not automated.
- **No ×2 badge** in the partner web app. The data is there; the UI is not.

---

## Related documents

- `Docs/Loyalty-Multipliers-Guide.md` — feature guide for mobile, QA and admins
- `Docs/Loyalty-Boosts-Spec.md` — internal design record and rationale
