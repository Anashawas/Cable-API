# Edit Station Request — BE Response (R1 + R2 + R3 notifications)

**Date:** 2026-07-13 · **Re:** `EDIT_STATION_REQUEST_BE_REQUIREMENTS.md`
**Status:** ✅ R1 whitelist, R2 diff/snapshot, R3 must-haves (incl. FCM notifications) all implemented and live-tested.

---

## Corrections to the requirements doc (checked against the code)

These items were listed as gaps but **already exist** — no FE workarounds needed:

| Doc claim | Reality |
|---|---|
| "Two Pending requests can coexist" | ❌ Never true in this build — submit returns **400** while a Pending request exists for the station |
| "Return rejectionReason to the owner" | Already returned in `my-requests` (and stored by `/reject`) |
| "New endpoint: GET update-requests/{id}" | Already existed; now returns the new diff shape (below) |
| "ownerId / ownerEmail / ownerName / ownerAccountPhone currently allowed" | The BE **never accepted** these on submit — they don't exist in the request contract. (`ownerPhone` = the station's contact number, which your whitelist allows.) Ownership transfer stays admin-only via ChangeOwner |

---

## R1 — Whitelist (server-enforced) ✅

The submit contract now accepts **exactly** the owner-editable set:

```
name, note, countryName, cityName, phone, methodPayment, price,
fromTime, toTime, chargerSpeed, chargersCount, latitude, longitude,
statusId (open/closed — NEW), ownerPhone, service, offerDescription,
address, plugTypeIds, attachmentsToDelete
(+ icon & attachments via the existing upload endpoints)
```

- **Removed** (were accepted before, now rejected structurally — unknown JSON
  fields are ignored, nothing reaches the DB): `chargerPointTypeId`,
  `stationTypeId`, `hasOffer`.
- **Added `statusId`** — validated against the Status lookup; applied on approve.
- Phones are normalized server-side and stored only when actually different.

### 🟡 Your product decisions (parked — tell us and we flip them)
- **`price`** — correction: it **is** currently owner-editable via update request. Say the word to remove it.
- **`cityName`/`countryName`** — still free text; if you want a controlled list, we need the list source.

## R2 — Old + new diff ✅

- **Old values are snapshotted at submit time** into the request row
  (`OldValuesJson`). Live-tested: station changed *after* submit → the diff
  still shows the submit-time baseline. Approve still applies the requested
  new values.
- All three endpoints return the same shape:

```json
{
  "id": 5, "chargingPointId": 174, "chargingPointName": "محطة واحة معان",
  "requestedByUserName": "...", "requestStatus": "Pending", "createdAt": "...",
  "rejectionReason": null,
  "changes": [
    { "field": "phone",    "oldValue": "962786660310", "newValue": "962791111222" },
    { "field": "fromTime", "oldValue": null,           "newValue": "05:00" },
    { "field": "status",   "oldValue": "مفتوح",        "newValue": "مغلق" },
    { "field": "plugTypes","oldValue": ["Type 2"],     "newValue": ["Type 2", "CCS2"] }
  ],
  "attachments": [ "https://.../photo1.jpg" ],
  "riskFlags": [ "contact_phone_changed" ]
}
```

- `POST .../update-requests/pending` → full diff per row (was metadata-only)
- `POST .../update-requests/my-requests` → same shape + `rejectionReason`
- `GET  .../update-requests/{id}` → same + detailed `attachmentChanges`
- Only changed fields appear; lookups render as **names** (status, plug types);
  icon changes as URLs; attachment deletions as an `attachmentsToDelete` entry.

### Risk flags (computed, R3.4 done early)
`name_changed` · `contact_phone_changed` · `owner_contact_changed` ·
`location_moved (123 m)` — distance computed from the snapshot coordinates.

---

## R3.3 — Notifications ✅

| Event | Who gets it | Content |
|---|---|---|
| Owner submits a request | **All admins** (role 2) | "طلب تعديل محطة جديد" + who and which station |
| Admin approves | **The requesting owner** | "تمت الموافقة على طلب التعديل" |
| Admin rejects | **The requesting owner** | "تم رفض طلب التعديل" **including the rejection reason** |

- Each event sends an **FCM push** to every registered device of the targets
  AND writes a **notification-inbox row** (so it appears in the in-app
  notification list even for users without a device token — e.g. admins on
  the web portal today).
- `data` payload on the inbox record: `{"updateRequestId": 6, "chargingPointId": 174, "approved": false}`
  — use it to deep-link into the request review screen.
- New notification types: `update_request_submitted`, `update_request_decided`
  (resolved by name; seeded by the deploy script).
- **Best-effort by design**: a Firebase failure is logged and swallowed —
  submit/approve/reject never fail because of a notification.
- Push `data` payload on the FCM message itself is not included (the current
  send service is title/body only, consistent with all existing pushes); the
  deep-link data lives on the inbox record. Tell us if the apps need it on the
  push itself.

## Verified end-to-end (dev)

submit with whitelisted + forbidden fields → 201, forbidden fields ignored ✅ ·
diff shows old/new with normalized phone + status names ✅ · station tampered
after submit → baseline unchanged ✅ · pending list carries diff + flags ✅ ·
approve applies phone/fromTime/status/note in one transaction ✅ · second
pending for same station blocked ✅ · submit → admins get inbox notification
with data payload ✅ · reject → owner notified with Arabic reason intact ✅ ·
no registered device tokens → operations still succeed (non-blocking) ✅

## Deploy

Run `Scripts/EditStationRequest_Phase1.sql` on production **before** deploying
(adds `StatusId` + `OldValuesJson`, drops the three de-whitelisted columns —
no data loss, both DBs have no historical requests — and seeds the two
notification types). Dev DB already migrated.

## Remaining (needs your product decisions first)

1. Auto-approve low-risk fields (need your field list + config flag)
2. Field-level approve/reject · deeper validation (`fromTime < toTime`, coordinate bounds)
3. `price` — remove from owner-editable? · `cityName`/`countryName` controlled list — source?
