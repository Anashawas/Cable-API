# Sprint 5 — Cable Gifts and promo codes (backend)

Implements `CABLE_GIFTS_AND_PROMOS.md` (2026-10-27) with the default decisions of its §5.
SQL: `Scripts/Sprint5_GiftsAndPromos.sql` (idempotent, apply to dev first).

General rules

- All texts (gift / promo names, descriptions, error messages, pushes) come from the server in the request language
  (`Accept-Language`, Arabic by default; pushes use the device language saved with the FCM token).
- Errors: `400` validation (`errors`), `404` not found, `409` with `code`, `422` with `code` and a ready-to-show `detail`.
- Dates are stored UTC and returned in Jordan time. Admin dates sent without an offset are read as Jordan time.
- The apps never compute a number: discounts, final amounts, points, "points missing", "balance after", "expires in N days"
  all come from the server.
- Enum fields are strings in JSON (`"giftType": "Physical"`); query `status=` accepts the name (any case) or the number.

---

## 1. Cable Gifts

### 1.1 User app

| Method | Path | |
|---|---|---|
| GET | `/api/cable-gifts` | Active gifts for the user's city |
| GET | `/api/cable-gifts/{id}` | One gift |
| POST | `/api/cable-gifts/{id}/order` | Buy (header `Idempotency-Key`) |
| GET | `/api/cable-gifts/my-orders?status=&page=&pageSize=` | "My gifts", paged (default 20) |

List item / details:

```jsonc
{
  "id": 12, "name": "كاسة كيبل", "description": "…", "howToReceive": "…",
  "giftType": "Physical", "pointsCost": 300, "imageUrl": "https://…/CableGifts/….png",
  "remaining": 14,                 // null = unlimited
  "startsAt": null, "endsAt": "2026-12-31T23:59:00",
  "myOrdersCount": 0, "perUserLimit": 1,
  "canOrder": false,
  "cannotOrderReason": "INSUFFICIENT_POINTS",   // or null
  "cannotOrderMessage": "نقاطك ما بتكفي",
  "myBalance": 260,
  "pointsMissing": 40,             // "ناقصك 40 نقطة"
  "balanceAfterOrder": null        // "رصيدك بعد الشراء" when affordable
}
```

Out-of-stock gifts stay in the list (`remaining: 0`, `cannotOrderReason: "OUT_OF_STOCK"`) so the card can be greyed out.
A gift limited to some cities is shown only to users of those cities (user city unknown → only city-free gifts).

Order:

```jsonc
// POST /api/cable-gifts/12/order
// Header: Idempotency-Key: 6f1c0e9e-…   (one UUID per tap; a retry with the same key returns the same order)
{ "contactPhone": "0791234567", "note": "بستلم من عمّان" }   // body optional; contactPhone defaults to the account phone

// 200
{ "orderId": 88, "orderCode": "GFT-7K2M9Q", "status": "Pending", "pointsSpent": 300, "newBalance": 140 }
```

`422` codes (`code` field):

| code | message (ar) |
|---|---|
| `INSUFFICIENT_POINTS` | نقاطك ما بتكفي — also `pointsCost`, `balance`, `pointsMissing` |
| `OUT_OF_STOCK` | نفدت الكمية |
| `LIMIT_REACHED` | استبدلت هاي الهدية قبل |
| `GIFT_EXPIRED` | انتهى وقت هاي الهدية |
| `GIFT_NOT_AVAILABLE` | الهدية غير متاحة حالياً (inactive, not started, other city) |
| `LOYALTY_BLOCKED` | حسابك موقوف عن الولاء مؤقتاً |

Server guarantees: points, stock and the per-user limit are checked and changed in one DB transaction with row locks on
the gift and the wallet — the last piece can't be sold twice. The purchase writes a points-history line
(`GetMyPointsHistory`, action `GIFT_PURCHASE`, type Redeem, `referenceType: "GiftOrder"`).

My gifts item:

```jsonc
{ "orderId": 88, "orderCode": "GFT-7K2M9Q", "giftId": 12, "giftName": "كاسة كيبل", "giftImageUrl": "…",
  "giftType": "Voucher", "pointsSpent": 300, "status": "Fulfilled",       // Pending | Fulfilled | Cancelled
  "contactPhone": "0791234567", "note": "…",
  "fulfillmentNote": "WASH-1234",   // only when Fulfilled (note or voucher code → copy button)
  "fulfilledAt": "…", "cancelReason": null, "cancelledAt": null,
  "pointsRefunded": false,          // true for Cancelled
  "createdAt": "…" }
```

