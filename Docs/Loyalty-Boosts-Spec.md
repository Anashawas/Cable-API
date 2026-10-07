# Loyalty Boosts — Technical Specification

**Status:** Draft for review
**Date:** 2026-08-19
**Feature:** Time-limited multiplied points at selected providers, and a
once-ever first-charge bonus.

---

## 1. Why, and what constrains the design

Two things the client wants:

1. **Campaign boost** — selected charging points award multiplied points during
   a date range, optionally only on certain days and only between certain hours.
2. **First-charge bonus** — a user's very first charge awards multiplied points,
   once ever.

Three facts from production (2026-08-19) shape the whole design:

| Finding | Consequence |
|---|---|
| `LoyaltySeason` has **0 rows, ever** | Anything gated on an active season awards nothing |
| `LoyaltyPointAction` has **0 rows** | Rate/favourite rewards are dead code in production |
| All 50 point transactions have a null action | The **only** live earning path is a partner transaction |

`LoyaltyPointService.AwardPointsAsync` returns `0` when there is no active
season, and again when the action code is not found. Both are empty, so rating a
station and adding a favourite have never awarded a point.

**Therefore: boosts must hook into the spend path only, and must not depend on
seasons or point-actions.** Building on either would reproduce the same silent
failure.

There is no charging telemetry — no `ChargingSession` entity, no kWh, no session
duration. "A charge" is operationally a **completed `PartnerTransaction`**: staff
enter an amount, the API issues a CBL code, the customer scans it.

---

## 2. Scope

**In scope**

- New `LoyaltyBoost` and `LoyaltyBoostProvider` entities
- Three attribution columns on `PartnerTransaction`
- Boost resolution at scan, where the customer is finally known
- Admin CRUD
- Boost surfaced to the partner apps so staff can tell the customer

**Out of scope**

- Boosting the action path (rate/favourite) — dead in production; separate ticket
- Applying tier multipliers to spending (see §11)
- Any change to commission or settlement

---

## 3. Data model

### 3.1 `LoyaltyBoost`

Inherits `BaseAuditableEntity`.

| Column | Type | Notes |
|---|---|---|
| `Name` | nvarchar(150) | Internal label, e.g. "Eid double points" |
| `NameAr` | nvarchar(150) null | Shown to partners/customers |
| `Description` | nvarchar(500) null | |
| `Multiplier` | float | `2.0` = double. Must be > 1.0 |
| `StartsAt` | datetime2 | **UTC**, inclusive |
| `EndsAt` | datetime2 | **UTC**, exclusive |
| `DailyStartMinute` | int null | 0–1439, **Jordan local**. Null = all day |
| `DailyEndMinute` | int null | 0–1439, Jordan local. Null = all day |
| `DaysOfWeekMask` | int null | Bit 0 = Sunday … bit 6 = Saturday. Null = every day |
| `AppliesToAllProviders` | bit | When false, `LoyaltyBoostProvider` lists the targets |
| `FirstTransactionOnly` | bit | The first-charge bonus (§7) |
| `Priority` | int | Tie-breaker, higher wins |
| `MaxBonusPointsPerUser` | int null | Cap on **bonus** points per user, per boost |
| `MaxTotalBonusPoints` | int null | Budget cap for the whole boost |
| `IsActive` | bit | |

`DailyStartMinute` and `DailyEndMinute` are both-or-neither; a validator enforces
that. A window where start > end **crosses midnight** and is valid (22:00–02:00).

### 3.2 `LoyaltyBoostProvider`

| Column | Type |
|---|---|
| `LoyaltyBoostId` | int, FK, cascade delete |
| `ProviderType` | nvarchar(32) — `ChargingPoint` \| `ServiceProvider` |
| `ProviderId` | int |

Unique index on `(LoyaltyBoostId, ProviderType, ProviderId)`.
Non-unique index on `(ProviderType, ProviderId)` for resolution lookups.

