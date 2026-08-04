# Charger Brands — Many-to-Many Redesign

**Date:** 2026-07-12
**Status:** ✅ Implemented, live-tested on dev, dev DB migrated. Prod needs one script before deploy (see [Deployment](#deployment)).

---

## 1. What changed (summary)

Charger brands moved from a **free-text field** on the station to a proper
**lookup table + many-to-many relation with per-brand charger counts**.

A station can now have multiple brands, each with how many chargers of that
brand — e.g. *4× bene + 3× Charger + 2× Teison*.

| Before | After |
|--------|-------|
| `ChargingPoint.ChargerBrand` free text ("Teison") | ❌ **Removed** (column dropped) |
| `chargerBrandId` single-FK write field (transitional) | ❌ **Removed** (never reached prod) |
| — | ✅ `ChargerBrand` lookup table (id + unique name) |
| — | ✅ `ChargingPointChargerBrand` junction: station ⇄ brand + `count` |
| `chargersCount` typed by hand | ✅ **Auto-summed** from the brand counts when brands are sent |

---

## 2. Data model

```
ChargerBrand                      -- lookup
  Id            INT PK
  Name          NVARCHAR(100) UNIQUE

ChargingPointChargerBrand         -- junction (the ONLY brand storage)
  Id              INT PK
  ChargingPointId INT FK -> ChargingPoint (CASCADE on delete)
  ChargerBrandId  INT FK -> ChargerBrand
  Count           INT (chargers of this brand at this station)
  UNIQUE (ChargingPointId, ChargerBrandId)
```

`ChargingPoint.ChargerBrand` (free text) **no longer exists** — not in the
database, not in any request, not in any response.

---

## 3. New endpoints — brand lookup CRUD (tag: Charger Brands)

| Method | Route | Auth | Notes |
|--------|-------|------|-------|
| GET | `/api/charger-brands/GetAllChargerBrands` | — | For dropdowns: `[ { "id": 1, "name": "bene" } ]` |
| POST | `/api/charger-brands/AddChargerBrand` | 🔒 | Body `{ "name": "Tesla" }` — trimmed, duplicates → 400 |
| PUT | `/api/charger-brands/UpdateChargerBrand/{id}` | 🔒 | Rename; stations pick the new name up automatically (join) |
| DELETE | `/api/charger-brands/DeleteChargerBrand/{id}` | 🔒 | Blocked with 400 while any (non-deleted) station uses the brand |

---

## 4. Changed station endpoints

### 4.1 `POST /api/charging-points/AddChargingPoint` and `PUT /api/charging-points/UpdateChargingPoint/{id}`

Send brands (optionally) as an array:

```json
{
  "...": "all the normal station fields",
  "chargerBrands": [
    { "chargerBrandId": 1, "count": 4 },
    { "chargerBrandId": 2, "count": 3 }
  ]
}
```

Rules:
- `chargerBrands` is the **only** way to set brands.
- On **update**, when the array is provided the brand set is **fully replaced**
  (same semantics as `plugTypeIds`). When omitted/null/empty → junction untouched.
- **`chargersCount` is auto-summed** from the counts whenever `chargerBrands`
  is provided; any value sent for `chargersCount` in the same request is ignored.
  When brands are not sent, `chargersCount` from the request is used as before.
- Unknown brand ids → 400 naming the missing ids.
- Duplicate entries for the same brand are merged (counts added); counts ≤ 0 are dropped.
- Station data + brand rows commit in **one transaction**.

### 4.2 `GET /api/charging-points/GetChargingPointById/{id}`

```json
{
  "chargersCount": 7,
  "chargerBrands": [
    { "id": 1, "name": "bene",    "count": 4 },
    { "id": 2, "name": "Charger", "count": 3 }
  ]
}
```

### 4.3 Stations list (`POST /api/charging-points/GetAllChargingPoints`, paged variant)

The `chargerBrandId` **filter** matches stations having that brand **among**
their brands (EXISTS on the junction).

---

## 5. ⚠️ Breaking changes for clients (mobile / portal)

1. **`chargerBrand` (free text) is GONE** from every response (list + ById)
   and is no longer accepted in any request. Build display strings from the
   `chargerBrands` array (e.g. join the names).
2. **`chargerBrandId` single write field is GONE** from Add/Update station —
   use the `chargerBrands` array. (The list *filter* `chargerBrandId` still works.)
3. `chargersCount` becomes read-only in practice whenever brands are sent —
   the server computes it.
4. The provider **update-request flow no longer carries a brand field**
   (it only ever carried the free text). Brand changes are done through
   `UpdateChargingPoint` directly.

---

## 6. 🐞 Bug fixed on the way: submit-update-request 500

`POST /api/provider/charging-points/submit-update-request/{id}` returned
**500 "An error occurred while saving the entity changes"** on the deployed API.

- **Root cause:** the `ChargingPointUpdateRequest` *entity* declared a
  `ChargerBrand` property but the *table* never had that column (dev **and**
  prod) — every EF INSERT failed with *Invalid column name*. The endpoint had
  **never worked in any environment** (0 rows ever in both databases).
- **Fix:** removing the phantom property (part of this redesign) makes the
  entity match the table. Verified by replaying the exact failing request
  against the new build + dev DB → **200**.
- No DB change needed for this fix — deploying the new build resolves it.

---

## 7. Deployment

### Dev
- ✅ Dev DB already fully migrated (junction populated, free-text column dropped).
- Just deploy the new build.

### Production — run the script FIRST, then deploy
Run `Scripts/Lookups_Phase1_BrandSizeIconReviews.sql` on
**db_ab1977_cableproduction** *before* deploying the new build. It is
idempotent (safe to re-run) and, for brands, does in order:

1. Creates the `ChargerBrand` lookup and seeds it from prod's existing
   distinct free-text values.
2. Creates the `ChargingPointChargerBrand` junction.
3. Migrates every station's free-text brand into the junction
   (count = station's `ChargersCount`, min 1).
4. **Drops the `ChargingPoint.ChargerBrand` column** (only after 1–3 preserved the data).

> Deploy order matters: run the script before switching the build, so the old
> build's raw SQL never queries the dropped column. The window between script
> and deploy briefly breaks *old-build* station reads, so run them back-to-back.

---

## 8. Verified behavior (live tests on dev)

| Test | Result |
|------|--------|
| Create with 3 brands (4+3+2) | `chargersCount` auto-summed to 9 ✅ |
| Update with new set | Junction replaced, sum recomputed ✅ |
| Update sending `chargersCount: 999` + brands | 999 ignored, sum wins ✅ |
| Update without `chargerBrands` | Brands untouched ✅ |
| Unknown brand id | 400 with the missing ids ✅ |
| Delete brand in use | 400 ✅ |
| ById / list responses | No `chargerBrand` text field, array present ✅ |
| List filter `chargerBrandId` | Matches via junction ✅ |
| Submit update-request (was 500) | 200 ✅ |
| Build | 0 errors ✅ |