Pushes (FCM + inbox, `data.type = "cable_gift_order"`, `orderId`, `status`): when an admin fulfils and when he cancels.

### 1.2 Admin

| Method | Path | Body / query |
|---|---|---|
| GET | `/api/admin/cable-gifts` | `isActive, search, page, pageSize` (envelope only with page/pageSize) |
| GET | `/api/admin/cable-gifts/{id}` | |
| POST | `/api/admin/cable-gifts` | gift (below) → `{ "id": 12 }` |
| PUT | `/api/admin/cable-gifts/{id}` | gift |
| PATCH | `/api/admin/cable-gifts/{id}/active` | `{ "isActive": false }` |
| POST | `/api/admin/cable-gifts/{id}/image` | multipart, key `files`, jpg/png/webp ≤ 5 MB → `{ "imageUrl": "…" }` |
| GET | `/api/admin/cable-gifts/orders` | `status, giftId, from, to, search, page, pageSize` (Jordan dates, `to` inclusive) |
| GET | `/api/admin/cable-gifts/orders/export` | same filters → CSV, UTF-8 with BOM |
| GET | `/api/admin/cable-gifts/orders/pending-count` | → `{ "pendingCount": 3 }` (dashboard) |
| PATCH | `/api/admin/cable-gifts/orders/{id}/fulfill` | `{ "fulfillmentNote": "…" }` |
| PATCH | `/api/admin/cable-gifts/orders/{id}/cancel` | `{ "reason": "…" }` → refunds the points |

Gift body:

```jsonc
{ "nameAr": "كاسة كيبل", "nameEn": "Cable mug",
  "descriptionAr": "…", "descriptionEn": "…", "howToReceiveAr": "…", "howToReceiveEn": "…",
  "giftType": "Physical", "pointsCost": 300,
  "stock": 20, "perUserLimit": 1,                         // null = unlimited
  "startsAt": "2026-11-01T00:00", "endsAt": "2026-12-31T23:59",
  "cities": ["Amman", "إربد"],                           // resolved to canonical names; [] = all
  "sortOrder": 0, "isActive": true }
```

Rules: `pointsCost > 0`; the stock can't be set below the pieces already ordered; unknown city → 400.
Fulfil / cancel only a `Pending` order, else `409 ORDER_NOT_PENDING`. Cancel returns all points to the wallet
(history line "استرجاع — إلغاء هدية", action `GIFT_REFUND`) and gives the piece back to stock.

---

## 2. Promo codes

### 2.1 Model in short

- `PromoCampaign` — effect (`DiscountPercent` 1–100, `DiscountFixed` JOD, `PointsMultiplier` >1–10, `BonusPoints`),
  `maxDiscountJod`, `cableSharePercent` (0–100), `cableBudgetJod`, scope (stations / service providers: `None | All | Selected`
  + ids, service categories, provider cities, `minAmountJod`), dates, `validDaysAfterClaim`, `totalLimit`, `perUserLimit`,
  `newUsersOnly`, `codeMode` `Public` (admin code, e.g. `CABLE20`) or `Unique` (`PRM-` + 8 chars without 0 O 1 I L).
- `UserPromo` — "My codes" (`Available | Used | Expired | Revoked`), single use.
- `PromoUsage` — one row per use with the snapshot of amounts, the Cable / partner split, commission before / after, points.

### 2.2 User app

| Method | Path | |
|---|---|---|
| POST | `/api/promos/claim` | `{ "code": "cable 20" }` (case-insensitive; spaces and dashes ignored) |
| GET | `/api/promos/my?status=Available\|Used\|Expired` | "My codes" tab (default Available) |
| GET | `/api/promos/my/count` | `{ "available": 2 }` |

Claim / My codes card:

```jsonc
{ "userPromoId": 31, "campaignId": 4, "title": "غسيل مجاني", "description": "…",
  "effectType": "DiscountPercent", "effectValue": 100, "maxDiscountJod": 5,
  "scope": "عند مغاسل إربد",
  "status": "Available", "source": "Code",              // Code | Assigned
  "claimedAt": "…", "expiresAt": "2026-11-26T23:59:00",
  "expiresInDays": 29, "expiringSoon": false,           // expiringSoon = within 2 days
  "usedAt": null, "usedAtProviderName": null, "savedJod": null, "bonusPoints": null }   // filled on Used cards
```

Claim errors (`422`):