### 3.3 `PartnerTransaction` — attribution

| Column | Type | Notes |
|---|---|---|
| `AppliedBoostId` | int null | FK, **no cascade** — a deleted boost must not erase history |
| `BoostMultiplier` | float null | **Snapshotted** at scan |
| `BasePoints` | int null | What would have been awarded without the boost |

`PointsAwarded` keeps its current meaning: the final figure actually banked.
Bonus points are therefore `PointsAwarded - BasePoints`, derivable without a
counter table.

**Why snapshot the multiplier rather than join to the boost.** If only
`AppliedBoostId` were stored and someone later edited the campaign from 2× to
1.5×, every historical transaction would silently re-read at the new rate and
past reporting would change. This mirrors how station update requests already
work: the diff comes from `OldValuesJson`, "a snapshot taken at submit time,
never from the live station."

---

## 4. Timezone handling

Jordan is UTC+3. **`DailyStartMinute` / `DailyEndMinute` are Jordan local
minutes and must be converted, not compared against UTC.** A naive 18:00–21:00
window evaluated in UTC actually runs 21:00–00:00 local.

Use `TimeZoneInfo` with the IANA id `Asia/Amman` rather than a hardcoded `+3`:

```csharp
private static readonly TimeZoneInfo Jordan =
    TimeZoneInfo.FindSystemTimeZoneById("Asia/Amman");

var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, Jordan);
var minuteOfDay = local.Hour * 60 + local.Minute;
```

Jordan abolished DST in 2022 and sits on permanent UTC+3, so a fixed offset
would work *today* — but if that policy is ever reversed, a hardcoded `+3`
shifts every campaign window by an hour with no error. `TimeZoneInfo` tracks it.
.NET 6+ resolves IANA ids on Windows too.

`StartsAt` / `EndsAt` stay UTC, like every other timestamp in the system. Admin
UI is responsible for presenting them in Jordan time.

> **Pre-existing bug, separate ticket:** `LoyaltyPointService` enforces
> `MaxPerDay` using `DateTime.UtcNow.Date`, so its "day" rolls over at 3am
> Jordan time. Not touched here.

---

## 5. Resolution

`ILoyaltyBoostService.ResolveAsync(userId, providerType, providerId, nowUtc, ct)`
→ `BoostResolution?` (`BoostId`, `Multiplier`).

A boost is **eligible** when all hold:

1. `IsActive` and not deleted
2. `StartsAt <= nowUtc < EndsAt`
3. `DaysOfWeekMask` is null, or the bit for **Jordan-local** day-of-week is set
4. Daily window is null, or Jordan-local minute-of-day falls inside it
   (inclusive of start, exclusive of end; handles the midnight-crossing case)
5. `AppliesToAllProviders`, or a matching `LoyaltyBoostProvider` row exists
6. If `FirstTransactionOnly`, the user qualifies per §7
7. Per-user cap not yet reached: `MaxBonusPointsPerUser` is null, or
   `SUM(PointsAwarded - BasePoints)` for this user and boost is below it
8. Budget cap not reached: `MaxTotalBonusPoints` is null, or the same sum across
   all users is below it

**Selection when several are eligible: the single highest `Multiplier` wins.
Boosts never stack.** Tie broken by higher `Priority`, then lower `Id` so the
outcome is deterministic.

Multiplicative stacking is deliberately rejected: a 1.5× tier, a 2× campaign and
a 2× first-charge would compound to 6×, making the giveaway unbounded and
impossible to explain to a customer. "Your points are doubled" is a promise the
business can price.

Caps are evaluated at scan, before points are banked, so a boost that has
exhausted its budget simply stops applying to further charges. Both cap queries are sums over
`PartnerTransaction` filtered by `AppliedBoostId`; index accordingly.

---

## 6. Where it hooks in — resolution happens at scan

