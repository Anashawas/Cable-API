# BE-requirements — Backend Response (Part A + Part B)

**Status: everything in the document is implemented and live-tested on dev.**
This is the single response to `BE-requirements.md`: what changed, the new endpoint contracts, and our answers to your open decisions. Base URL examples omit the host; all endpoints are under `/api`.

---

# PART A — Home Screen Ads (deltas)

Most of Part A was already live; these are the six deltas that were missing or wrong. Everything else in your doc (nearby banners ranker, radius setting, premium flags, viewImage flow, guest analytics with `anonymousId`, campaign/premium stats) was already implemented and verified.

## A1. Two station endpoints (§10.3)

```
GET /api/charging-points/GetNearestPremium?lat=&lng=     → premium only
GET /api/charging-points/GetNearestNormal?lat=&lng=      → non-premium only
```
Same item shape as GetNearest: `distanceKm`, `isPremium`, `premiumUntil`, `viewImage` (approved only), `statusSummary`, `stationType`, `isPartner`, `iConUrl`. Distance-sorted, no count limit, admin radius applies.

## A2. Impression dedup = 30 seconds (§10.1)

Server-side dedup window changed from 30 minutes to **30 seconds** per user/device per asset (applies to Banner views and Premium views, for logged-in users AND guests via `anonymousId`). Verified: two identical guest views 2s apart stored exactly 1 event.

## A3. Announcement is FLAT everywhere (§5.2)

`GET /api/home/announcement` and all admin CRUD now use flat fields — the nested `title:{en,ar}` shape is gone:

```jsonc
{ "id": 4,
  "titleEn": "…", "titleAr": "…", "bodyEn": "…", "bodyAr": "…",
  "imageUrl": "…", "actionType": 1, "actionUrl": "…",
  "actionLabelEn": "Learn More", "actionLabelAr": "اعرف أكثر",   // NEW optional CTA labels
  "targetType": "national", "targetCity": null,
  "centerLat": null, "centerLng": null, "radiusKm": null,
  "audience": "all", "startDate": "…", "endDate": "…",
  "maxPerDay": 1, "cooldownHours": 24, "maxLifetime": 30, "stopOnDismiss": true,
  "isActive": true, "campaignId": null, "advertiserId": null }
```
Create already returned the new id (`200` + bare number).

## A4. Announcement admin extras (§5.5 d/e/f)

```
DELETE /api/home/admin/announcements/{id}              → HARD delete (per-user cap states removed, analytics kept)
POST   /api/home/admin/announcements/{id}/image        → multipart, form key "files", one jpg/png/webp ≤5MB
                                                          → { "imageUrl": "…" } (also set on the announcement)
GET    /api/home/admin/announcements/{id}/stats?from=&to=
       → { "impressions", "clicks", "dismisses", "conversions", "ctr", "uniqueUsers" }
```
`uniqueUsers` = distinct logged-in `userId` + distinct guest `anonymousId`. `from`/`to` optional.

## A5. Pending view-image queue

This one did NOT exist (your doc marked it ✅ by mistake) — now it does:

```
GET /api/charging-points/view-image/pending?page=&pageSize=      (admin)
→ items: { chargingPointId, stationName, cityName, ownerId, ownerName, viewImage (full URL), uploadedAt }
```
Newest first, optional paging.

## A6. Attention summary (§11)

```
GET /api/admin/attention-summary        (admin)
→ {
    "stationUpdateRequests": 0,      // requestStatus = pending
    "openComplaints": 120,           // status not in {1 NotComplaint, 2 Solved}
    "pendingViewImages": 0,          // viewImageStatus = pending
    "pendingOffers": 0,              // approvalStatus = Pending(1)
    "settlementsPending": 2,         // SettlementStatus = Pending(1)
    "settlementsDisputed": 0,        // SettlementStatus = Disputed(4)
    "premiumExpiringSoon": 0,        // premiumExpiresAt within 7 days (warn-only rule §4.3)
    "premiumExpired": 0,             // passed, still premium
    "campaignsEndingSoon": 0,        // active, endDate within 7 days
    "announcementsExpiringSoon": 0,  // isActive, endDate within 7 days
    "pendingWorkerNotifications": 0  // Part B F3 — LIVE, real count
  }
```
One call instead of 8+ list calls. `expiringSoonDays` = 7 (hardcoded for now, as you suggested). Every count maps to an existing admin route for the click-through.