| code | message (ar) |
|---|---|
| `PROMO_NOT_FOUND` | هذا الكود غير صحيح (counts as a wrong attempt) |
| `PROMO_EXPIRED` | هذا الكود منتهي الصلاحية |
| `PROMO_ALREADY_CLAIMED` | الكود موجود عندك بأكوادي |
| `PROMO_LIMIT_REACHED` | انتهت الكمية المتاحة من هذا العرض (totalLimit or budget used up) |
| `PROMO_NOT_ELIGIBLE` | هذا العرض غير متاح لحسابك (newUsersOnly, or a revoked one) |
| `PROMO_NOT_STARTED` | العرض لسا ما بلّش |
| `PROMO_ALREADY_USED` | هذا الكود مستعمل (unique code taken by someone else / per-user limit used) |
| `TOO_MANY_ATTEMPTS` | 10 wrong codes in the last hour; `retryAfterMinutes: 60` |

### 2.3 Promo at scan time

```jsonc
// GET /api/partners/PreviewPartnerCode?code=PTR-8KQ2M4
{ "transactionAmount": 4.000, "basePoints": 40, "multiplier": null, "pointsToBeAwarded": 40, /* … unchanged fields … */
  "eligiblePromos": [
    { "userPromoId": 31, "title": "غسيل مجاني", "effectType": "DiscountPercent", "effectValue": 100,
      "maxDiscountJod": 5, "expiresAt": "…", "discountJod": 4.000, "finalAmountJod": 0.000, "bonusPoints": 0 },
    { "userPromoId": 35, "title": "نقاط ×2", "effectType": "PointsMultiplier", "effectValue": 2,
      "maxDiscountJod": null, "expiresAt": "…", "discountJod": 0, "finalAmountJod": 4.000, "bonusPoints": 40 } ] }

// GET /api/partners/PreviewPartnerCode?code=PTR-8KQ2M4&userPromoId=31
{ "transactionAmount": 4.000,                         // original amount
  "promo": { "userPromoId": 31, "title": "غسيل مجاني", "effectType": "DiscountPercent",
             "discountJod": 4.000, "finalAmountJod": 0.000, "bonusPoints": 0 },
  "basePoints": 0, "pointsToBeAwarded": 0, "multiplier": null,
  "pointsAvailable": false, "pointsUnavailableReason": "fullyDiscounted", /* … */ }

// POST /api/partners/ScanPartnerCode?code=PTR-8KQ2M4&userPromoId=31
//   or body { "code": "PTR-8KQ2M4", "userPromoId": 31 }  (the old ?code= call is unchanged)
{ "providerName": "…", "transactionAmount": 4.000, "currencyCode": "JOD", "commissionAmount": 0, "pointsAwarded": 0,
  "promo": { "userPromoId": 31, "title": "غسيل مجاني", "discountJod": 4.000, "finalAmountJod": 0.000, "bonusPoints": 0 } }
```

