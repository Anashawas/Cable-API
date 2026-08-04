# BE Notes 2026-07-07 — Backend Response (Backend → Admin Portal)

**In response to:** `BE_NOTES_2026-07-07.md`
**Date:** 2026-07-08
**Status:** Implemented on **DEV** and live-verified. One small DB migration
(`Scripts/Offers_Phase1_PointsPriceValue.sql`) — already applied on dev.
Parked items (need answers) listed at the end.

---

# A. Users

## A1 ✅ `GET /api/users/summary` (Admin role required)

Exactly the proposed shape: `totalUsers`, `active`, `deleted`, `phoneVerified`,
`withVehicles`, `newToday/ThisWeek/ThisMonth`, `byRole` (matches the tab badges —
verified: 13,980 User / 5 Provider / 2 Admin on dev), `byCity` (top 10 + `Other`
bucket), `registrationTrend` (last 30 days).
Notes: date boundaries are UTC; week starts Sunday (same as settlements). No
caching server-side yet — the aggregate is a few milliseconds at 14k users; add
client-side caching if you like.

## A2 ✅ Optional server-side paging — Users AND Stations

Both use the agreed opt-in pattern: **legacy calls are untouched** (plain array);
adding any new parameter switches to the paged envelope. Omitting
`page`/`pageSize` (with other filters present) returns the **full filtered set**
in the envelope.

**Envelope (all paged endpoints, loyalty ones included):**
```json
{ "items": [...], "totalCount": 13987, "page": 2, "pageSize": 25,
  "totalPages": 560, "hasNextPage": true, "hasPreviousPage": true }
```
`totalPages` / `hasNextPage` / `hasPreviousPage` are new additive fields — no
existing field changed.

### A2a — Users: `GET /api/users/GetAllUsers` (Admin for the new params)
New query params: `search` (name / email / phone / **id**), `roleId`, `city`,
`isDeleted` (default false), `sort` (`createdAt_asc|createdAt_desc|name_asc|name_desc`,
default newest first), `page`, `pageSize` (max 500).
Rows are the existing `UserSummaryDto` fields — unchanged.
Verified live: `?search=hamzeh&page=1&pageSize=3` → `totalCount: 51`, 3 rows.

### A2b — Stations: `POST /api/charging-points/GetAllChargingPoints`
New body fields: `search` (name / address / city), `statusId`, `chargerBrandId`,
`isVerified`, `plugTypeId`, `sort`
(`name_asc|name_desc|visitors_asc|visitors_desc|rating_asc|rating_desc`), `page`,
`pageSize`. Existing `chargerPointTypeId` / `cityName` still work as filters.
Rows are the existing station DTO (incl. `hasOwner`).
Verified live: page 1 size 3 by `visitors_desc` → `totalCount: 180`.