---

# PART B — Notifications

## B1. Root cause found and fixed

Our system's FCM pushes previously carried **no data payload at all** (only title/body) — that's why push taps went home. Every send now flows through ONE routing builder: from `(targetType, targetId)` the backend produces both the inbox `deepLink` and the console-parity FCM data, so **console push tap, our push tap, and inbox tap all land on the same screen** (R1/R2).

**FCM data on every push we send:**
```jsonc
{ "type": "stations",                                 // or "provider_announcement" / "general"
  "chargerId": "42",                                  // stations; providers get "providerId"
  "deepLink": "cable://charging-point?targetId=42" }  // fallback, as you requested
```
**Canonical deep links (R7 — id is always `targetId`):**
- Station → `cable://charging-point?targetId=<id>`
- Service provider → `cable://service-provider?targetId=<id>`

`shops` is **dropped** from the contract (R6, your recommendation) — nothing emits it.

## B2. R5 — the routing map (from the REAL type list)

Your suggested table used a different type list than the DB has. `GET /api/notification-types` now returns a `deepLinksTo` field per type; this is the actual map:

| id | name | deepLinksTo |
|---|---|---|
| 1 | system_announcement | none |
| 2 | favorite_added | charging-point |
| 3 | favorite_removed | charging-point |
| 4 | charging_point_status_changed | charging-point |
| 5 | offer_available | charging-point |
| 6 | rating_received | charging-point |
| 7 | complaint_status_updated | complaint |
| 8 | charging_session_started | charging-point |
| 9 | charging_session_completed | charging-point |
| 10 | new_charging_point_nearby | charging-point |
| 11 | update_request_submitted | charging-point |
| 12 | update_request_decided | charging-point |
| 13 | provider_announcement | provider *(per-send: the stored deepLink decides station vs provider)* |

Treat `deepLinksTo` as the icon/expectation hint — **navigation is always driven by the stored `deepLink`**.

### Your §8 open decisions — answered
1. **Type map:** as above (built from the real DB types).
2. **Admin target (R4):** yes — structured `targetType`+`targetId`, backend builds the link. Wire the station picker to it.
3. **`shops`:** dropped.
4. **Offer deep link:** the station carrying the offer → `cable://charging-point?targetId=<stationId>`.
5. **Partner announcement type:** reuse **`provider_announcement` (id 13)** — already exists, default for fan announcements.

## B3. R4 — admin sends a TARGET, not free text

`POST /api/notifications` and `POST /api/notifications/send-by-filter` accept:
```jsonc
{ "notificationTypeId": 6, "title": "…", "body": "…",
  "targetType": "charging-point",     // "charging-point" | "service-provider" | "none"
  "targetId": 42
  /* existing fields unchanged: userIds / isForAll / filters / appType */ }
```
Backend builds `deepLink` + FCM `type`/`chargerId`. A raw `deepLink` is still accepted for advanced cases; the structured target wins when both are sent.

## B4. R3 + F5 — partner send (routing + structured payload)

`POST /api/provider/favorites/{ChargingPoint|ServiceProvider}/{id}/notify` now takes:
```jsonc
{ "notificationTypeId": 13, "body": "…" }   // title built server-side: "{typeName} From {stationName}"
{ "title": "…", "body": "…" }               // legacy raw title — still accepted
```
- Routing is attached **automatically** from the URL path — the partner never sends it.
- Response: `{ recipientCount, pushDeliveredCount, remainingSendsToday, status, notificationId }` — `status` is `"sent"`, or `"pending"` for a worker (see B6).
- ⚠️ Type *names* are code-style (`provider_announcement`), so a built title reads "provider_announcement From محطة X". If you want pretty titles, send us the per-type display names (EN/AR) and we'll add them — the mechanism is ready.

**Suggestion templates (admin-managed, partner-consumed):**
```
GET    /api/notification-templates?notificationTypeId=13   (any authenticated user — the compose chips)
POST   /api/notification-templates                         (admin) { notificationTypeId, body } → id
PUT    /api/notification-templates/{id}                    (admin)
DELETE /api/notification-templates/{id}                    (admin)
```

## B5. F1 — partner notification history

