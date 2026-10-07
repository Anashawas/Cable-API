# Sprint 5 — Backend Response

**Responding to:** `SPRINT5_BE_REQUIREMENTS.md` (2026-09-17, updated 2026-09-22)
**Date:** 2026-09-22
**Status:** all backend items implemented, built, and **verified end-to-end on dev**. Every response shown below is a real captured payload from that run.

**Deploy prerequisite (production):** run `Scripts/PlugType_Type2AcFast_Seed.sql` (§6). Already applied to dev.

---

## Summary

| # | Item | Outcome | Client action |
|---|---|---|---|
| 1 | Delete station image via update request | **Implemented** — accepts file names, validates ownership | None — send what you built |
| 2.1 | Admin on followers / notifications endpoints | **Already allowed.** Your 401 is a missing/invalid token, not policy | Check the admin web attaches the bearer on these calls |
| 2.2 | `favoritesCount` on `GetAllChargingPoints` | **Fixed** — was never computed on that query | None |
| 2.3 | Same for service providers | **Fixed** on list + detail; admin auth already worked | None |
| 3 | `isVerified` on `UpdateChargingPoint` | **Already worked.** Now admin-only (see §3) | None for admin web |
| 4 | `ownerPhone` on Add/Update | **Already worked.** Returns as `+962…` | Strip `+` on load if you compare strings |
| 5 | Partner analytics data flowing | **Confirmed** — hundreds of thousands of events | None |
| 6 | Seed "Type 2 AC (Fast)" | **Seeded as id 23** — matches your catalogs | None |
| 7 | Remote Config flag | Console-only; not a code change | — |

---

## 1. Delete a station image (partner → admin approval)

### What changed
`POST /api/provider/charging-points/submit-update-request/{chargingPointId}` now takes **file names** in `attachmentsToDelete`, exactly as the doc proposed:

```json
{
  "name": "Power Station",
  "attachmentsToDelete": [
    "019b0276-434e-7413-abf0-9f74e8cd2a22.jpg"
  ]
}
```

- **File name = last path segment of the image URL.** A full URL is also accepted and reduced to its file name server-side.
- **Ownership is enforced.** Every name must be a live attachment of `{chargingPointId}`. Any name that isn't — another station's, or nonexistent — **rejects the whole request** with a field-level 400, before anything is saved. Nothing is silently dropped, so a partner can never believe a deletion was queued when it wasn't.
- Deletions are applied **only on admin approval**: the attachment is removed from the gallery **and** the physical file is deleted from `/CableAttachments/`.

### Captured responses

Foreign / unknown file name → **400**, nothing saved:
```json
{
  "status": 400,
  "detail": "These files do not belong to this charging point: not-mine-0000.jpg",
  "errors": [{ "name": "attachmentsToDelete",
               "reasons": ["These files do not belong to this charging point: not-mine-0000.jpg"] }]
}
```

Valid file name → **200**, body is the new update-request id: `11`

### 1.2 — visible in the review diff
`GET /api/provider/charging-points/update-requests/{id}` (and the pending list) include the deletion as a `changes[]` entry — the doc's first option:
```json
{ "field": "attachmentsToDelete",
  "oldValue": ["https://…/CableAttachments/019b0276-434e-7413-abf0-9f74e8cd2a22.jpg"],
  "newValue": null }
```

### Acceptance (verified)
| Check | Result |
|---|---|
| Mark 1 of 6 photos → submit → request created with 1 pending deletion | ✅ id 11 |
| Diff shows the deletion | ✅ |
| File name that isn't the partner's | ✅ 400 |
| Full URL instead of bare file name | ✅ accepted |
| Non-owner submitting for the station | ✅ 403 |

> Existing rule unchanged: **one pending update request per station**. A second submit while one is pending returns 400 *"There is already a pending update request for this charging point"* — cancel or wait for review first.

---

## 2. Admin: followers & sent-notifications

### 2.1 — no policy change needed
Both endpoints already allow **owner, active worker, or admin**, for both `ChargingPoint` and `ServiceProvider`:
- `GET /api/provider/favorites/{type}/{id}?page=&pageSize=`
- `GET /api/provider/favorites/{type}/{id}/notifications?page=&pageSize=`

Verified on dev with the discriminator that matters:

| Caller | Result |
|---|---|
| No `Authorization` header | **401** |
| Signed-in user who is not owner/worker/admin | **403** |
| Admin | passes |

So a **401 means the request arrived without a valid bearer token** — it is not the endpoint refusing the admin role (that would be 403). Please check that the admin web attaches the token to these two calls and that it isn't expired. With a valid admin token you'll get the list for any station.

### 2.2 — `favoritesCount` on `POST /api/charging-points/GetAllChargingPoints` — fixed
Root cause: the count was only ever computed on the owner-scoped query; the admin list is a raw-SQL path that never populated it. Now batched into that path (and the paged variant) in one query.

Captured on dev: station 37 → `"favoritesCount": 7`; 67 of 180 stations non-zero; no nulls.
Production has **513 favourites across 148 of 214 stations**, so the column will be meaningful immediately.

### 2.3 — service providers — fixed
`favoritesCount` is now populated on `GetAllServiceProviders` and `GetServiceProviderById` (it existed on the DTO but only `GetMyServiceProviders` filled it). Captured: provider 2 → `"favoritesCount": 1`, others `0` (never `null`). Admin authorization on the `ServiceProvider` favorites endpoints already worked — see 2.1.

