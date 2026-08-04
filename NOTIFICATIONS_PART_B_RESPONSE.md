# Part B — Notifications: Backend Response

Everything in Part B of `BE-requirements.md` is **built and live-tested on dev**. This doc is the contract answer: what was implemented, the answers to your open decisions (§8), and the new/changed endpoints.

---

## 1. The routing contract (R1–R7) — DONE

**One source of truth.** Every send now flows through a single routing builder: from `(targetType, targetId)` the backend produces
- the inbox **`deepLink`** → `cable://charging-point?targetId=<id>` or `cable://service-provider?targetId=<id>` (id always travels as `targetId` — R7), and
- the FCM **data payload** for the push tap, console-parity (R2):

```jsonc
// FCM data on every push our system sends
{
  "type": "stations",                              // or "provider_announcement" / "general"
  "chargerId": "42",                               // stations → chargerId; providers → providerId
  "deepLink": "cable://charging-point?targetId=42" // belt-and-suspenders fallback
}
```

| Requirement | Status |
|---|---|
| R1 — inbox record always gets a server-built `deepLink` when routable | ✅ |
| R2 — our pushes now carry `type` + `chargerId` (+ `deepLink`) exactly like the console | ✅ (previously our FCM pushes carried **no data at all** — root cause found and fixed) |
| R3 — partner favorites-notify: routing attached automatically from the URL path | ✅ |
| R4 — admin sends a structured target, BE builds the link | ✅ (see §3) |
| R5 — published type→routing map | ✅ (see §2) |
| R6 — `shops` | **Dropped** from the contract, as you recommended. Nothing emits it. |
| R7 — id keys standardized: `chargerId` in FCM (stations), `providerId` (providers), `targetId` in the link | ✅ |

---

## 2. R5 — the routing map (real type list)

Your suggested table used a different type list than what exists in the DB. Here is the **actual** map, now stored on the type itself and returned by `GET /api/notification-types` as a new `deepLinksTo` field:

| id | name | deepLinksTo |
|---|---|---|
| 1 | system_announcement | none |
| 2 | favorite_added | charging-point |
| 3 | favorite_removed | charging-point |
| 4 | charging_point_status_changed | charging-point |
| 5 | offer_available | charging-point *(§8.4 answer: an offer notification lands on the station that has the offer)* |
| 6 | rating_received | charging-point |
| 7 | complaint_status_updated | complaint |
| 8 | charging_session_started | charging-point |
| 9 | charging_session_completed | charging-point |
| 10 | new_charging_point_nearby | charging-point |
| 11 | update_request_submitted | charging-point |
| 12 | update_request_decided | charging-point |
| 13 | provider_announcement | provider *(per-send: the stored deepLink decides charging-point vs service-provider)* |

The app should treat `deepLinksTo` as the icon/expectation hint; **the actual navigation is always the stored `deepLink`**.

### §8 open decisions — our answers
1. **Type map:** as above (per the real DB types).
2. **Admin target:** yes — `targetType` + `targetId`, link built server-side (R4).
3. **`shops`:** dropped.
4. **Offer deep link:** the station carrying the offer → `cable://charging-point?targetId=<stationId>`.
5. **Partner announcement type:** reuse **`provider_announcement` (id 13)** — it already exists and is the default for partner fan announcements.

---

## 3. R4 — admin sends a TARGET

`POST /api/notifications` and `POST /api/notifications/send-by-filter` accept two new optional fields:

```jsonc
{
  "notificationTypeId": 6,
  "title": "…", "body": "…",
  "targetType": "charging-point",   // "charging-point" | "service-provider" | "none"
  "targetId": 42
  // ...existing fields unchanged (userIds / isForAll / filters / appType)
}
```
The backend builds `deepLink` + FCM `type`/`chargerId` from the target. A raw `deepLink` string is still accepted for advanced cases, but the structured target wins when both are present. Wire the admin's station picker to this.

---

## 4. F1 — partner notification history

