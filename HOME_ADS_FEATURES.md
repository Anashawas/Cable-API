# Home-Screen Ads — Location Banners, Premium Stations & Welcome Message

**Date:** 2026-07-27 · **Status:** ✅ All phases (P0+P1+P2) implemented and live-tested on dev.
**Re:** `BE-home-ads-requirements.md` — every endpoint from the checklist is built.
**Scalar tags:** Banners · Charging Points · Settings · Home · Ads

---

## 1. Global pieces

### Admin nearby-radius (P0)
```
GET /api/settings/nearby-radius            → { "radiusKm": 30 }     (public)
PUT /api/settings/nearby-radius            { "radiusKm": 45 }       (admin only)
```
Default 30 km. Caps nearby stations and radius-banners that don't carry their own `radiusKm`.

### Analytics event codes (extends POST /api/analytics/track)
| entityType | eventType | meaning |
|---|---|---|
| Banner | 20 / 21 | view / click *(existing)* |
| ChargingPoint | **30 / 31** | premium-card view / click |
| Announcement | **20 / 21 / 22 / 23** | welcome view / click / dismiss / CTA-conversion |

- Track payload now accepts optional **`city`, `lat`, `lng`** — send them so "Irbid users saw this" is provable.
- **Impression dedup**: views (20/30) are counted at most **once per user per asset per 30 minutes** server-side. Fire freely — the server de-duplicates.
- Announcement **dismiss (22)** also feeds the `stopOnDismiss` frequency cap automatically.

---

## 2. Location banners (P0)

```
GET /api/banners/GetNearbyBanners?lat=31.95&lng=35.91&city=Amman     (no auth needed)
```

- Only banners inside their **active window** are returned — expired ads never serve.
- **Render the returned order as-is. Stop shuffling** — ordering is the paid feature.
- Layering (never interleaved): **①radius-hit (nearest first) → ②same city → ③national.**
  Out-of-range radius ads and other-city ads are **excluded** (the advertiser paid for that area).
- Within a layer: pacing (less-shown boosted from real view rollups) + optional paid `priority` + small jitter.

Each item: existing banner fields **plus** `targetType`, `targetCity`, `centerLat/Lng`, `radiusKm`, `linkedEntityType/Id`, `priority`, `campaignId`, `startDate/endDate`, and **`distanceKm`** (radius banners only; show "2.3 km away").

`GetAllBanners` / `AddBanner` / `UpdateBanner` / `DeleteBanner` are **unchanged** — a banner created the old way defaults to `targetType: national` and serves exactly as before. Targeting is set through a dedicated admin endpoint:

```
PUT /api/banners/SetBannerTargeting/{id}      (admin only)
{ "targetType": "radius",            // national | city | radius
  "targetCity": null,                // required when city
  "centerLat": 31.95, "centerLng": 35.91, "radiusKm": 8,   // required when radius
  "linkedEntityType": "station", "linkedEntityId": 456,     // optional
  "priority": 50,                    // optional paid top-spot weight
  "campaignId": 55 }                 // optional billing link
```

---

## 3. Nearest stations & premium (P0/P1)

```
GET /api/charging-points/GetNearest?lat=31.95&lng=35.91&premium=all   (true | false | all)
GET /api/charging-points/GetNearestPremium?lat=&lng=                  (premium only — same item shape)
GET /api/charging-points/GetNearestNormal?lat=&lng=                   (non-premium only — same item shape)
```
- All stations within the admin radius, **distance-sorted only** (no priority weighting on stations). No count limit.
- Each item: `distanceKm`, **`isPremium`** (derived from the paid premium dates — warn-only expiry, an expired premium stays premium until admin removes), `premiumUntil`, `viewImage` (**approved creative only**, null → app falls back to the station photo), `statusSummary`, `stationType`, `isPartner`, `iConUrl`.

### viewImage — one promo image, partner-uploaded, admin-reviewed (P1)
```
POST   /api/charging-points/{id}/view-image        multipart file      (owner/worker → PENDING; admin → approved)
PUT    /api/charging-points/{id}/view-image/review?approve=true|false  (admin)
DELETE /api/charging-points/{id}/view-image                            (owner/worker/admin)
GET    /api/charging-points/view-image/pending?page=&pageSize=         (admin — review queue)
```
- The pending queue item: `{ chargingPointId, stationName, cityName, ownerId, ownerName, viewImage (full URL), uploadedAt }`, newest first, optional paging.
- Exactly ONE image per station — a new upload **replaces** the old.
- jpg/png/webp only, max 5 MB → anything else `400`.
- Pending/rejected images are **never** served to B2C.
- Owners see their image + its status on `GetMyChargingPoints` (`viewImage`, `viewImageStatus`).

---

## 4. Welcome message (P1)