Scan errors (`422`): `PROMO_NOT_APPLICABLE`, `PROMO_ALREADY_USED`, `PROMO_EXPIRED`, `PROMO_BUDGET_EXHAUSTED`
("انتهى رصيد هذا العرض"), `PROMO_NOT_FOUND` (not the user's promo). The promo becomes `Used` in the same DB transaction
as the completion — if the scan fails (expired PTR code, loyalty block, any error) it stays `Available`.
Without `userPromoId` nothing changes in the scan.

Partner side — `GET /api/partners/provider/GetTransactionById/{id}` and `GET /api/partners/provider/GetProviderTransactions`
now carry, for a transaction with a promo:

```jsonc
"promo": { "title": "غسيل مجاني", "originalAmountJod": 4.000, "discountJod": 4.000,
           "finalAmountJod": 0.000,     // "collect from the customer"
           "cableShareJod": 4.000,      // "Cable pays you"
           "providerShareJod": 0.000 }
```

Settlements (`GetProviderSettlement(s)`, `GetSettlements`): new field `promoDiscountsPaidByCable` — the line
"خصومات تتحمّلها كيبل" (sum of `cableShareJod`), already included in `netBalance` (owed by Cable to the partner).

Admin settlement exports (leftovers): the settlements CSV (`GET /api/offers/GetSettlementsCsv`, same filters) has a new last
column `PromoDiscountsPaidByCable`; the line items of one settlement (`GET /api/offers/GetSettlementTransactions?settlementId=`) carry
`promoDiscountsPaidByCable` on the settlement and, per partner line with a promo, `promoTitle` (request language),
`promoDiscountJod`, `promoCableShareJod`, `promoProviderShareJod` (null on lines without a promo). Existing fields unchanged.

Boost caps (leftovers): points a `BonusPoints` promo adds on a boosted scan are the promo's — they no longer count toward
the boost's `maxBonusPointsPerUser` / `maxTotalBonusPoints`, nor its "spent" figure in the admin boosts list. Only the
boost's own extra points (awarded − base − promo bonus) do.

Preview at a bundle partner whose bundle is used up (leftovers): `basePoints` is 0 like `pointsToBeAwarded`, and the promo
quotes (`eligiblePromos`, `promo`) start from 0 base points as the scan will (a `BonusPoints` promo still adds its
bonus); `pointsUnavailableReason` stays `partnerQuotaReached`.

Server rules (§2.9):

| | |
|---|---|
| Discount | `DiscountPercent`: amount × value / 100; `DiscountFixed`: value; capped by `maxDiscountJod` and the amount; rounded to the fils |
| Split | `cableShareJod = discount × cableSharePercent / 100`, the rest `providerShareJod` |
| Points | on the final amount (100 % off → 0 from the transaction); `BonusPoints` added on top |
| Multiplier | promo multiplier vs boost / welcome bonus → the higher, never multiplied |
| Commission | on `finalAmount + cableShare`; the difference to the reserved commission goes back to the partner's wallet (`WalletTransactionType.PromoCommissionRefund = 9`) |
| Budget | `cableSpentJod + cableShare > cableBudgetJod` → `PROMO_BUDGET_EXHAUSTED` and the campaign is switched off (`deactivatedReason: "BudgetExhausted"`); it also switches off when a use reaches the budget exactly |
| Scope | the partner must be in the scope of its type (stations / service providers); then categories (service providers only), provider city, `minAmountJod` |

### 2.4 Admin

| Method | Path | Body / query |
|---|---|---|
| GET | `/api/admin/promos` | `isActive, search, page, pageSize` |
| GET | `/api/admin/promos/{id}` | |
| POST | `/api/admin/promos` | campaign (below) → `{ "id": 4 }` |
| PUT | `/api/admin/promos/{id}` | campaign |
| PATCH | `/api/admin/promos/{id}/active` | `{ "isActive": true }` |
| POST | `/api/admin/promos/{id}/codes` | `{ "count": 100 }` (1–5000, Unique mode) → `{ generated, totalCodes, batchId }` |
| GET | `/api/admin/promos/{id}/codes/export` | CSV (UTF-8 BOM): code, claimed by, when, promo status |
| POST | `/api/admin/promos/{id}/assign` | `{ "target": "All\|City\|Users", "cities": [], "userIds": [], "phones": [] }` → `{ assigned, skipped, batchId }` + push |
| GET | `/api/admin/promos/{id}/report` | claims, assigned, available / used / expired / revoked, uses, total discount, Cable / partners share, bonus points, budget left, per partner |
| GET | `/api/admin/user-promos` | `userId, campaignId, status, page, pageSize` (user profile → codes) |
| PATCH | `/api/admin/user-promos/{id}/revoke` | `{ "reason": "…" }` — available only, else `409 PROMO_NOT_AVAILABLE` |

Campaign body:

```jsonc
{ "titleAr": "غسيل مجاني", "titleEn": "Free wash", "descriptionAr": "…", "descriptionEn": "…",
  "scopeLabelAr": "عند مغاسل إربد", "scopeLabelEn": "At Irbid car washes",   // optional card text
  "cableSharePercent": 100, "cableBudgetJod": 500,
  "stationsScope": "None", "stationIds": [],
  "serviceProvidersScope": "Selected", "serviceProviderIds": [7, 9],
  "effectType": "DiscountPercent", "effectValue": 100, "maxDiscountJod": 5,
  "appliesTo": ["PartnerScan"], "serviceCategoryIds": [2], "cities": ["Irbid"], "minAmountJod": null,
  "startsAt": "2026-11-01T00:00", "endsAt": "2026-12-31T23:59",
  "validDaysAfterClaim": 30, "totalLimit": 1000, "perUserLimit": 1, "newUsersOnly": false,
  "codeMode": "Public", "publicCode": "WASH100", "isActive": true }
```

Validation: a discount with `cableSharePercent < 100` can't use `All` for stations or providers (400 on
`StationsScope` / `ServiceProvidersScope`); a discount Cable pays part of needs `cableBudgetJod` (and `maxDiscountJod` for a
percent); after the first use the effect (type, value, cap, Cable's share) is locked; the code mode is locked once codes
exist; the budget can't go below what was spent; public codes are 3–30 letters/digits, unique, not starting with
PRM / PTR / GFT / CBL.

Pushes: `promo_assigned` on assignment; `promo_expiring` once per promo, daily job `promo-expiry` (06:00 UTC), which
also marks past-date promos `Expired`.

---

## 3. Decisions taken in the code (beyond §5)

1. **Cities are names, not ids** (`cities: ["Amman"]`): Cable has no City table; names go through `JordanCityResolver`
   (Arabic or English accepted, canonical English stored). Gifts: the user's city. Promos: the partner's city at scan.
2. **totalLimit** caps claims by code; admin assignments are not counted against it (the budget still protects money).
3. **perUserLimit** counts every promo the user had of the campaign (used, expired unused, revoked), so a public code
   can't be re-added to renew `validDaysAfterClaim`.
4. **New user** = account created within the last 30 days.
5. **Cancelled gift orders give the piece back to stock.** Only `Pending` orders can be fulfilled or cancelled.
6. **The user's scan result** shows `{ title, discountJod, finalAmountJod, bonusPoints }` only — the Cable / partner split is
   commercial, like the commission, and is shown to the partner (transaction endpoints) and the admin (report).
7. **`ScanPartnerCode`** accepts `userPromoId` as query or JSON body; older calls (`?code=` with no body) are untouched.
8. **Points multiplier promo vs boost**: when the promo multiplier wins, the boost / welcome bonus is not attributed to the
   transaction (`AppliedBoostId`, `IsWelcomeBonus` cleared). A `BonusPoints` promo on a boosted scan adds its points on top of
   the boost.
9. **Category scope** applies to service providers only — a station is in no category, so a campaign with categories never
   applies to a station.
10. **Wrong-code limit** returns `422 TOO_MANY_ATTEMPTS` (no 429 facility in the API).

---

## 4. Dev test steps (§4 of the document)

Prepare: run `Scripts/Sprint5_GiftsAndPromos.sql` on dev; a test user (see `Docs/dev-test-accounts.local.md`), an admin
token, and a test partner with an active agreement.

1. **Gift**: `AdjustPoints` +500 to the user → create a gift (300 points, stock 5) → `POST /api/cable-gifts/{id}/order`
   → balance 200, history line "شراء هدية". Admin `PATCH …/orders/{id}/cancel` → balance 500 again, history line
   "استرجاع — إلغاء هدية", push received. Order again → `…/fulfill` with `"fulfillmentNote": "WASH-1234"` → the note shows in
   `my-orders`, push received. Repeat an order with the same `Idempotency-Key` → same `orderId`, balance unchanged.
2. **Last piece**: stock 1, two orders at the same moment (two users, or the same user with two keys) → exactly one 200,
   the other `422 OUT_OF_STOCK`.
3. **100 % wash, Cable 100 %**: campaign `DiscountPercent 100`, `maxDiscountJod 5`, `cableSharePercent 100`, budget 50,
   providers Selected = the test partner. Claim the code, partner creates a 4.000 PTR code → preview with `userPromoId`:
   `finalAmountJod 0.000`, `pointsToBeAwarded 0`. Scan → partner `GetTransactionById`: `finalAmountJod 0.000`,
   `cableShareJod 4.000`; the settlement shows `promoDiscountsPaidByCable 4.000` (and netBalance +4.000); commission
   unchanged (on 4.000).
   - **3b. 50 %**: same at `cableSharePercent 50` → `cableShareJod 2.000`, settlement 2.000, commission on 2.000 and a
     `PromoCommissionRefund` wallet line for the difference.
   - **3c. Budget**: budget 5, two 4.000 scans by two users (or `perUserLimit 2` and two claims) → the second preview /
     scan returns `PROMO_BUDGET_EXHAUSTED` and the campaign
     shows `isActive: false, deactivatedReason: "BudgetExhausted"`.
4. **×2 with a welcome bonus**: first scan of a new user (welcome 2×) with a `PointsMultiplier 3` promo → 3× (not 6×);
   with a `PointsMultiplier 2` promo → 2× (bonusPoints 0).
5. **Failed scan**: let the PTR code expire, scan with `userPromoId` → `expired` error, the promo is still in
   `my?status=Available`.
6. **Limits**: a used code (`PROMO_ALREADY_USED` at scan / claim), an expired code (`PROMO_EXPIRED`), a promo limited to
   another city (not in `eligiblePromos`, `PROMO_NOT_APPLICABLE` when forced), 11 wrong codes in an hour → the 11th
   `TOO_MANY_ATTEMPTS`.
7. **Scope rule**: create a discount with `cableSharePercent 50` and `stationsScope: "All"` → 400.
8. **No promo = no change**: a scan without `userPromoId` gives the same points, commission and settlement as before.