```
GET /api/provider/favorites/{ChargingPoint|ServiceProvider}/{id}/notifications?page=&pageSize=
```
Owner, worker, or admin. Newest first. Each row:
```jsonc
{ "id": 5, "title": "…", "body": "…",
  "status": "sent",                  // pending | sent | rejected
  "notificationTypeId": 13, "notificationTypeName": "provider_announcement",
  "sentByUserId": 14112, "sentByName": "worker3",
  "submittedAt": "…", "sentAt": "…", "decidedAt": "…",
  "recipientCount": 120,             // fans at send time
  "deliveredCount": 95,              // devices FCM accepted (null until sent)
  "readCount": 37 }                  // read in the in-app inbox
```

## 5. F2 — own review: see / edit / delete

```
GET    /api/rate/GetMyReview/{chargingPointId}   → the caller's review or null
PUT    /api/rate/UpdateRate/{reviewId}           → { chargingPointRate, comment } (PATCH also still works)
DELETE /api/rate/DeleteRate/{reviewId}
```
All three are **author-or-admin only**. ⚠️ Security fix included: the existing update endpoint previously let ANY logged-in user edit ANY review by id — it is now locked to the author/admin. Station averages self-correct after edit/delete.

## 6. F3 — worker approval workflow

- A **worker's** `POST .../notify` is stored as **`pending`** — nothing is delivered. Response: `{ status: "pending", notificationId }` → partner app shows "sent for approval".
- Owner (or admin — not the worker) decides:
```
GET /api/provider/favorites/{type}/{id}/notifications/pending     → the approval queue
PUT /api/provider/favorites/notifications/{id}/approve            → delivered NOW
PUT /api/provider/favorites/notifications/{id}/reject             → discarded
```
- The **2-sends/24h limit counts only actual sends** — submissions and rejections are free; the limit is enforced at approval time.
- `GET /api/admin/attention-summary` now returns a real `pendingWorkerNotifications` count.

## 7. F4 — auto-approve toggle

```
PUT /api/provider/favorites/{type}/{id}/auto-approve-worker-notifications   { "enabled": true }
```
Owner or admin only (workers get 403 — they can't unlock their own sends). Default **off**. When on, worker sends go out directly.

## 8. F5 — structured partner send

`POST /api/provider/favorites/{type}/{id}/notify` now takes:
```jsonc
{ "notificationTypeId": 13, "body": "…" }     // title built server-side: "{typeName} From {stationName}"
{ "title": "…", "body": "…" }                 // legacy raw title — still accepted
```
Note: type *names* are the code-style values (`provider_announcement`, `offer_available`…), so a built title looks like "provider_announcement From محطة X". If you want prettier titles, tell us the display names you want per type (EN/AR) and we'll add display-name columns — the mechanism is ready.

Suggestion templates:
```
GET    /api/notification-templates?notificationTypeId=13    (any authenticated user — partner chips)
POST   /api/notification-templates                          (admin) { notificationTypeId, body } → id
PUT    /api/notification-templates/{id}                     (admin)
DELETE /api/notification-templates/{id}                     (admin)
```

## 9. F6 — tap opens the sending station

Covered by R2/R3: both the push tap (FCM `type`+`chargerId`+`deepLink`) and the inbox tap (`deepLink`) now land on the sender's station/provider. Verified end-to-end on dev.

---

## 10. Verified live on dev (all ✅)

types list returns `deepLinksTo` · admin template CRUD + non-admin 403 · worker submit → `pending` with server-built title · attention-summary `pendingWorkerNotifications` = 1 · worker self-approve → 403 · owner approve → delivered, inbox rows carry `cable://charging-point?targetId=42` + `chargerId` in data · owner reject → discarded, nothing delivered · owner toggles auto-approve (worker toggle → 403) → worker send goes direct · 3rd actual send in 24h → 400 rate-limit · history shows sent/rejected/pending with recipient/delivered/read counts (readCount verified = 1 after a read) · GetMyReview / PUT own / PUT someone else's → 403 / DELETE own → null · admin structured-target send → push delivered with routing data + inbox deepLink built

## 11. Data model changes (already on dev; in the master prod script as PART 13)

- `NotificationType.DeepLinksTo` (+ seeded map)
- `ProviderFavoriteNotification`: `Status`, `NotificationTypeId`, `BatchId`, `DeliveredCount`, `DecidedByUserId`, `DecidedAt`, `SentAt`
- `ChargingPoint.AutoApproveWorkerNotifications`, `ServiceProvider.AutoApproveWorkerNotifications` (default 0)
- New table `NotificationTemplate`
