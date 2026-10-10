# Partner app — Cable Connect and worker permissions (mobile spec)

> For the partner (station owner) mobile team. Build the same screens and behaviour as the
> **partner web portal**, menu **"Cable Connect"** and **Workers** (source:
> `D:\Cable\cable-partner\src\features\connect` and `…\features\workers`). Open the web portal
> next to the app while building — it is the reference for layout, wording and every edge case.
> Arabic and English strings: `cable-partner/public/locales/{ar,en}/connect.json` and `workers.json`
> (reuse them as-is).
>
> API base: dev `http://dev.cable-app.com`, production `https://cable-app.com`. All endpoints need the
> provider login (email + password → OTP → tokens). Times from the API are **UTC**. Money in
> **fils** (`costFils`) with `costJod = fils / 1000`. Energy in kWh.
>
> Errors: RFC 7807 problem details. Show `detail` to the user — it is written for them. 401 → refresh
> the token / re-login. 403 → the user may not do this on this station (owner-only, or a worker
> without the permission).

---

## 0. Before anything: what the signed-in user may do

`GET /api/provider/my-assets` now returns `access[]` next to `chargingPoints` and `serviceProviders`:

```json
{
  "chargingPoints": [ { "id": 210, "name": "محطة الرشدان لشحن المركبات", "...": "..." } ],
  "serviceProviders": [],
  "access": [
    { "providerType": "ChargingPoint", "providerId": 210, "isOwner": false,
      "privileges": ["ConnectView", "PointOfSale", "ViewFeedback"] }
  ]
}
```

- Owners get `isOwner: true` and every key. Workers get what the owner granted.
- Use the entry of the **selected asset** to show / hide menu items and buttons. Re-read it after login
  and when the app returns to the foreground (the owner may have changed it).
- **Clear every cached response on logout and on login.** The web had a bug where the previous
  account's cache made a worker see the owner's menu until a refresh.

| Key | Unlocks | Enforced by the API |
|---|---|---|
| `ConnectView` | Cable Connect screen: live plugs, sessions, session detail, command history, card list | ✅ |
| `ConnectControl` | Start / Stop / Unlock / out of service / Refresh / Restart, add / switch off cards, sharing switch, charger names | ✅ |
| `PointOfSale` | Generate QR / create and cancel transactions | ✅ |
| `ManageOffers` | Offers | UI only |
| `SendAnnouncements` | Messages to followers | UI only |
| `ViewWallet` | Wallet and settlements | UI only |
| `ViewFeedback` | Reviews and complaints | UI only |
| `ViewStatistics` | Statistics | UI only |
| `ManageStation` | Edit station details (sent for review) | UI only |

Station details are always visible. The **Workers** screen is owner-only (hide it for workers;
the API refuses worker management from a worker anyway).

`ConnectControl` implies `ConnectView`: never show controls without the view.

---

## 1. Cable Connect screen (menu item, gated by `ConnectView`)

Scoped to the selected station (`providerType = ChargingPoint`). Layout from top to bottom, as on the web:

1. KPI tiles
2. "Today" tiles
3. One card per charger, with its plugs and the live session panel under a busy plug
4. "Live data for drivers" panel with the "what drivers see" preview
5. Sessions list
6. Accepted cards

Polling: the live picture every **5 s** while any plug has a session or a car waiting
(`openSessionId` set or `ocppStatus = "Preparing"`), otherwise every **15 s**. The live session
detail every **5 s** while open. After a command succeeds, refetch at +2 s, +5 s, +10 s so the
effect shows before the next poll. Stop polling when the screen is not visible.

### 1.1 Station live picture

`GET /api/provider/charging-points/{stationId}/live`

```json
{
  "chargingPointId": 210, "available": true, "unavailableReason": null, "updatedAt": "2026-10-09T20:58:09Z",
  "reliable": null, "reliabilityPct": 9.9,
  "today": { "sessions": 2, "energyKwh": 41.5, "costFils": 7431, "costJod": 7.431, "running": 1 },
  "plugTypes": [ { "plugTypeId": null, "name": "Other", "family": null, "total": 4, "free": 0, "busy": 2, "outOfOrder": 0, "unknown": 2, "maxPowerKw": null } ],
  "chargers": [
    { "id": 7, "ordinal": 2, "displayName": "Test 1", "online": true, "updatedAt": "…",
      "plugs": [
        { "connectorId": 1, "state": "Busy", "plugTypeId": null, "plugTypeName": null, "powerKw": null, "ocppStatus": "Charging",  "openSessionId": 36 },
        { "connectorId": 2, "state": "Busy", "plugTypeId": null, "plugTypeName": null, "powerKw": null, "ocppStatus": "Preparing", "openSessionId": null }
      ] }
  ]
}
```

