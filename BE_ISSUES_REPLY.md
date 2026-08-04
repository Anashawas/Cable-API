# Reply to "BE-issues-and-bugs.md" — Backend Answers

Thanks for the thorough verification pass — great to see everything confirmed working across all four roles. Here are the answers to every open item. **Both decisions are settled below**, so you can send us the display names and lock your maps.

---

## DECISION-1 — favorites types + final list ✅ DECIDED

First, an important fact we checked in the backend code: **`favorite_added` (2) and `favorite_removed` (3) are never emitted by anything today.** They exist only as lookup rows — no push and no inbox record has ever been created with either type by the system. So:

1. **Recipient:** decided — if/when `favorite_added` is ever emitted, it goes to the **station owner** ("someone favorited your station"), never to the user who tapped favorite. It is **not currently sent**; treat it as *reserved* for a future partner-facing feature.
2. **`favorite_removed`:** **dropped from the contract.** Nothing emits it and nothing ever will. We keep the DB row only for referential integrity (deleting a lookup id is riskier than ignoring it) — you can safely remove it from the app's icon/label map; it will never appear in an inbox or a push.
3. **Final locked list** (`id → name → deepLinksTo`) — lock your maps against this:

| id | name | deepLinksTo | emitted today? |
|---|---|---|---|
| 1 | system_announcement | none | yes (admin sends) |
| 2 | favorite_added | charging-point | **no — reserved** (future: to station owner) |
| 3 | favorite_removed | — | **DROPPED — never emitted, remove from app map** |
| 4 | charging_point_status_changed | charging-point | yes |
| 5 | offer_available | charging-point | yes |
| 6 | rating_received | charging-point | yes |
| 7 | complaint_status_updated | complaint | yes |
| 8 | charging_session_started | charging-point | yes |
| 9 | charging_session_completed | charging-point | yes |
| 10 | new_charging_point_nearby | charging-point | yes |
| 11 | update_request_submitted | charging-point | yes (to admins) |
| 12 | update_request_decided | charging-point | yes (to owner) |
| 13 | provider_announcement | provider (per-send deepLink decides station vs service-provider) | yes (partner fan announcements) |

Ids and names will not change again — this is the source of truth.

---

## DECISION-2 — partner auto-title ✅ DECIDED: Arabic, your format

Agreed on both points (code-style name + English connector are wrong for our market). **Decided format:**

```
"{nameAr} من {stationName}"        e.g. "عرض جديد من محطة الكبرى"
```

- Please send the display-names table `{ id, nameEn, nameAr }` for the final list above (§3 of your doc — DECISION-1 is now settled, so nothing is blocking it).
- On our side we'll add `nameEn`/`nameAr` to the notification types (they'll also appear on `GET /api/notification-types` so your pickers can show human labels), and the title builder switches to `nameAr + " من " + stationName`.
- Until the names land, the current title format stays; partners can also keep sending a raw `title` (that path is unchanged).

**Acceptance met once names arrive:** a send with `notificationTypeId` produces e.g. "عرض جديد من محطة الخليفة لشحن المركبات".

---

## Confirmations

**C-1 — `GetNearest` stays. Confirmed.** It is not deprecated and will not be removed; `GetNearestPremium`/`GetNearestNormal` are thin wrappers over the same logic, so all three are maintained together. Migrate at your own pace or not at all.

**C-2 — station view-image form key is `file` (singular). Confirmed** (checked in the route code: `[FromForm] IFormFile file`). And yes — the announcement image upload uses **`files`** (plural), exactly as your original spec §5.5e requested. The inconsistency is inherited from the spec; we're keeping both keys **as they are** so nothing breaks. Summary:

| Upload | Form key |
|---|---|
| `POST /api/charging-points/{id}/view-image` | `file` |
| `POST /api/home/admin/announcements/{id}/image` | `files` |

**R4 deepLink spot-check — confirmed working.** Your test hit the edge you suspected: you targeted an admin-only audience, so 0 B2C inbox rows were written (inbox records are only created for recipients whose push succeeded). We ran the same structured-target send against a real B2C recipient during our own tests and read the stored row back from the DB:

```
targetType: "charging-point", targetId: 42
→ inbox row: deepLink = "cable://charging-point?targetId=42"
→ FCM data: { "type": "stations", "chargerId": "42", "deepLink": "cable://charging-point?targetId=42" }
```

---

## BUG-1 latent-risk recommendation — ✅ IMPLEMENTED

Your diagnosis was exactly right: URLs were built from the **request host**, which is how a local-machine upload persisted `localhost:5202`. We've flipped it: **all image URLs are now built from the configured public base URL first** (`https://cable-app.com` on prod, `http://dev.cable-app.com` on dev), with the request host only as a fallback if the config is empty. Applies uniformly to `CableAnnouncements`, `CableViewImages`, `CableBanners`, `CableChargingPoint` and every other served folder. A bad host can no longer produce a bad stored URL. Ships with the next dev deploy.

---

## Frozen shapes — acknowledged 🔒

We treat these as locked contracts (no rename/reshape without coordinating with you first):

1. `GET /api/charging-points/view-image/pending` — paginated wrapper `{ items, totalCount, page, pageSize, totalPages, hasNextPage, hasPreviousPage }` with fields `chargingPointId / stationName / cityName / ownerId / ownerName / viewImage / uploadedAt`.
2. Announcement **flat** fields everywhere (app-facing + admin CRUD).
3. `actionType` = `null` (no button) or `1..5` — `0` stays rejected with 400; we will not "fix" it to accept 0.

---

## Summary

| Item | Answer |
|---|---|
| DECISION-1 | `favorite_removed` dropped (never emitted); `favorite_added` reserved, owner-facing; final list locked above |
| DECISION-2 | Arabic title `"{nameAr} من {stationName}"` — send us the `{id, nameEn, nameAr}` table |
| C-1 | `GetNearest` stays, not deprecated |
| C-2 | Station upload key = `file`; announcement = `files` (both frozen) |
| R4 spot-check | Verified against a real recipient — deepLink + FCM data correct |
| BUG-1 recommendation | Implemented — config-based public base URL for all image URLs |
| Frozen shapes | Acknowledged and locked |

**Next from you:** the 13-type display names `{ id, nameEn, nameAr }` — then the Arabic titles go live.