---

## 3. `isVerified` on `UpdateChargingPoint` — already worked, now guarded

`PUT /api/charging-points/UpdateChargingPoint/{id}` reads and persists `isVerified`, and it round-trips on GET. **No client change** for the admin web.

Two things worth knowing:

**Authorization was added.** Previously *any* signed-in account could edit *any* station and set its own `isVerified = true`. Now:
- **Owner or admin** may update a station → anyone else gets **403** `"Only the station owner or an admin can update this charging point."`
- **Only an admin's `isVerified` is honoured.** An owner's save leaves the flag exactly as it was. Same on `AddChargingPoint`: a self-registered station is always created `isVerified = false`, and a non-admin passing another `ownerId` gets **403**.

**`isVerified` is a non-nullable bool.** A client that omits it sends `false`. For admins that means *omitting the field un-verifies the station* — always send the current value. (Owners are protected from this by the rule above.)

Verified: consumer → 403 · owner sets `false` → 200 but flag stays `true` · admin sets `false` → flips · admin sets `true` → flips back.

---

## 4. `ownerPhone` on Add/Update — already worked

Both `POST /AddChargingPoint` and `PUT /UpdateChargingPoint/{id}` accept `ownerPhone` (distinct from `phone`) and persist it. Input is normalized to `962XXXXXXXXX`; **`962…`, `00962…`, `07…` and `7…` are all accepted** — no `+` required.

One detail for round-tripping: GET returns phones in **E.164 (`+962…`)** for dial-ready `tel:` links, while storage is `962…`. Resubmitting `+962…` normalizes fine, so nothing breaks — just don't expect the exact same string back.

---

## 5. Partner analytics — confirmed live

Events **are** being recorded. Production, last 7 days:

| Entity | FullView (1) | HalfView (2) | CallButtonClick (3) | MapClick (4) |
|---|---:|---:|---:|---:|
| ChargingPoint | 24,726 | 11,320 | 274 | 3,854 |
| ServiceProvider | 224 | — | 3 | 34 |

All-time ChargingPoint FullView: 221,598. Insights / Statistics will not be zeros for any station with traffic.

**Privileges:** the API's privilege tables are **empty in production by design** (not seeded), so `GetPrivileges` returns `[]` for everyone and your "no list ⇒ show all" gate keeps Statistics visible. Nothing to add. If that table is ever seeded, `ViewStatistics` must be granted to the Provider role or the item disappears — noted on our side.

---

## 6. Plug type "Type 2 AC (Fast)" — id **23** ✅

Seeded exactly as requested, with the id forced to **23** so it matches both app catalogs — **no app change needed**.

| Field | Value |
|---|---|
| `id` | **23** |
| `name` | `Type 2 AC (Fast)` |
| `serialNumber` | `AC TYPE 2 FAST` |
| `plugTypeFamily` | `EURO` |

Captured from `GET /api/plug-types/GetAllPlugTypes` on dev:
```json
{ "id": 23, "name": "Type 2 AC (Fast)", "serialNumber": "AC TYPE 2 FAST", "plugTypeFamily": "EURO" }
```

Why it had to be forced: the next identity on both dev and production was **22**, so a plain insert would have landed on 22 and the plug would never have rendered. The seed uses `IDENTITY_INSERT`; the gap at 22 is harmless (3–5 are already gaps).

**Applied to dev. Production needs `Scripts/PlugType_Type2AcFast_Seed.sql` run** — idempotent, safe to re-run.

---

## 7. Firebase Remote Config
`enable_app_review` is a console-only key; nothing in the backend. Whoever holds the Firebase project adds it.

---

## Corrections to the "no BE action" notes

**24-hour stations (`fromTime`/`toTime = "0:00"`).** The note says the apps already understand this marker. **The B2C list card does not** — it treats `0:00 → 0:00` as a zero-length window and renders **مغلق**, while the details screen correctly shows *مفتوح 24/7*. Details and the fix are in `MOBILE_OPEN_CLOSED_BADGE_FIX.md`. Because admin and partner web now write that marker more often, **more stations will show as closed in the B2C list until that mobile fix ships** — worth aligning the release timing. 151 of 209 production stations already carry it.

**`estPointsPerCharge`** — confirmed still absent from the backend. Remains pending.

---

## Files changed (backend)

- `Application/ChargingPoints/Commands/SubmitChargingPointUpdateRequest/SubmitChargingPointUpdateRequestCommand.cs` — file-name deletions + ownership validation
- `WebApi/Requests/ChargingPoints/SubmitChargingPointUpdateRequest.cs` — `attachmentsToDelete: string[]`
- `Infrastructrue/Persistence/Repositories/ChargingPointRepository.cs` — `favoritesCount` on admin list + paged list
- `Application/ServiceProviders/Queries/GetAllServiceProviders/…`, `GetServiceProviderById/…` — `favoritesCount`
- `Application/ChargingPoints/Commands/UpdateChargingPoint/…`, `AddChargingPoint/…` — owner/admin guard, admin-only `isVerified`
- `Scripts/PlugType_Type2AcFast_Seed.sql` — new