## A3 ✅ `createdAt` coverage — CONFIRMED, no backfill needed
All 13,988 users have `createdAt` populated (0 NULL, 0 bogus dates, no
bulk-backfill cluster). The growth fields in A1 are reliable. (Dev shows
`newToday = 0` simply because dev's latest registrations are from April.)

---

# B. Offers

## B1 ✅ `pointsPriceValue` — persisted

- New nullable `pointsPriceValue` (decimal 18,3) on offers.
- Accepted in **ProposeOffer** and **UpdateOffer** bodies; returned in **OfferDto**
  (all list + detail endpoints).
- **Server-side derivation (your recommended option):** when omitted, BE derives
  it as `pointsCost ÷ pointsPerUnit` using the **active conversion rate for the
  offer's currency** (default rate preferred). When sent, it is **stored as sent**
  (admin rounding respected — no rejection).
- Existing offers were **backfilled** from the conversion rate. Verified on dev:
  485 pts → 9.700 JOD, 500 pts → 10.000 JOD (vs `monetaryValue` 8.000 / 2.000 —
  the two values are distinct as specified).
- Flow-through to settlement records: parked as nice-to-have (per your note).

**Migration:** `Scripts/Offers_Phase1_PointsPriceValue.sql` (idempotent; includes
the backfill). Applied on dev; run on prod at release.

---

# C. Settlements

## C1 ✅ `GET /api/offers/GetSettlementTransactions?settlementId={id}` (Admin)

The line-items behind one settlement: every **completed** offer redemption and
partner charge for the settlement's provider inside its Sunday–Saturday week
(computed with the same calendar rule the settlement engine uses). Row shape:

```json
{
  "activityType": "Partner",
  "transactionId": 7,
  "userId": 13953,
  "userName": "Saad",
  "code": "PTR-XXXX",
  "status": 2,
  "statusName": "Completed",
  "points": 50,
  "amount": 20.0,
  "currencyCode": "JOD",
  "createdAt": "...",
  "completedAt": "..."
}
```

**Verified against real data:** settlement #1 (week 15) returned exactly its 4
partner transactions and the amounts sum to the settlement's stored totals
(20+8+5+10 = 43.00 JOD ✓).

## C2 ✅ Summary additions on `GetSettlementSummary`
- `totalOutstandingAmount` — Σ (commission − walletApplied)
- `totalDisputedAmount` — Σ |netBalance| where Disputed
- `totalPartnerTransactionAmount`, `totalPointsAwarded`, `totalPointsDeducted`
  were already in the response — keep using them.

## C3 ✅ `PUT /api/offers/UpdateSettlementStatusBatch`
Body: `{ "settlementIds": [1,2,3], "status": 3, "note": "..." }` →
`{ "updated": [...], "failed": [{ "settlementId", "reason" }] }`.
Per-id validation with the same rules as the single update: **Paid stays locked**,
can't mark Paid while the week is still active; failures don't abort the batch.

## C4 ✅ `GET /api/offers/GetSettlementsCsv?year=&week=&periodType=&status=&search=`
CSV download (`settlements.csv`) honoring the same filters. Columns: settlement id,
provider, owner, period, partner tx/amount/commission, offer tx/payment,
netBalance, walletApplied, outstanding, **current wallet balance**, status,
paidAt, createdAt.

## C6 ✅ Paging + batched wallet balance on `GET /api/offers/GetSettlements`
- Same opt-in pattern: add `search` / `page` / `pageSize` → paged envelope;
  legacy call unchanged (plain array).
- `search` matches owner name or provider id.
- **Every row now includes `currentWalletBalance`** (batched server-side) — you
  can drop the per-row wallet-balance calls (the N+1).

## C7 ✅ Answers (no code needed)
- **Generation cadence:** settlements are **NOT generated on a schedule** — they
  are upserted **in real time** the moment each transaction completes. The old
  monthly job was removed.
- **Period:** weekly, **Sunday–Saturday**; `periodType` is already in the DTO
  (1 = Monthly legacy, 2 = Weekly — everything current is 2), so the UI can stop
  hardcoding `W{week}`.
- **Recompute trigger:** parked — see below.

## C8 ✅ (read side) Wallet history enrichment on `GetWalletHistory`
`WalletTransactionDto` now includes:
- `relatedUserId` + `relatedUserName` — the **customer** behind the wallet entry
  (resolved from the underlying offer/partner transaction)
- `relatedTransactionIds` — structured ids (legacy batch-refund rows are parsed
  from the `"IDs: …"` note text)

Verified live: commission entries resolve to their customers ("Saad", tx [7]);
multi-transaction batches return the id list without a single user chip (they can
span users); entries for expired codes have ids but no user (the user never
scanned — correct).
`canRefund` is intentionally **not returned yet** — see parked items.

---

## Parked — waiting on answers

| Item | Needs |
|---|---|
| **C5 due date** | The grace rule: due = settlement week end + **N days** — what is N? (Will ship as a computed `dueDate` field, no schema change.) |
| **C8 refund action** | Business rules: what exactly gets refunded (points? wallet?), until when, who may do it. Money write — needs sign-off before we build `RefundTransaction` + `canRefund`. |
| **C7 recompute** | Confirm recompute may only touch **Pending/Disputed** periods (Paid is permanently locked — recomputing Paid would break the financial invariant). |

## Deployment
- **Dev:** everything live; `ProviderOffer.PointsPriceValue` column added + backfilled.
- **Prod (at release):** run `Scripts/Offers_Phase1_PointsPriceValue.sql`; everything
  else in this batch is code-only.