```
GET /api/home/announcement?lat=&lng=&city=         (send token when logged in)
```
Returns **at most one** message or `null`. BE decides everything:
- **Targeting**: radius beats city beats national; audience `all | guests | loggedIn`.
- **Scheduling**: only inside `startDate..endDate`.
- **Frequency caps** (enforced server-side for logged-in users): `maxPerDay`, `cooldownHours`, `maxLifetime`, `stopOnDismiss`. A served message counts as a show.
- ⚠️ Guests can't be capped server-side (no identity) — the app should apply the returned cap fields locally for guests, or send a stable device id as `AnonymousId` on track calls.

Response is **FLAT** (no nested localized objects): `{ id, titleEn, titleAr, bodyEn, bodyAr, imageUrl, actionType, actionUrl (same 1..5 contract), actionLabelEn, actionLabelAr, targetType, targetCity, centerLat, centerLng, radiusKm, audience, startDate, endDate, maxPerDay, cooldownHours, maxLifetime, stopOnDismiss, isActive, campaignId, advertiserId }`.
App fires events 20/21/22/23 and honors **one takeover per app open** (client-side arbitration: welcome > loyalty > what's-new).

### Admin CRUD (admin-only by design — partners cannot create these)
```
POST   /api/home/admin/announcements               create (bilingual content required; returns the new id)
PUT    /api/home/admin/announcements/{id}          update (incl. isActive, actionLabelEn/Ar)
GET    /api/home/admin/announcements               list (optional page/pageSize)
DELETE /api/home/admin/announcements/{id}          HARD delete (per-user cap states removed; analytics history kept)
POST   /api/home/admin/announcements/{id}/image    multipart, form key "files", jpg/png/webp ≤5MB → { imageUrl } (also sets it on the announcement)
GET    /api/home/admin/announcements/{id}/stats?from=&to= → { impressions, clicks, dismisses, conversions, ctr, uniqueUsers }
```
- `uniqueUsers` counts logged-in users **and** guests (via `anonymousId`) once each.
- Impression dedup window is **30 seconds** per user/device (client decision §10.1).

---

## 5. Advertisers, campaigns & reports (P2)

```
POST/PUT/GET /api/ads/admin/advertisers[/{id}]     admin CRUD
POST/PUT/GET /api/ads/admin/campaigns[/{id}]       admin CRUD (type: banner|premium|welcome, window, price, status)
GET /api/ads/campaigns/{id}/stats?from=&to=        admin → { impressions, clicks, ctr, dismisses, dismissRate, uniqueUsers, byDay[], byCity[] }
GET /api/ads/premium/{stationId}/stats?from=&to=   owner/worker/admin → { impressions, clicks, ctr }
```
Link a banner or announcement to a campaign (`campaignId`) and its delivery rolls up into the campaign report — including per-city proof from the geo fields on track.

---

## 6. What the app must change (from the requirements doc §9)

1. Send `lat/lng/city` to GetNearbyBanners / GetNearest / announcement (fallback: chosen city → Amman).
2. **Stop shuffling banners** — render server order; show `distanceKm` when present.
3. Premium card reads `isPremium` + `viewImage` (stop guessing from `stationType.id`).
4. Fire 30/31 for the premium card, 20/21/22/23 for welcome, add `city` (+lat/lng) to all track calls.
5. Honor caps + one-modal-per-open; partner app adds the single-image viewImage upload screen.
6. `actionType 1..5` contract unchanged everywhere.

---

## 7. Verified live on dev (all ✅)

radius setting GET/PUT + 403 non-admin · layered order exactly ①radius(0 km)→②city→③national from Amman coords · Amman-targeted ads correctly hidden from an Irbid location · 89 stations distance-sorted within 30 km · `premium=true` filter · announcement served to guest + user, **second call same day capped**, wrong city → null · dismiss tracked → state updated · **duplicate views collapsed to 1 impression** · `city` recorded on events · partner upload → `pending`, non-owner → 403, admin approve → public sees it, reject → hidden, txt file → 400 · advertiser+campaign CRUD · campaign stats with byDay/byCity · premium stats 30/31

## 7b. Part A deltas (client BE-requirements, verified live on dev ✅)

- `GET /api/admin/attention-summary` (admin) → `{ stationUpdateRequests, openComplaints, pendingViewImages, pendingOffers, settlementsPending, settlementsDisputed, premiumExpiringSoon, premiumExpired, campaignsEndingSoon, announcementsExpiringSoon, pendingWorkerNotifications }` — one dashboard call instead of 8+ list calls; "soon" = next 7 days; `pendingWorkerNotifications` stays 0 until the worker-approval flow (Part B) ships.
- Flat announcement + `actionLabelEn/Ar` round-trip · announcement image upload → served from `/CableAnnouncements/` · stats with/without date range · hard delete → second delete 404 · pending view-image queue with real row · non-admin → 403 on all new admin endpoints · GetNearestPremium/Normal · duplicate guest views 2s apart → 1 event row (30s dedup).

## 8. Deploy

`Scripts/HomeAds_Phase1.sql` (2+5 tables/column-sets, idempotent) is **Part 12 of the master deploy script** (now 14 parts) — same procedure as before: backup → master script → deploy build. Dev DB is already migrated.
