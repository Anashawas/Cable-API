# Sprint 5 — Backend Requirements

**Date:** 2026-09-17 (updated 2026-09-22)
**Apps affected:** Cable B2C (Flutter app), Cable Partner (Flutter app), Cable Partner Web, Cable Admin Web

This lists what the **frontend has already built this sprint** and now needs from the backend to actually work end-to-end. Items are ordered by priority. Each has exact endpoints, payloads, and acceptance criteria.

---

## 1. 🔴 Delete a specific station image (partner → admin approval)

**Context:** Partners can add gallery images (via an update request that an admin approves), but there is **no way to remove a specific existing image** in the partner app or partner web. We built the UI on both — the owner now sees their existing gallery photos and can mark individual ones for removal. Removal is submitted **as part of the update request** (consistent with how edits/adds already work) and should be applied when an admin approves.

The frontend now sends a new field on the existing submit-update-request call.

### 1.1 Accept `attachmentsToDelete` on submit-update-request
**Endpoint (already exists):** `POST /api/provider/charging-points/submit-update-request/{chargingPointId}`

Add support for a new optional body field:
```json
{
  "name": "…",                       // existing fields, unchanged
  "attachmentsToDelete": [
    "01992989-df59-71f0-9d39-5ade01d2f009.jpg",
    "01992998-1af4-7f51-bf20-a67536e46556.jpg"
  ]
}
```
- **`attachmentsToDelete`**: array of **file names** (strings) — the stored file name of each image, i.e. the last path segment of its URL (`https://cable-app.com/CableAttachments/{fileName}`). The client sends file names because the image lists (`GetMyChargingPoints` / attachments) expose only URLs, **no numeric id**.
- **Preferred:** accept file names as above.
- **Alternative (if you'd rather key by id):** return a numeric `attachmentId` per image on `GetMyChargingPoints` **and** on `GetAllChargingPointAttachmentsById`, and accept `attachmentsToDelete: number[]`. If you choose this, tell us and we'll switch the client. **File names are simpler and preferred.**
- **Security:** validate every file name actually belongs to `{chargingPointId}`; ignore/deny anything that doesn't.

### 1.2 Surface pending deletions in the update-request diff
So an admin knows what they're approving, include the requested deletions in the request detail the admin review screen reads:
- Either as entries in the existing `changes[]` diff (e.g. `{ field: "attachmentDeleted", oldValue: "{fileName}", newValue: null }`),
- or a dedicated `attachmentsToDelete: string[]` on the update-request DTO returned by the pending/detail endpoints.

### 1.3 Apply on approval
When the admin **approves** the update request, delete each listed attachment from the charging point's gallery **and** remove the underlying file from storage (`/CableAttachments/{fileName}`).

**Acceptance:**
- Partner marks 2 of 5 photos for removal → submits → an update request is created showing 2 pending deletions.
- Admin approves → those 2 files are gone from the station gallery and storage; the other 3 remain.
- Partner deleting a file name that isn't theirs is rejected.

---

## 2. 🟠 Admin: Station Followers & Sent-Notifications

**Context:** In Cable Admin → Charge Management, each station now has **Followers** and **Notifications sent** tabs (and a dialog). They call the existing partner-favorites endpoints. Verified on live: the endpoints exist but return **401 unauthenticated**, and `favoritesCount` is returned but always **0**.

### 2.1 Authorize the ADMIN role on these endpoints
- `GET /api/provider/favorites/ChargingPoint/{id}?page=&pageSize=` (followers list)
- `GET /api/provider/favorites/ChargingPoint/{id}/notifications?page=&pageSize=` (sent notifications)

They currently allow **owner / worker / admin**. Please confirm a platform **admin token** is accepted for *any* station (not only the station's own owner). If not, add the admin role to the auth policy so the admin dashboard can read any station's followers/notifications.

**Acceptance:** an admin (not the owner) opens any station's Followers tab and sees the list instead of a 403.

### 2.2 Populate `favoritesCount` on the stations list
**Endpoint:** `POST /api/charging-points/GetAllChargingPoints`

The field `favoritesCount` is present in the response but returns **0 for all 211 stations** on live. Please compute the real count (same value the followers endpoint returns). It drives the "Followers" count column in the admin stations table.

**Acceptance:** stations with followers show a non-zero `favoritesCount`.

### 2.3 Same for SERVICE PROVIDERS (new this sprint)
The admin now also shows **Followers / Notifications** tabs on the **service-provider** detail page, hitting the same type-generic endpoints with `{type} = ServiceProvider`:
- `GET /api/provider/favorites/ServiceProvider/{id}` and `.../ServiceProvider/{id}/notifications`
Please confirm the **admin role** is authorized for `ServiceProvider` too (same as §2.1), and — to drive a future followers count on the service-provider list — add **`favoritesCount`** to the service-provider list/detail DTO (it currently has `visitorsCount`/`rateCount` but no follower count).

---

## 3. 🟠 Admin: mark a station Verified from Edit Station

**Context:** The admin Edit-Station form now has a **Verified** switch. On save it sends `isVerified` in the standard update body:
**Endpoint:** `PUT /api/charging-points/UpdateChargingPoint/{id}`
```json
{ "name": "…", "isVerified": true }
```
Please confirm `UpdateChargingPoint` **reads and persists `isVerified`**. If verification is instead a separate endpoint, give us that endpoint and we'll point the toggle at it.

**Acceptance:** toggling Verified on and saving flips `isVerified` on the station; it round-trips on reload.

---

## 4. 🟠 Admin: accept `ownerPhone` on Add/Update charging point

**Context:** The admin add/edit station form now has an editable **Owner Phone** field (previously the owner's contact phone could only be set by the partner). On save it's sent in the standard body, normalized to storage format `962…`:
- `POST /api/charging-points/AddChargingPoint`
- `PUT /api/charging-points/UpdateChargingPoint/{id}`
```json
{ "name": "…", "phone": "9627…", "ownerPhone": "9627…" }
```
Please confirm both endpoints **read and persist `ownerPhone`** (distinct from the station's public `phone`). If it's ignored today, add it.

**Phone format note (all apps):** the partner app already stores phones as `962<national>` (no `+`, no leading `0`). We aligned the partner **web** to send the same canonical format for `phone` and `ownerPhone`. Please make sure the backend accepts/stores `962…` consistently and doesn't require `+` or a local `07…` form.

**Acceptance:** setting Owner Phone in the admin form and saving persists it and it round-trips on GET (`ownerPhone`).

---

## 5. 🟢 Partner analytics — confirm data is flowing

**Context:** We turned on the (already-built) partner analytics — the app's dashboard "Insights" screen and a new **Statistics** page in the partner web. Both read the existing, owner-scoped endpoint:
`GET /api/analytics/{entityType}/{entityId}/summary?from=&to=` (verified live: 401 unauth, i.e. exists + owner-scoped).

Two things to confirm on the BE side:
1. **Event tracking must actually be recording** the events this endpoint aggregates — `FullView (1)`, `HalfView (2)`, `CallButtonClick (3)`, `MapClick (4)` — for charging points and service providers. If the B2C app isn't sending these events (or the BE isn't storing them), the partner analytics screens render but show **all zeros**. Please confirm events are being captured so the numbers are real.
2. **(Web only) privilege grant — optional.** The web "Statistics" nav item is gated on the `ViewStatistics` privilege. Where the API returns no privilege list for a partner, it shows by default; if you *do* send a privilege list, please include `ViewStatistics` for the **provider** role so the menu item appears. (If you'd rather not, tell us and we'll drop the gate.)

**Acceptance:** an owner opening Insights (app) / Statistics (web) sees non-zero view/call/map counts for a station that has real traffic.

---

## 6. 🔴 Seed a new plug type: "Type 2 AC (Fast)"

**Context:** We added a new charging-connector option, **Type 2 AC (Fast)** — the same physical Type 2 connector as the existing slow one, but a fast AC charger (for cars like the Renault Zoe / Samsung EVs). Plug types are **read-only lookup data** — there is no admin screen to add one, so this must be seeded on the backend.

**Ask:** insert one new PlugType row:
| Field | Value |
|---|---|
| `name` | `Type 2 AC (Fast)` |
| `serialNumber` | `AC TYPE 2 FAST` (must be distinct from the slow `AC TYPE 2`) |
| `plugTypeFamily` | `EURO` (same family as the slow Type 2) |
| `id` | server-assigned — **tell us the id** |

It should be returned by `GET /api/plug-types/GetAllPlugTypes` (so partners can select it) and included in a station's `plugTypeSummary` once assigned.

**⚠️ Critical — the id:** the Flutter apps map each plug to its icon/label/compat-note **by numeric id**. We provisionally used **`id: 23`** in both apps' plug catalogs (`mob/lib/core/constants/plug_type_constants.dart` and `cable_partner/lib/core/constants/plug_icons.dart`). **Please tell us the real id you assign** — if it isn't 23, we change one number in each catalog. Until this row exists and its id matches, the new plug won't appear on any station.

**Frontend already done (no further app work once the id matches):** the B2C app shows this plug with an **"!" info dot**; tapping it opens a sheet explaining which cars it fits (that text is client-side and editable without a backend change). The partner app lists it in the Fast group with an icon. The web apps show it as text automatically.

**Acceptance:** `GetAllPlugTypes` returns the new type; a station assigned it shows the Type 2 (Fast) chip with the "!" info tap in the B2C app.

---

## 7. ⚙️ Firebase Remote Config — one console flag (no code)

We added an app-store rating prompt to the **B2C app**, gated by a Remote Config boolean so you can disable it without a release. Please add this key in the Firebase Remote Config console:
- **`enable_app_review`** = `true` (boolean). Set to `false` to instantly turn the rating prompt off for everyone.

(The client already ships a safe default of `true`, so nothing breaks if the key is missing — this just gives you the remote kill switch.)

---

## Notes / already working (no BE action)
- **Admin per-image delete** already works via `DELETE /api/files/attachment/CableAttachments/{fileName}` (verified live) — no change needed.
- **Decimal input fields** across the admin were a frontend-only fix — no BE change.
- **24-hour stations** — partner web + admin now write the existing `fromTime`/`toTime = "0:00"` marker the backend + apps already understand. No BE change.
- **B2C "Spend your points" page** — built entirely on the existing `offers/GetActiveOffers` + loyalty balance. No BE change.
- **B2C unverified-station caution** — reuses the existing `usercomplaints/AddUserComplaint` for the "report wrong info" action. No BE change.
- **B2C app-store rating prompt** — client-only (`in_app_review`), no endpoints (see the one Remote Config flag in §7).

## Open, unchanged from prior sprints (still pending)
- `estPointsPerCharge` (loyalty §11.1) — still awaiting BE.