- `unavailableReason = "NoChargers"` → empty state "No chargers connected yet — the Cable team registers them".
- `unavailableReason = "Offline"` → warning banner "All chargers are offline right now".

**KPI tiles**: chargers online / total (`online`), plugs free / total (`state = Free`), cars charging
(`state = Busy`), reliability % (`reliabilityPct`, "—" until the first full day).

**Today tiles** (from `today`): sessions today (+ "N running now"), kWh today, revenue today in JOD.

### 1.2 Charger card

- Title: `displayName` or "Charger {ordinal}". Pencil icon to rename (needs `ConnectControl`):
  `PUT /api/provider/charging-points/{stationId}/chargers/{chargerId}/display-name` `{ "displayName": "الكابينة اليمنى" }`
  (max 100 chars, `null` = back to the default). Drivers see this name.
- Status chip: **Online** (green) · **Offline** (grey) · **Not connected yet** (orange) when offline and
  `updatedAt` is null — a charger registered by Cable that never connected; show "Plugs appear after the
  first connection" instead of the plug list.
- Card actions (need `ConnectControl`): **Refresh** and **Restart** (see 1.4). **History** toggles the
  command history (needs `ConnectView`).

**Each plug row**: "Plug {n}", a state chip (Free green · Busy orange · Out of order red · Unknown grey),
the charger's own status in words when not Available (`ocppStatus`: Preparing = "car connected, waiting",
Charging, SuspendedEV = "car paused", SuspendedEVSE = "charger paused", Finishing = "finished, still plugged in",
Unavailable = "out of service", Faulted = "fault"), plug type and kW, then the buttons:

| Button | Show when | Request (body) |
|---|---|---|
| **Start session** (green) | `ocppStatus = "Preparing"` and no `openSessionId` | `remote-start` `{ "connectorId": 1 }` |
| "Plug in first" (grey chip, not a button) | `ocppStatus = "Available"` and no `openSessionId` | — |
| **Stop** (red) | `openSessionId` set | `remote-stop` `{ "transactionId": 36 }` |
| **Unlock cable** | always; disabled while `Charging` | `unlock-connector` `{ "connectorId": 1 }` |
| **Out of service** / **Back in service** | `ocppStatus != "Unavailable"` / `= "Unavailable"` | `change-availability` `{ "connectorId": 1, "type": "Inoperative" \| "Operative" }` |

All buttons disabled while the charger is offline or another command is in flight. Hide all of them for a
worker without `ConnectControl` and show the info line "You can see this station's chargers and sessions.
Starting, stopping and other controls need a permission from the owner."

The API refuses a start on an empty plug ("Plug the cable into the car first"), so the app must not offer it.

### 1.3 Live session panel (under a plug with `openSessionId`)

`GET /api/provider/charging-points/{stationId}/sessions/{openSessionId}/live` — every 5 s while `isOpen`.

```json
{
  "id": 36, "chargerName": "Test 1", "connectorId": 1, "startedAt": "2026-10-09T20:59:46Z", "stoppedAt": null,
  "isOpen": true, "durationSec": 23, "startSource": "Operator",
  "energyKwh": 0.28, "powerW": 40400, "socPercent": 40, "voltageV": 398.5, "currentA": 101.4,
  "lastSampleAt": "…", "secondsSinceSample": 2, "maxPowerW": 40400, "avgPowerW": 40100,
  "costFils": 53, "costJod": 0.053, "currentRateFils": 193, "currentWindowNameEn": "Partial peak", "currentWindowNameAr": "ذروة جزئية",
  "price": [ { "key": "night", "nameEn": "Partial peak", "nameAr": "ذروة جزئية", "kwh": 0.28, "priceFils": 193, "fils": 53 } ],
  "series": [ { "at": "…", "powerW": 40400, "socPercent": 40, "energyKwh": 0.28 } ]
}
```

Show, in this order (the web is the reference look):

- Header: pulsing green dot, "Charging now · #36", source chip (Card / Driver app / You-staff), "updated N s ago"
  (`lastSampleAt`; orange when over 60 s).
- Four figures: **Power** `powerW/1000` kW (animated count) with "peak X kW"; **Energy** kWh with the battery %
  or "battery not reported"; **Elapsed** clock ticking every second on the device from `startedAt`, with "avg X kW";
  **Cost so far** JOD with "{currentRateFils} fils/kWh · {window name}".
- Battery progress bar when `socPercent > 0`.
- Chart: power over time as a filled area, battery % as a dashed line, from `series` (≤ 120 points, oldest first).

