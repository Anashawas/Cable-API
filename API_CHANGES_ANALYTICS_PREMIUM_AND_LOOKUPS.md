# API Changes — Analytics Engine, Premium Stations, Lookups & Reviews

**Date:** 2026-07-03
**Status:** Applied to **DEV** database only (`db_ab1977_cable`). Production requires running the 3 migration scripts (see [Deployment](#deployment--production-migration)).

This release adds 6 features:

| # | Feature | Summary |
|---|---------|---------|
| 1 | [Analytics Engine](#1-analytics-engine) | Track Full/Half views, Call, Map clicks on stations & service providers + Banner view/click, each with timestamp |
| 2 | [Premium Stations](#2-premium-station-payment--expiry-dates) | Payment date + expiry date + payment history for premium stations |
| 3 | [Charger Brand Lookup](#3-charger-brand-lookup) | `ChargerBrand` is now a lookup table instead of free text |
| 4 | [Car Type Logo Icon](#4-car-type-logo-icon) | Car types now have an uploadable logo icon |
| 5 | [Car Model Size](#5-car-model-size) | Car models can be classified (SUV, Hatchback, Sedan, ...) |
| 6 | [Station Reviews](#6-station-reviews) | Ratings now support a text review comment |

---

## How to test in Scalar

1. Run the API and open **`https://localhost:7000/scalar/v1`** (or the server URL + `/scalar/v1`).
2. **Authenticate** (needed for any endpoint marked 🔒):
   - Open the **Users** tag → `POST /api/users/authenticate` → send your email/password.
   - Copy the `accessToken` from the response.
   - Click the **Auth** button (top of Scalar) → choose **Bearer** → paste the token.
3. Find each feature under its tag: **Analytics**, **Charging Points**, **Charger Brands**, **Car Management**, **Rates**, **Banners**.
4. All request/response bodies are **camelCase JSON**. Enums accept either the name (`"fullView"`) or the number (`1`). Dates are returned in **Jordan local time** (existing global converter).

---

## 1. Analytics Engine

One engine records every interaction on stations, service providers and banners. Each event is stored with type, user (when logged in), and timestamp, and is aggregated per day for fast dashboards.

### Event types

| Value | Name | Valid for |
|-------|------|-----------|
| 1 | `FullView` | ChargingPoint, ServiceProvider |
| 2 | `HalfView` | ChargingPoint, ServiceProvider |
| 3 | `CallButtonClick` | ChargingPoint, ServiceProvider |
| 4 | `MapClick` | ChargingPoint, ServiceProvider |
| 20 | `BannerView` | Banner |
| 21 | `BannerClick` | Banner |

Entity types: `"ChargingPoint"`, `"ServiceProvider"`, `"Banner"`.

### `POST /api/analytics/track` — record an event (public, auth optional)

The single front door for all tracking. If the caller is logged in, their `userId` is captured automatically; anonymous callers can pass a device/session id.

```json
{
  "entityType": "ChargingPoint",
  "entityId": 12,
  "eventType": "FullView",
  "anonymousId": "device-abc-123",
  "source": "Android"
}
```

- `anonymousId` (optional, ≤100 chars) — device/session id for unique anonymous tracking.
- `source` (optional, ≤20 chars) — `iOS` / `Android` / `Web`.
- Returns `200 OK` (empty). `404` if the entity doesn't exist, `400` if the event type isn't valid for the entity type (e.g. `BannerClick` on a station).
- A `FullView` on a station/service provider **also increments the legacy `visitorsCount`** automatically.

**Scalar test:** Analytics tag → *Track Analytics Event* → paste the body above → Send → then call the summary endpoint below to see the count.

### 🔒 `GET /api/analytics/{entityType}/{entityId}/summary?from=&to=` — dashboard

Totals, unique users, and a daily time-series. **Providers: the owner OR any Admin (Role 2)** — admins can view any provider's analytics, including unassigned ones. Banners: any authenticated user. Defaults to the last 30 days when `from`/`to` are omitted.

```json
{
  "entityType": "ChargingPoint",
  "entityId": 12,
  "fromUtc": "2026-06-03T10:00:00",
  "toUtc": "2026-07-03T10:00:00",
  "totals": [
    { "eventType": 1, "eventName": "FullView", "totalCount": 240, "uniqueUsers": 87 },
    { "eventType": 3, "eventName": "CallButtonClick", "totalCount": 31, "uniqueUsers": 22 }
  ],
  "daily": [
    { "day": "2026-07-01T00:00:00", "eventType": 1, "eventName": "FullView", "count": 14 }
  ]
}
```

### `POST /api/banners/{id}/view` and `POST /api/banners/{id}/click`

Convenience wrappers for banner analytics — no body needed. Use the summary endpoint with `entityType=Banner` to read view/click totals (CTR = clicks / views).

### ⚠️ Behavior change for mobile

`GET /api/service-providers/{id}` **no longer auto-increments the visitor counter**. The app must now send a `FullView` track call when a profile screen opens (both stations and service providers). The old station endpoint `PATCH /api/charging-points/UpdateChargingPointVisitorsCount/{id}` still works and now records a `FullView` through the engine.

---

## 2. Premium Station Payment & Expiry Dates

Premium station = `StationTypeId 2`. Now every premium placement records **when it was paid** and **when it expires**, with full payment history.

### 🔒 `PATCH /api/charging-points/{id}/premium` — record / renew a premium payment

```json
{
  "paymentDate": "2026-07-01T00:00:00Z",
  "expiresAt": "2026-10-01T00:00:00Z",
  "amount": 150.000,
  "note": "Q3 premium placement - bank transfer #4411"
}
```

Response:

```json
{ "subscriptionId": 3, "paymentDate": "...", "expiresAt": "..." }
```

- Appends a row to the payment history, updates the station's current dates, and **sets the station type to Premium** automatically.
- Call it again to renew — history is preserved.
- Validation: `expiresAt` must be after `paymentDate`; `amount ≥ 0`; `note ≤ 500` chars.

### 🔒 `GET /api/charging-points/{id}/premium-history`

```json
{
  "chargingPointId": 12,
  "currentPaymentDate": "2026-07-01T00:00:00",
  "currentExpiresAt": "2026-10-01T00:00:00",
  "isPremiumActive": true,
  "history": [
    { "id": 3, "paymentDate": "...", "expiresAt": "...", "amount": 150.000, "note": "...", "createdAt": "...", "createdBy": 5 }
  ]
}
```

### Also

- `GET /api/charging-points/GetChargingPointById/{id}` now returns `premiumPaymentDate` and `premiumExpiresAt` (null for non-premium stations).
- **Not yet implemented (by decision):** auto-downgrade when the date passes. Dates are informational for now; the expiry job can be added later.

---

## 3. Charger Brand Lookup

`ChargingPoint.ChargerBrand` free text is **removed** and replaced by a **`ChargerBrand` lookup table** + a many-to-many junction with per-brand counts. Existing brand values were migrated automatically and the free-text column was dropped — the junction is the only brand storage.

### New endpoints — tag **Charger Brands**

| Method | Route | Auth | Notes |
|--------|-------|------|-------|
| GET | `/api/charger-brands/GetAllChargerBrands` | — | For dropdowns: `[ { "id": 1, "name": "ABB" } ]` |
| POST | `/api/charger-brands/AddChargerBrand` | 🔒 | Body `{ "name": "Tesla" }` — duplicates rejected |
| PUT | `/api/charger-brands/UpdateChargerBrand/{id}` | 🔒 | Renames the brand — stations pick the new name up automatically (join) |
| DELETE | `/api/charger-brands/DeleteChargerBrand/{id}` | 🔒 | Blocked with `400` while any station uses the brand |

### Using it on charging points — MANY-TO-MANY with per-brand counts

A station can have **multiple charger brands, each with how many chargers of
that brand** (e.g. 4× ABB + 3× Teison + 2× Bene). `AddChargingPoint` /
`UpdateChargingPoint` accept:

```json
{ "chargerBrands": [ { "chargerBrandId": 1, "count": 4 },
                     { "chargerBrandId": 2, "count": 3 } ] }
```

Rules:
- On update the set is **replaced** (same semantics as `plugTypeIds`).
- **`chargersCount` is auto-summed** from the counts whenever `chargerBrands`
  is provided (any value sent for `chargersCount` is overridden).
- `chargerBrands` is the ONLY way to set brands. **The old free-text
  `chargerBrand` field is GONE** — no longer accepted in any request and no
  longer returned in any response (build the display string from the array).
- Unknown brand ids → `400`; duplicate brand entries are merged; counts must be > 0.

`GET .../GetChargingPointById/{id}` returns:

```json
{ "chargersCount": 9,
  "chargerBrands": [ { "id": 1, "name": "bene", "count": 4 },
                     { "id": 2, "name": "Charger", "count": 3 },
                     { "id": 4, "name": "Teison", "count": 2 } ] }
```

The stations-list `chargerBrandId` filter matches stations that have that brand
**among** their brands. Deleting a brand is blocked while any station uses it.

---

## 4. Car Type Logo Icon

### 🔒 `POST /api/carmanagement/UploadCarTypeIcon/{id}` — multipart upload

- Form field name: **`file`** (jpg/jpeg/png, same size limits as other uploads).
- Replacing an icon deletes the old file automatically.

**Scalar test:** Car Management tag → *Upload car type icon* → choose a file in the form body → Send.

### Reading it

`GET /api/carmanagement/GetAllCarTypes` and `GetAllCarModels` now return the full URL:

```json
{ "id": 1, "name": "Tesla", "iconUrl": "https://server/files/CableCarTypes/xxx.png" }
```

---

## 5. Car Model Size

New **`CarModelSize`** lookup, seeded with: **SUV, Hatchback, Sedan, Crossover, Coupe, Pickup, Van**.

### `GET /api/carmanagement/GetAllCarModelSizes`

```json
[ { "id": 1, "name": "SUV" }, { "id": 2, "name": "Hatchback" } ]
```

### Using it on car models

`POST /api/carmanagement/AddCarModel` and `PUT /api/carmanagement/UpdateCarModel/{id}` accept an optional `sizeId`:

```json
{ "name": "Model Y", "carTypeId": 1, "sizeId": 1 }
```

`GET /api/carmanagement/GetAllCarModels` now returns per model:

```json
{ "id": 7, "name": "Model Y", "sizeId": 1, "sizeName": "SUV" }
```

Invalid `sizeId` → `404`. Omitting it leaves the model unclassified (`null`).

---

## 6. Station Reviews

Station ratings (`Rate`) now carry an optional **text review**, matching the existing service-provider review pattern.

### Writing a review

🔒 `POST /api/rate/AddRate`:

```json
{ "chargingPointId": 12, "chargingPointRate": 5, "comment": "Fast charger, clean location" }
```

🔒 `PATCH /api/rate/UpdateRate/{id}`:

```json
{ "chargingPointRate": 4, "comment": "Updated: one plug was broken" }
```

`comment` is optional, max 1000 chars. Rating-only requests keep working unchanged.

### `GET /api/rate/GetChargingPointReviews/{id}` — read reviews (public)

```json
{
  "chargingPointId": 12,
  "averageRating": 4.33,
  "totalReviews": 3,
  "reviews": [
    { "id": 9, "userId": 4, "userName": "Ali", "rating": 5, "comment": "Fast charger...", "createdAt": "2026-07-02T14:20:00" }
  ]
}
```

Reviews are ordered newest first. Ratings without a comment appear with `"comment": null`.

---

## 7. Station Owner Exposure (admin request)

The admin can reassign a station's owner (`PATCH /api/charging-points/ChangeOwner/{id}`)
but previously couldn't *see* ownership anywhere. Now:

### List — `POST /api/charging-points/GetAllChargingPoints`

Each station row includes a new flag:

```json
{ "id": 219, "name": "EV Station", "hasOwner": true }
```

`hasOwner: false` = the station has no valid owner account (unassigned/orphaned) —
use it to flag stations needing assignment without bloating the list payload.

### Detail — `GET /api/charging-points/GetChargingPointById/{id}`

Four new fields with the owner **account** details:

```json
{
  "ownerId": 8296,
  "ownerName": "Hamzeh Bani-Issa",
  "ownerEmail": "hamzeh@example.com",
  "ownerAccountPhone": "9627XXXXXXXX"
}
```

- `ownerId` is nullable — `null` = unassigned.
- ⚠️ Note the distinction: the **existing** `ownerPhone` field is the free-text
  contact phone stored on the station itself; the **new** `ownerAccountPhone` is
  the phone on the owner's user account. They can differ.
- Powers the station view/edit screen and lets the Change Owner dialog show who
  currently owns the station before reassigning.

### Unassign / reassign owners (stations AND service providers)

`OwnerId` is now truly nullable — a station or service provider can exist with
**no owner** ("unassigned"). Both change-owner endpoints accept `null`:

```
PATCH /api/charging-points/ChangeOwner/{id}      body: { "newOwnerId": null }   → unassign
PATCH /api/service-providers/ChangeOwner/{id}    body: { "newOwnerId": null }   → unassign
PATCH .../ChangeOwner/{id}                       body: { "newOwnerId": 6046 }   → assign/reassign
```

Rules enforced:
- **Unassign is blocked** while the provider has **active offers or active partner
  agreements** → `400 "Cannot unassign: this provider has X active offer(s) and Y
  active partner agreement(s). Deactivate them first."` (prevents zombie offers and
  ownerless commission flows)
- Unassigning an already-unassigned provider → `400`
- Assigning still requires the new owner to have the **Provider role**
- **Creating a partner agreement on an unassigned provider is blocked** → `400`
- Unassigned providers appear in nobody's "my stations/providers" lists; nobody can
  submit update requests or manage workers for them

Also in this change:
- `AddChargingPoint` / `CreateServiceProvider` accept an optional **`ownerId`** so
  an admin can create on behalf of a provider (defaults to the caller as before)
- 🐛 **Bug fix:** `UpdateChargingPoint` previously **reassigned the station to
  whoever edited it** on every update — ownership is now only changed via
  `ChangeOwner`

**DB migration required:** `Scripts/NullableOwner_Phase1.sql` (makes
`ChargingPoint.OwnerId`, `ServiceProvider.OwnerId` and
`ProviderWalletTransaction.RecordedByUserId` nullable). Already applied on dev.

---

## 8. Optional Pagination — API-wide

Every meaningful **list endpoint** now supports opt-in pagination:

- **Without** `page`/`pageSize` → the response is **exactly what it was before**
  (plain array) — no client changes needed anywhere.
- **With** `?page=1&pageSize=25` (query string on GETs, body fields on POST-body
  endpoints) → the standard envelope:
  ```json
  { "items": [...], "totalCount": 180, "page": 1, "pageSize": 25,
    "totalPages": 8, "hasNextPage": true, "hasPreviousPage": false }
  ```
- `pageSize` max 500 (loyalty admin ledgers: 200). Defaults to 20 when only `page` is sent.

Covered: offers (active/pending/for-provider/transactions ×2), wallet history,
provider settlements, partners (active/agreements/transactions ×2), loyalty
(rewards ×2, redemptions ×3, blocked lists, reward performance, fraud queue),
service providers (all/by-category/nearby/my/favorites), stations
(all/by-user/my), update requests ×2, users, settlements, banners, emergency
services.

NOT paged (by design): tiny lookup lists (plug types, brands, categories, car
types/sizes, statuses, tiers, seasons, conversion rates) and mobile endpoints
that already paginate with a plain-array contract (`GetMyPointsHistory`).

---

## Deployment — Production Migration

The API code expects the new tables/columns. **Run these scripts on `db_ab1977_cableproduction` before or together with the deploy** (all idempotent — safe to re-run):

1. `Scripts/Analytics_Phase1_CreateTables.sql` — analytics tables
2. `Scripts/Premium_Phase1_AddStationPremium.sql` — premium columns + history table
3. `Scripts/Lookups_Phase1_BrandSizeIconReviews.sql` — brand & size lookups (also seeds/backfills brands from production's own data), car type icon, rate comment

### Database objects added

| Object | Type | Feature |
|--------|------|---------|
| `AnalyticsEvent` | table | Analytics raw log |
| `AnalyticsDailyRollup` | table | Analytics daily counters |
| `StationPremiumSubscription` | table | Premium payment history |
| `ChargerBrand` | table | Brand lookup (seeded from data) |
| `CarModelSize` | table | Size lookup (seeded: 7 sizes) |
| `ChargingPoint.PremiumPaymentDate`, `PremiumExpiresAt`, `ChargerBrandId` | columns | Premium + brand |
| `CarType.Icon` | column | Logo icon |
| `CarModel.SizeId` | column | Model size |
| `Rate.Comment` | column | Reviews |

### Mobile integration checklist

- [ ] Call `POST /api/analytics/track` with `FullView` when a station/service-provider profile opens (**required** — service-provider views are no longer counted automatically)
- [ ] Wire `HalfView`, `CallButtonClick`, `MapClick`, `BannerView`, `BannerClick` events
- [ ] Switch brand selection to the `GetAllChargerBrands` dropdown and send `chargerBrandId`
- [ ] Show `iconUrl` on car type selection screens
- [ ] Offer size selection (`GetAllCarModelSizes`) in admin model management; show `sizeName` on cars
- [ ] Add the review text box to the rating screen (`comment`) and the reviews list (`GetChargingPointReviews`)
- [ ] Provider app: show premium status via `premiumExpiresAt` / `premium-history`