**The customer is not known when the code is generated.**
`InitiatePartnerTransactionCommand` takes only `PartnerAgreementId`,
`TransactionAmount` and `CurrencyCode`; `currentUserService.UserId` there is the
**staff member**, and `PartnerTransaction.UserId` is nullable and set at scan.

That rules out evaluating any user-dependent condition at initiation — which is
both `FirstTransactionOnly` (§7) and `MaxBonusPointsPerUser` (§5). Splitting
resolution across the two points would mean two code paths that must agree
forever, so:

**All boost resolution happens in `ScanPartnerCodeCommand`, at the single moment
both the provider and the user are known.**

### 6.1 At initiation

Store the unboosted figure only:

```csharp
var basePoints = (int)Math.Floor((double)pointsEligibleAmount * conversionRate);

BasePoints    = basePoints,
PointsAwarded = basePoints,   // provisional; overwritten at scan if boosted
```

The response may additionally carry a **display-only** `activeBoostMultiplier`,
resolved with provider and time but no user, so staff can say "double points
today". It is never persisted and is explicitly not authoritative — a customer
on their second charge will not receive a first-charge bonus the staff screen
hinted at.

### 6.2 At scan — ordering is load-bearing

Inside the existing `completionTransaction`:

```csharp
transaction.UserId      = userId;
transaction.CompletedAt = DateTime.UtcNow;
transaction.Status      = (int)PartnerTransactionStatus.Completed;

// 1. Resolve with the user finally known, and rewrite the figures.
var boost = await loyaltyBoostService.ResolveAsync(
    userId, transaction.ProviderType, transaction.ProviderId,
    DateTime.UtcNow, cancellationToken);

if (boost is not null)
{
    transaction.AppliedBoostId  = boost.BoostId;
    transaction.BoostMultiplier = boost.Multiplier;
    transaction.PointsAwarded   =
        (int)Math.Floor((transaction.BasePoints ?? 0) * boost.Multiplier);
}

// 2. THEN settlement — it reads PointsAwarded.
await settlementService.UpsertSettlementForPartnerTransactionAsync(
    transaction, cancellationToken);

await applicationDbContext.SaveChanges(cancellationToken);

// 3. THEN award, from the rewritten figure.
```

**The boost rewrite must precede the settlement upsert.**
`SettlementService` reads `transaction.PointsAwarded` directly
(`SettlementService.cs:33`) to populate `TotalPointsAwarded`. Resolve after it
and the settlement permanently records the unboosted figure while the customer
banks the boosted one, and the two never reconcile.

Everything downstream is then consistent: `AwardPointsFromOfferAsync` banks the
rewritten `PointsAwarded`, and `ReverseTransaction` — which refunds
`-(PointsAwarded ?? 0)` — deducts exactly what was granted. All of it happens
inside one database transaction, so a failure anywhere rolls the whole thing
back.

### 6.3 Consequence to accept

A code issued during a campaign but scanned after it closes pays the **base**
rate, because resolution happens at scan. The reverse of what an
initiation-time design would do.

This is the right trade for a 60-second code lifetime, and it is the only
option that keeps first-charge and per-user caps working at all. If codes ever
become long-lived, revisit it.

---

## 7. Welcome bonus — the first charge

**The welcome bonus is not a boost.** It is a property of the app, like the
signup gift it represents: double points the first time a customer ever charges,
at any provider, from the moment the code ships.

This was originally built as a `LoyaltyBoost` row carrying a
`FirstTransactionOnly` flag. That was wrong in one decisive way — it only
existed if somebody remembered to create it. A welcome gift that depends on an
admin seeding a campaign row is a welcome gift that will one day silently not
be there. The flag has been removed and the bonus resolved in code instead.

### 7.1 Configuration is optional, by design

The multiplier is read through `AppSettingsProvider.GetWelcomeBonusMultiplierAsync`,
which follows the existing `NearbyRadiusKm` pattern: a typed reader over the
`AppSetting` key/value table **with a default when no row exists**.

```csharp
public const double DefaultWelcomeBonusMultiplier = 2.0;
```

