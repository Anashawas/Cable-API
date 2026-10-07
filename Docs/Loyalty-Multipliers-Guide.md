# Points Multipliers — Welcome Bonus & Boost Campaigns

**Audience:** mobile team, client/QA team, admins
**Status:** built, migrated and verified on **dev**. Not yet on production.
**Design detail:** `Docs/Loyalty-Boosts-Spec.md`

---

## 1. What was built

Two ways a charge can earn more than its ordinary points. They are separate
features and they **never stack**.

| | Welcome bonus | Boost campaign |
|---|---|---|
| What | Double points on a customer's **first ever charge** | A multiplier at chosen stations for a chosen period |
| Where | Every provider | All providers, or a named list |
| When | Any time, never expires | Date range, optional time-of-day and days-of-week |
| How often | **Once per customer, ever** | Every qualifying charge |
| Needs setup? | **No — on by default** | Yes, an admin creates each campaign |
| Lives in | `AppSetting` (optional override only) | `LoyaltyBoost` table |

---

## 2. Welcome bonus

A customer installs the app and charges for the first time: **double points**.

### It works with nothing configured

This is the important part. There is **no row to create**. Ship the code and it
is live at 2×. An empty `LoyaltyBoost` table and an empty `AppSetting` table
still give a first-time customer double points.

It is once per **account**, not once per station — a customer who charges at
station A and then visits station B for the first time does **not** earn it
again.

### Changing it

`GET /api/settings/welcome-bonus` (admin)

```json
{ "multiplier": 2, "isEnabled": true, "isDefault": true }
```

- `isDefault: true` — nobody has overridden it; the shipped 2× is in force
- `isEnabled` — simply `multiplier > 1`

`PUT /api/settings/welcome-bonus` (admin)

```json
{ "multiplier": 3 }
```

| Value | Effect |
|---|---|
| `2` | double points (the default) |
| `3` | triple points |
| **`1`** | **switches the bonus off** |
| below 1, or above 10 | rejected with 400 |

**There is no separate on/off flag.** `1` is how you disable it. One number
cannot contradict itself; a flag plus a multiplier can end up disagreeing, and
then nobody can tell from the row whether the bonus is actually running.

Changes take effect **immediately** — no deploy, no restart. The multiplier is
read on every scan. Disabling only affects future charges; customers who already
received it keep their points.

---

## 3. Boost campaigns

Admin-created multipliers, for things like "triple points at Yalla Charge,
weekday evenings during Ramadan".

`GET /api/loyalty/boosts` · `POST /api/loyalty/boosts` ·
`PUT /api/loyalty/boosts/{id}` · `PUT /api/loyalty/boosts/{id}/deactivate`
— all admin-only.

Key fields:

| Field | Meaning |
|---|---|
| `multiplier` | Must be > 1 and ≤ 10 |
| `startsAt` / `endsAt` | **UTC.** Start inclusive, end exclusive |
| `dailyStartMinute` / `dailyEndMinute` | Minutes from midnight in **Jordan local time**, 0–1439. Both or neither. Start > end crosses midnight (22:00–02:00) |
| `daysOfWeekMask` | Bit 0 = Sunday … bit 6 = Saturday. Null = every day |
| `appliesToAllProviders` | When false, `providers` lists the targets |
| `providers` | `[{ "providerType": "ChargingPoint", "providerId": 59 }]` |
| `priority` | Tie-break when two campaigns share a multiplier |
| `maxBonusPointsPerUser` / `maxTotalBonusPoints` | Budget caps on **bonus** points |

⚠️ **The two time fields use different clocks.** `startsAt`/`endsAt` are UTC;
the daily window is Jordan local. This is the single easiest thing to get wrong
— a window of 540–600 is 09:00–10:00 **in Amman**, not UTC.

Once a campaign has started it can no longer be edited — only deactivated. A
running campaign is a record of terms customers were given, and there is no
versioning to show what the original terms were.

---

## 4. How the winner is chosen

Both are evaluated, then **the higher multiplier wins outright**.

```
campaign?  →  no  →  welcome bonus (if first charge)
welcome?   →  no  →  campaign
both       →  the larger of the two
```

Worked examples, base 25 points:

| Customer | Campaign running | Awarded | Why |
|---|---|---|---|
| First ever charge | none | **50** | welcome ×2 |
| First ever charge | ×3 | **75** | ×3 beats ×2 — **not 150** |
| First ever charge | ×1.5 | **50** | ×2 beats ×1.5 |
| 2nd charge | none | 25 | welcome already used |
| 2nd charge | ×3 | 75 | campaign |

A first-timer who charges during a ×3 campaign gets ×3 and then never receives
the welcome bonus, because after that charge they are no longer first-time.
That is deliberate — they got the larger of the two.

---

## 5. For the mobile team

### `GET /api/partners/PreviewPartnerCode?code=PTR-XXXXXX`

The confirmation sheet. **Three new fields**:

```json
{
  "pointsToBeAwarded": 50,
  "basePoints": 25,
  "multiplier": 2.0,
  "isWelcomeBonus": true
}
```

- `pointsToBeAwarded` — final points, **multiplier already applied**. This
  matches exactly what the scan will award.
