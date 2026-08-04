# Provider Favorites — Counts, Fans List & Announcements

**Date:** 2026-07-26 · **Status:** ✅ Implemented and live-tested on dev.
**Scalar:** open `/Cable-API/v1` → tags **Provider** / **Charging Points**. All endpoints below need a provider `Authorization: Bearer <token>`.

Three features that let a provider see and reach the users who favorited
their charging point or service business.

---

## 1. `favoritesCount` on the provider's own lists

No new endpoint — the existing owner endpoints now include how many users
favorited each asset:

| Endpoint | Where the field appears |
|---|---|
| `POST /api/charging-points/GetMyChargingPoints` | on each station |
| `GET /api/provider/my-assets` | on each station **and** each service provider |
| `GET /api/provider/service-providers/my` | on each service provider |

```json
{ "id": 174, "name": "محطة واحة معان", "...": "...", "favoritesCount": 5 }
```

Note: the field is only filled on these owner-scoped endpoints. On public
lists it is `null` (not computed) — this keeps the public map/list fast.

---

## 2. Who favorited — the fans list

```
GET /api/provider/favorites/{providerType}/{providerId}?page=&pageSize=
    providerType = ChargingPoint | ServiceProvider
```

**Who can call it:** the provider's owner, its assigned worker, or an admin.
Anyone else → `403`.

**Response** (optionally paged — omit page/pageSize for the full list):

```json
{
  "items": [
    { "userId": 6132, "name": "Ali", "city": "Amman", "favoritedAt": "2026-04-02T05:44:18" }
  ],
  "totalCount": 5, "page": 1, "pageSize": 2,
  "totalPages": 3, "hasNextPage": true, "hasPreviousPage": false
}
```

Newest first. **Deliberately excludes phone/email** — providers see who likes
them, not how to contact them directly (that's what feature 3 is for).

**Errors:** `400` unknown providerType · `403` not owner/worker/admin · `404` provider doesn't exist.

---

## 3. Send an announcement to your fans

```
POST /api/provider/favorites/{providerType}/{providerId}/notify
Body: { "title": "عرض اليوم في محطتنا", "body": "شحن مجاني لأول ١٠ زبائن هذا المساء" }
```

Sends to **every user who favorited the provider**:
- an **FCM push** to all their registered devices, and
- an **inbox notification** in the app (visible even if push permission is off),
  with `data: { "providerType", "providerId", "providerName" }` — use it to
  deep-link the tap into the station/provider screen.

**Response:**

```json
{ "recipientCount": 5, "pushDeliveredCount": 3, "remainingSendsToday": 1 }
```

- `recipientCount` — fans who got the inbox notification (always all of them)
- `pushDeliveredCount` — devices Firebase accepted the push for (can be lower:
  no token, stale token, permission denied)
- `remainingSendsToday` — sends left for this provider in the rolling 24h window

**Rules & limits:**

| Rule | Value |
|---|---|
| Who can send | owner, assigned worker, or admin (else `403`) |
| Rate limit | **2 sends per provider per rolling 24 hours** → 3rd attempt `400` |
| `title` | required, max 100 chars |
| `body` | required, max 500 chars |
| No fans yet | `400` "No users have favorited this provider yet." |
| Audit | every send is permanently recorded server-side (who, what, to how many) |

**Notification type** for the FE inbox filter: `provider_announcement`.

---

## Trying it in Scalar

1. Authenticate as a provider (`POST /api/provider/authenticate`) and paste the
   token into Scalar's Auth box.
2. `GET /api/provider/my-assets` → note an asset's `id` and its `favoritesCount`.
3. `GET /api/provider/favorites/ChargingPoint/{id}` → see the fans.
4. `POST /api/provider/favorites/ChargingPoint/{id}/notify` with a title/body →
   check `recipientCount` and `remainingSendsToday` in the response.
5. Call it twice more to see the `400` rate-limit response shape.

---

## Also recently added (previous update, in case you missed it)

- **`createdAt` / `modifiedAt`** now returned on station lists, station by-id,
  and all complaint list endpoints (Jordan time, `modifiedAt` null when never
  edited).