So with an empty `AppSetting` table, no `LoyaltyBoost` row and nothing seeded
anywhere, a first-time customer still gets double points. Setting the
`WelcomeBonusMultiplier` key overrides it at runtime, with no deploy:

| Value | Effect |
|---|---|
| absent | 2× — the shipped behaviour |
| `3` | triple points on a first charge |
| `1` | bonus switched off |
| below `1` | rejected; the default stands, because a "bonus" must never subtract |

### 7.2 Once per customer, ever — not once per station

The test has **no provider filter**. It asks whether this customer has completed
any charge anywhere, so the bonus is once for the life of the account. A
customer who charges at station A and then visits station B for the first time
does not earn it again.

### 7.3 It never stacks with a campaign

Resolution answers two independent questions — is a campaign running here, and
is this their first charge — and then takes **the higher of the two, never both**:

```csharp
if (campaign is null) return welcome;
if (welcome  is null) return campaign;
return welcome.Multiplier > campaign.Multiplier ? welcome : campaign;
```

A first-timer during a 3× campaign gets 3×, not 6× and not 2×. They consequently
never receive the 2× welcome bonus, since after that charge they are no longer
first-time — deliberate, because they received the larger of the two.

### 7.4 Attribution

`AppliedBoostId` is null for a welcome bonus, since there is no campaign row to
point at. `PartnerTransaction.IsWelcomeBonus` records it explicitly rather than
leaving it to be inferred from that null, so "what has the welcome bonus cost
us" stays a direct query and does not depend on the inference holding.

### 7.5 Why the current transaction is excluded

Because resolution happens at scan (§6), the test is simply whether the user has
any **other** completed transaction:

```csharp
var hasChargedBefore = await db.PartnerTransactions.AnyAsync(t =>
    t.UserId == userId
    && t.Id != currentTransactionId
    && t.Status == (int)PartnerTransactionStatus.Completed
    && !t.IsDeleted,
    ct);
```

Excluding the current row matters: by this point it has already been marked
`Completed` in the change tracker, so without the exclusion every user looks
like a repeat customer and the bonus never fires.

Evaluating at scan also removes the double-award race that an initiation-time
check would have had, where two unscanned codes could each qualify as "first".
Two concurrent scans by the same user remain theoretically possible; the
`UPDLOCK` already taken on the loyalty wallet in `AwardPointsFromOfferAsync`
serialises them, and the second scan then sees the first as completed.

---

## 8. Admin API

Group `/api/loyalty/boosts`, matching `LoyaltyRoutes.cs` conventions, each
handler guarded by `AdminRoleGuard.EnsureAdminAsync` as `CreateSeason` does.

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/loyalty/boosts` | List, filter by active/current |
| GET | `/api/loyalty/boosts/{id}` | Detail, with targeted providers and spend to date |
| POST | `/api/loyalty/boosts` | Create |
| PUT | `/api/loyalty/boosts/{id}` | Update — **only before `StartsAt`**, see below |
| PUT | `/api/loyalty/boosts/{id}/deactivate` | End early; allowed at any time |
| GET | `/api/loyalty/boosts/{id}/performance` | §10 |

**A running campaign is not editable.** Once `StartsAt` has passed, only
`IsActive` may change. `BaseAuditableEntity` timestamps an edit but does not
version it, so a mid-flight rate change would leave no record of the original
terms. Ending a campaign early is a legitimate business need; retroactively
rewriting its rate is not.

**Validation** (FluentValidation, following `CreateWorkerCommandValidator`):

- `Multiplier` > 1.0 and <= 10.0 — the upper bound is a typo guard; 20× is
  almost certainly a slipped decimal, and it would be applied to real money
- `EndsAt` > `StartsAt`
- Daily minutes both null or both 0–1439
- `DaysOfWeekMask` null or 1–127
- When `AppliesToAllProviders` is false, at least one provider must be listed
- Caps, when present, > 0

---

## 9. Partner and mobile surface

Staff should be able to tell the customer, and the customer should see why the
number is larger than usual.

- `InitiatePartnerTransaction`'s response gains `boostMultiplier` and `basePoints`
- Partner web and mobile show a `×2 نقاط مضاعفة` badge on the transaction screen
  when `boostMultiplier > 1`
- The customer's points history shows the boosted line with its multiplier,
  from the snapshot rather than a live campaign lookup

A boost nobody is told about buys no behaviour change, which is the entire point
of running one.

---

## 10. What the history answers

Because both the campaign and the per-transaction attribution are recorded:

```sql
-- Cost and reach of a campaign
SELECT COUNT(*)                             AS Charges,
       COUNT(DISTINCT UserId)               AS Customers,
       SUM(BasePoints)                      AS BasePoints,
       SUM(PointsAwarded - BasePoints)      AS BonusPoints,
       SUM(TransactionAmount)               AS Revenue