### 1.4 Commands

`POST /api/provider/charging-points/{stationId}/chargers/{chargerId}/commands/{action}`

| action | body |
|---|---|
| `remote-start` | `{ "connectorId": n }` |
| `remote-stop` | `{ "transactionId": id }` |
| `unlock-connector` | `{ "connectorId": n }` |
| `change-availability` | `{ "connectorId": n, "type": "Operative" \| "Inoperative" }` |
| `reset` | `{ "type": "Soft" }` (the web offers Soft only — "Restart") |
| `trigger-message` | `{ "requestedMessage": "StatusNotification" }` ("Refresh") |

Every command needs a **confirm dialog** (texts in `connect.json` → `confirm.*`). Response:

```json
{ "commandId": 50, "action": "RemoteStartTransaction", "status": "Answered", "resultStatus": "Accepted",
  "responsePayload": "{\"status\":\"Accepted\"}", "errorCode": null, "errorDescription": null, "elapsedMs": 118, "accepted": true }
```

- `status != "Answered"` → red toast with the reason: `NotConnected` (charger not connected), `Timeout` (no answer
  within 30 s), `Disconnected`, `Unreachable` (Cable's charger server unreachable), `CallError` (refused by the charger).
- `accepted = false` → red toast with `resultStatus`.
- Limits (show `detail`): 10 commands per charger per minute; the same unconfirmed command is refused for 90 s.

### 1.5 "Session finished" card

When a plug that had `openSessionId` loses it on the next poll, keep the session's panel on that plug for
**60 s** in a "finished" style: blue check, "Session finished · #id", the stop reason in words, final kWh, peak
power, duration and **price**, a close (×) button, no chart. Same endpoint as 1.3 with the old id
(it now returns `isOpen: false` and the stored price).

### 1.6 Command history (per charger, "History")

`GET /api/provider/charging-points/{stationId}/chargers/{chargerId}/commands?take=20`

Items: `{ id, action, status, resultStatus, errorCode, errorDescription, durationMs, requestedByName, createdAt, completedAt, confirmedAfterSec }`.
Columns: when · command (translated action name) · result chip (green when answered and not Rejected /
NotSupported / UnlockFailed) + "done after N s" (`completedAt` set) or "not confirmed yet" · by.

### 1.7 Live data for drivers (sharing) + what drivers see

`GET /api/provider/charging-points/{stationId}/live-visibility`

```json
{ "visibleToDrivers": true, "subscriptionOn": true, "ownerSharing": true, "ownerDecidedAt": "…",
  "adminBlocked": false, "adminBlockedAt": null, "adminBlockReason": null }
```

- Three gates with ✓ / ✗: Cable Connect subscription active · Approved by Cable (`!adminBlocked`, show the reason when
  blocked) · Sharing switched on by you.
- **What drivers see right now** (dashed box): when `visibleToDrivers` and at least one charger is online, one chip per
  `plugTypes[]` entry from 1.1: "{name} · {free} of {total} free" (green when `free > 0`), plus a "Reliable" chip when
  `reliable = true`. Otherwise a grey line: "Nothing — the station is hidden from drivers" or "Nothing — no charger is online".
- Switch "Share my plug states with drivers" (needs `ConnectControl`):
  `PUT /api/provider/charging-points/{stationId}/live-visibility/share` `{ "share": true | false }` → returns the same object.

### 1.8 Sessions list

`GET /api/provider/charging-points/{stationId}/sessions?page=1&pageSize=20` (optional `chargerId`, `from`, `to` in UTC)

```json
{ "items": [ { "id": 30, "ocppChargePointId": 1, "chargerName": "Demo charger (simulator)", "connectorId": 1, "idTag": "CBL-S223",
               "startedAt": "…", "stoppedAt": "…", "durationSec": 85, "energyKwh": 3.0, "stopReason": "Remote", "isOpen": false,
               "startSource": "Operator", "stopReasonText": "Stopped from the app", "costFils": 639, "costJod": 0.639 } ],
  "totalCount": 5, "page": 1, "pageSize": 20 }
```

Columns: started (+ "running" chip) · charger · plug · duration · energy · price (JOD, 3 decimals) · started by
(`Card` / `App` = Driver app / `Operator` = You-staff) · ended (`stopReasonText`, empty while running).
**Tap a row** → bottom sheet / dialog with the session panel from 1.3 (chart + price breakdown). Show `idTag` only in
that detail; it is a card number or a virtual tag (`CBL-U…` app, `CBL-S…` operator).

### 1.9 Accepted cards

- List: `GET /api/admin/ocpp/authorized-tags?chargingPointId={stationId}&includeDisabled=true`
  → `[{ id, chargingPointId, idTag, label, isEnabled, expiresAt, createdAt }]`
- Add (needs `ConnectControl`): `POST /api/admin/ocpp/authorized-tags` `{ "chargingPointId": 210, "idTag": "12345678", "label": "Ahmad", "expiresAt": null }`
  — number upper-cased, max 20 chars; the chargers receive the new list within seconds.
- Switch off / on (needs `ConnectControl`): `PUT /api/admin/ocpp/authorized-tags/{cardId}/enabled` `{ "isEnabled": false }`.
- These live under `/api/admin/ocpp` but allow the station's owner and its workers with the privilege.

---

## 2. Workers screen — permissions (owner only)

Existing screen, extended. The worker record:

`GET /api/workers?providerType=ChargingPoint&providerId={stationId}` → `null` / 404 when none, else

```json
{ "providerManagerId": 13, "userId": 14118, "name": "…", "phone": "+962790000098", "email": "…",
  "isActive": true, "assignedAt": "…",
  "privileges": ["ConnectView", "ConnectControl", "PointOfSale", "…"], "usesAllPrivileges": true }
```

Other worker calls (unchanged, listed for completeness):

| Call | Request |
|---|---|
| Create | `POST /api/workers` `{ providerType, providerId, name, email, phone, password }` |
| Suspend / activate | `PATCH /api/workers/{providerManagerId}/active?isActive=true` — **query parameter, no body** (the web sent a body and failed with 500; fixed Oct 10) |
| Remove | `DELETE /api/workers/{providerManagerId}` |
| **Set permissions** | `PUT /api/workers/{providerManagerId}/privileges` `{ "privileges": ["ConnectView", "PointOfSale"] }` → the updated worker |
| Permission keys | `GET /api/workers/privileges` → `["ManageStation", "PointOfSale", …]` |

**Permissions UI** (web reference: `WorkerPermissions.tsx`): a section "What the worker can see and do" on the
worker card, with a summary chip (Everything · Nothing · "5 of 9"), "Allow everything" and "Nothing" shortcuts, then
**four groups of toggle switches**, each row with an icon, a title and a one-line description:

| Group | Switches |
|---|---|
| Cable Connect | See chargers and sessions (`ConnectView`) · Operate the chargers (`ConnectControl`) |
| Sales and money | Generate QR / point of sale · Offers · Wallet and settlements |
| Customers | Messages to customers · Reviews and complaints |
| Station | Statistics · Edit station details |

- Turning **on** "Operate the chargers" also turns on "See chargers and sessions"; turning **off** "See" also turns off "Operate".
- Nothing is sent until **Save** (enabled only when something changed; show "Unsaved changes").
- Empty list = station details only. All keys = everything (also keys added later).
- Existing workers keep everything until the owner changes it.

---

## 3. Push notifications — required

`PUT /api/notification-token` after login and on every token refresh:

```json
{ "token": "<FCM token>", "osName": "android", "osVersion": "14", "appVersion": "2.3.0", "appType": 2, "language": "ar" }
```

`appType: 2` = partner app. Without it the owner gets inbox entries only and **no push** for: charger offline
(15 min), back online, plug faulted (immediately and after 15 min), fault cleared, session open too long (6 h),
car parked after charging (20 min after the driver was told, or immediately when no driver is known). The minutes are
per station (set by the Cable team).

---

## 4. Not in the partner app

Registering a charger and the connection sheet, hard reset, reading / changing charger settings, the raw message log,
alert thresholds, blocking a station's live data — all Cable admin only.

---

## 5. Test on dev

Station **210 محطة الرشدان** has a simulator charger "Test 1" with a car on each plug when the Cable team runs it
(`--remote` demo mode): both plugs show "car connected, waiting", Start works on each, sessions charge at ~40 / 48 kW,
a new car arrives 10 s after every stop. Test owner and worker accounts are held by the backend team (ask for them).

1. Owner: Cable Connect → tiles, two charger cards, Start on plug 1 → live panel with growing chart → Stop → "Session
   finished" card for a minute → row in Sessions with price → tap → detail.
2. Owner: Start plug 1 and plug 2 together; "what drivers see" shows 0 of N free.
3. Owner: sharing off → preview greys out; on again.
4. Owner: add a card, switch it off.
5. Owner → Workers: switch off "Operate the chargers", Save. Worker (log in on another device): Cable Connect shows
   everything but no buttons and the info line; Workers is not in the menu.
6. Owner: switch off "See chargers and sessions" → the worker's Cable Connect menu item disappears; a direct call
   returns 403.
7. Worker suspend / activate on the worker card.