- `basePoints` — before the multiplier. Equal to `pointsToBeAwarded` when none applies.
- `multiplier` — **null when no multiplier applies.** `2.0` = double.
- `isWelcomeBonus` — true when it is the once-ever first-charge bonus, so you
  can word it differently ("Welcome gift — double points!" vs "Double points
  here today").

Suggested UI: when `multiplier` is not null, show the badge and both numbers —
`~~25~~ **50** ×2`. When it is null, render as before.

> **Changed behaviour:** `pointsToBeAwarded` previously returned the unboosted
> figure, so the sheet would say 25 and the scan would award 50. It is now
> boosted. If you were computing anything from it, re-check.

### `POST /api/partners/ScanPartnerCode?code=PTR-XXXXXX`

Unchanged shape. `pointsAwarded` is the final boosted figure and always matches
the preview.

Nothing else changes. No new endpoint to call, no new required field.

### `GET /api/offers/PreviewOfferCode?code=CBL-XXXXXX` — new

The same confirmation step, for **spending** points on an offer. Previously the
user scanned and the points were simply gone; there was no sheet.

```json
{
  "offerTitle": "Small Wash",
  "offerTitleAr": "غسيل",
  "offerImageUrl": "",
  "providerName": "test wash",
  "pointsToBeDeducted": 100,
  "monetaryValue": 2.0,
  "currencyCode": "JOD",
  "currentPointsBalance": 0,
  "pointsShortfall": 100,
  "expiresInSeconds": 58,
  "canConfirm": false,
  "blockReason": "Insufficient points balance. Required: 100, Available: 0"
}
```

`pointsShortfall` is `0` when affordable, otherwise how many points are missing —
so you can show *"you need 50 more points"* rather than only a refusal.

`canConfirm: false` covers **every** rule the scan enforces — blocked account,
insufficient balance, per-user usage limit — so nothing the user can hit on
confirm is invisible on the sheet. Read-only: it deducts nothing and does not
even mark an expired code as expired.

Flow is now **scan → preview → confirm → `ScanOfferCode`**. `ScanOfferCode` is
unchanged, so skipping the preview still works.

---

## 6. For QA — what to test

1. **Brand-new account, first charge** → double points, with the badge on the
   preview sheet.
2. **Same account, second charge at a different station** → ordinary points.
   *(This is the one most likely to be wrong in a naive implementation — it must
   not pay again just because the station is new to them.)*
3. **Preview then scan** → the two numbers are identical.
4. **First-timer during a ×3 campaign** → 3× base, not 6×.
5. **`PUT /api/settings/welcome-bonus {"multiplier":1}`**, then a new account
   charges → ordinary points. Set back to `2` → double again. No restart.
6. **Returning customer, no campaign** → ordinary points.
7. **`GET /api/loyalty/boosts`** with no query string → 200, not 500.

Offer redemption confirmation:

8. **Preview an offer you cannot afford** → `canConfirm: false`, `pointsShortfall`
   equals the gap, and the scan then refuses with the *same* message.
9. **Preview an offer you can afford** → `canConfirm: true`, `pointsShortfall: 0`,
   and the scan succeeds and deducts exactly `pointsToBeDeducted`.
10. **Offer with `maxUsesPerUser: 1`, redeem once, preview again** → `canConfirm:
    false` with the limit message.
11. **Blocked loyalty account with enough points** → `canConfirm: false` with the
    blocked message.
12. **Let a code expire, then preview** → `400`, and the transaction row is still
    `Initiated` — the preview must never mutate it.

All twelve verified passing on dev against the current build.

---

## 7. Deployment

**Dev** — done. Schema migrated, verified end to end, no seed data required.

**Production** — not yet applied. Two scripts, in order:

1. `Scripts/LoyaltyBoosts_Phase1.sql` — tables and columns
2. `Scripts/LoyaltyBoosts_Phase2_WelcomeBonus.sql` — no-op on a fresh database;
   only does work where the older schema was already applied

Nothing to seed. The welcome bonus is live the moment the code is deployed.

> ⚠️ Phase 2 has a **load-bearing statement order**, documented in its header.
> An old-style first-charge row with the flag dropped out from under it would
> silently become "double points for every customer at every station, forever".
> The script retires those rows before dropping the column. Do not reorder it.

Also outstanding for production, unrelated to this feature but blocking a
publish: `Scripts/ProviderSessionStamp.sql` — production has no
`ProviderWebSecurityStamp` column, and without it every authenticated request
returns 500.

---

## 8. Reporting

Every boosted charge records what happened, so cost is reconstructable from the
transactions alone — there is no counter to fall out of step:

| Column on `PartnerTransaction` | |
|---|---|
| `BasePoints` | before the multiplier |
| `PointsAwarded` | after |
| `BoostMultiplier` | what was applied |
| `AppliedBoostId` | the campaign — **null for the welcome bonus** |
| `IsWelcomeBonus` | true when it was the welcome bonus |

Bonus cost is always `PointsAwarded - BasePoints`.

```sql
-- What has the welcome bonus cost us?
SELECT COUNT(*) AS Charges,
       SUM(PointsAwarded - BasePoints) AS BonusPoints
FROM PartnerTransaction
WHERE IsWelcomeBonus = 1 AND IsDeleted = 0 AND Status = 2;
```

`GET /api/loyalty/boosts` returns `bonusPointsSpent` and `boostedTransactions`
per campaign, derived the same way.

---

## 9. Known gaps

- **No admin UI.** The welcome-bonus toggle and the campaign screens are API
  only. Cable-Admin has nothing wired to them yet.
- **No unit tests** for the resolution matrix — verified end to end on dev, but
  not covered by automated tests.
- **No `×2` badge in the partner web app.** Note the provider side has no
  confirmation sheet at all — it issues the code, the customer scans it. What
  partner web *does* show is the transactions list, whose `pointsAwarded` is
  already the boosted figure, just unlabelled. Surfacing "this was boosted"
  there means adding `basePoints`/`boostMultiplier`/`isWelcomeBonus` to that
  list DTO.