FROM PartnerTransaction
WHERE AppliedBoostId = @boostId
  AND Status = 2 AND IsDeleted = 0;
```

Did it work? Compare charge volume at boosted providers during the window
against the equivalent period before it, and against non-boosted providers over
the same window — the second comparison controls for seasonality, which the
first alone cannot.

For the first-charge bonus: how many users triggered it, and how many returned
for a second charge afterwards. That retention figure is the only number that
says whether the bonus bought anything.

---

## 11. Known issues this spec deliberately leaves alone

1. **Tier multipliers do not apply to spending.** `AwardPointsAsync` applies
   `LoyaltyTier.Multiplier`; `AwardPointsFromOfferAsync` does not. A Gold
   customer gets their bonus for rating a station but not for spending money.
   This looks like an oversight, but changing it alters live earning rates and
   belongs in its own ticket.
2. **Rate/favourite rewards are dead** (§1). Seeding `LoyaltyPointAction` and
   activating a season would switch them on — confirm that is wanted first,
   since users would abruptly begin accruing points from actions that have
   earned nothing to date.
3. **`MaxPerDay` rolls over at 3am Jordan time** (§4).

---

## 12. Testing

**Unit — boost resolution** (the whole risk sits here):

- Outside `StartsAt`/`EndsAt` → no boost
- Inside window, wrong day-of-week → no boost
- Daily window 18:00–21:00: 17:59 no, 18:00 yes, 20:59 yes, 21:00 no —
  **asserted against Jordan time, with the clock set to UTC**
- Midnight-crossing window 22:00–02:00: 23:00 yes, 01:00 yes, 03:00 no
- Provider not in the target list → no boost
- Two eligible boosts → the higher multiplier wins, never their product
- Equal multipliers → higher `Priority`, then lower `Id`
- Per-user cap reached → no boost; another user still receives it
- Budget cap reached → no boost for anyone
- `FirstTransactionOnly`: no history → applies; one completed → does not;
  **one unexpired initiated code → does not**; one expired initiated code → applies

**Integration:**

- Initiate under a boost → `BasePoints`, `BoostMultiplier`, `AppliedBoostId`,
  `PointsAwarded` all persisted consistently
- Scan → banked points equal the stored `PointsAwarded`
- Reverse a boosted transaction → the full boosted amount is deducted, leaving
  no residue
- Edit a boost's multiplier after transactions exist → historical rows still
  report the original rate

---

## 13. Migration

`Scripts/LoyaltyBoosts_Phase1.sql`, following the guarded, idempotent style of
`ProviderSessionStamp.sql`: `IF COL_LENGTH(...) IS NULL` / `IF OBJECT_ID(...) IS
NULL` around every change, and a verification report at the end.

Additive only — two new tables and three nullable columns. Existing rows read as
`AppliedBoostId = NULL`, meaning "no boost", which is correct for every
transaction that predates the feature. No backfill required, and the change is
safe to deploy ahead of the code.