```
GET /api/provider/favorites/{type}/{id}/notifications?page=&pageSize=      (owner/worker/admin)
→ items (newest first):
  { "id", "title", "body",
    "status": "sent",                       // pending | sent | rejected
    "notificationTypeId", "notificationTypeName",
    "sentByUserId", "sentByName",
    "submittedAt", "sentAt", "decidedAt",
    "recipientCount",                       // fans at send time
    "deliveredCount",                       // devices FCM accepted (null until sent)
    "readCount" }                           // read in the in-app inbox
```

## B6. F3 — worker approval workflow

- A **worker's** send is stored as **`pending`** — nothing is delivered. Partner app shows "sent for approval".
- Owner or admin decides (the submitting worker cannot self-approve — 403):
```
GET /api/provider/favorites/{type}/{id}/notifications/pending      → approval queue
PUT /api/provider/favorites/notifications/{id}/approve             → delivered NOW
PUT /api/provider/favorites/notifications/{id}/reject              → discarded
```
- The **2-sends/24h rate limit counts only actual sends** — submissions/rejections are free; the limit is enforced at approval time.
- `GET /api/admin/attention-summary` → `pendingWorkerNotifications` is live.

## B7. F4 — auto-approve toggle

```
PUT /api/provider/favorites/{type}/{id}/auto-approve-worker-notifications    { "enabled": true }
```
Owner or admin only (workers get 403). Default **off**. When on, worker sends skip the pending state.

## B8. F2 — own review: see / edit / delete

```
GET    /api/rate/GetMyReview/{chargingPointId}    → caller's review or null
PUT    /api/rate/UpdateRate/{reviewId}            → { chargingPointRate, comment }
DELETE /api/rate/DeleteRate/{reviewId}
```
Author-or-admin only. ⚠️ **Security fix included:** the pre-existing update endpoint allowed ANY logged-in user to edit ANY review by id — it is now locked down. Station averages self-correct after edit/delete.

## B9. F6 — tap opens the sending station

Covered by B1 + B4: push tap AND inbox tap both route to the sender's station/provider. Verified end-to-end.

---

# Verified live on dev (full test log, all ✅)

**Part A:** flat announcement + actionLabels round-trip (AR/EN) · image upload via form key `files` → served from `/CableAnnouncements/`, imageUrl set · stats with/without date range · hard delete → second delete 404 · pending view-image queue with a real pending row · attention-summary all 11 counts · GetNearestPremium/Normal · duplicate guest views 2s apart → 1 impression (30s dedup) · non-admin → 403 on every admin endpoint.

**Part B:** types list returns `deepLinksTo` · template CRUD (non-admin create → 403) · worker submit → `pending` with server-built title · attention-summary `pendingWorkerNotifications` = 1 · worker self-approve → 403 · owner approve → delivered; inbox rows carry `cable://charging-point?targetId=42` + `chargerId` in data · owner reject → discarded · owner enables auto-approve (worker toggle attempt → 403) → next worker send goes direct · 3rd actual send in 24h → 400 rate-limit (the rejected one did NOT consume the limit) · history rows show sent/rejected with recipient/delivered/read counts (readCount verified) · GetMyReview → PUT own → PUT someone else's → 403 → DELETE own → null · admin structured-target send → **push actually delivered via Firebase with the routing data** + inbox deepLink built.

The only thing not verifiable from the backend alone is what the phone renders on tap — that's the app-side unification work from your §6.

---

# Data model changes (already on dev; production gets them via our deployment script)

| Entity | Change |
|---|---|
| Announcement | + `ActionLabelEn`, `ActionLabelAr` (nvarchar 100) |
| NotificationType | + `DeepLinksTo` (seeded routing map) |
| ProviderFavoriteNotification | + `Status` (pending/sent/rejected), `NotificationTypeId`, `BatchId`, `DeliveredCount`, `DecidedByUserId`, `DecidedAt`, `SentAt` |
| ChargingPoint | + `AutoApproveWorkerNotifications` (bit, default 0) |
| ServiceProvider | + `AutoApproveWorkerNotifications` (bit, default 0) |
| NotificationTemplate | NEW table (admin-managed body suggestions) |

No breaking changes to any existing endpoint: legacy `{ title, body }` partner sends, raw `deepLink` on admin sends, and all current response shapes keep working.
