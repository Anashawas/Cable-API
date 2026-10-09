# Cable Connect — mobile implementation spec (driver app + partner app)

> For the mobile team. Everything below is live on the dev API (`http://dev.cable-app.com`) once
> the Oct 9 build is published, and on the local stack today. The partner **web** portal
> (`D:\Cable\cable-partner`, menu "Cable Connect") implements the partner side one-to-one; use it
> as the reference for behaviour and wording. The admin portal shows the same data with more
> controls. Arabic strings for every label are in
> `cable-partner/public/locales/ar/connect.json` (partner) and can be reused.
>
> All times from the API are **UTC**. Money is in **fils** (`costFils`) with `costJod` = fils / 1000.
> Energy is kWh. Poll intervals below are what the web does; do not poll faster.

---

## 0. Vocabulary

| Term | Meaning |
|---|---|
| Station | A charging point (`chargingPointId`) — what the owner manages, what the driver sees on the map |
| Charger / cabinet | One OCPP unit at the station (`chargers[].id`). Drivers see "Charger 1" or the owner's name, never the OCPP id |
| Plug | One connector of a charger (`plugs[].connectorId`, 1-based) |
| Plug state | `Free` · `Busy` · `OutOfOrder` · `Unknown` (charger offline) |
| `ocppStatus` | The raw charger status, present while the charger is online: `Available`, `Preparing` (car connected, waiting), `Charging`, `SuspendedEV`, `SuspendedEVSE`, `Finishing` (done, still plugged), `Reserved`, `Unavailable`, `Faulted` |
| Session | One charging transaction (`id`), from StartTransaction to StopTransaction |
| `startSource` | `Card` (RFID at the charger) · `App` (driver app) · `Operator` (partner app / admin started it for the customer) |

Errors: every 4xx comes as RFC 7807 problem details — show `detail` to the user (it is written for them, e.g. "Plug the cable into the car first"). `errors[0].reasons[0]` carries the same text. 401 → re-login. 403 → the user may not act on that station.

---

## 1. Driver app (Cable)

### 1.1 Station page — live plugs

`GET /api/charging-points/{id}/live` (anonymous or logged in) — poll every **15 s** while the page is open.

```json
{
  "chargingPointId": 210,
  "available": true,
  "unavailableReason": null,
  "updatedAt": "2026-10-09T15:12:01Z",
  "reliable": true,
  "plugTypes": [
    { "plugTypeId": 17, "name": "CCS2", "family": "EURO", "total": 2, "free": 1, "busy": 1, "outOfOrder": 0, "unknown": 0, "maxPowerKw": 80 }
  ],
  "chargers": [
    { "id": 6, "ordinal": 1, "displayName": "الكابينة اليمنى", "online": true, "updatedAt": "…",
      "plugs": [
        { "connectorId": 1, "state": "Free", "plugTypeId": 17, "plugTypeName": "CCS2", "plugTypeFamily": "EURO", "powerKw": 40, "ocppStatus": "Available" },
        { "connectorId": 2, "state": "Busy", "plugTypeId": 17, "plugTypeName": "CCS2", "plugTypeFamily": "EURO", "powerKw": 80, "ocppStatus": "Charging" }
      ] }
  ]
}
```

- `available=false` → show nothing live, and explain with `unavailableReason`: `NoSubscription` / `NotShared` / `Blocked` → "Live availability is not offered at this station"; `NoChargers` → same; `Offline` → "Charger offline right now". Never show the reason codes themselves.
- Badge per plug type: "CCS2 · 1 of 2 free". Use `plugTypes[]`, not the chargers list.
- `reliable: true` → show a "Reliable" badge. `null` → nothing (never show a bad score to drivers).
- Charger title: `displayName ?? "Charger {ordinal}"`. Plug chips: colour by `state`. When `ocppStatus = Preparing` on a Free-looking plug, the state is already `Busy`.

### 1.2 Map / list badges

`GET /api/charging-points/live-summary?ids=1,2,3` (max 50 ids). Returns one entry per station **that has chargers**; stations missing from the response get no badge.

```json
[{ "chargingPointId": 210, "available": true, "unavailableReason": null, "updatedAt": "…",
   "plugTypes": [{ "plugTypeId": 17, "name": "CCS2", "free": 1, "total": 2 }] }]
```

### 1.3 Start charging from the app (no card)

Precondition in the UI: the user is logged in, the station is `available`, and the chosen plug has `ocppStatus = "Preparing"` (the driver plugged the cable in first). Show the plug as selectable only in that state; on an `Available` plug show "Plug the cable into your car first".

`POST /api/users/me/ocpp-sessions/start` (auth)
```json
{ "chargingPointId": 210, "chargerId": 6, "connectorId": 1 }
```
→ 200
```json
{ "commandId": 42, "accepted": true, "status": "Answered", "resultStatus": "Accepted",
  "idTag": "CBL-U14119", "message": "The charger accepted. Plug in if you have not yet; charging starts in a few seconds." }
```
- `accepted=true` → show "Starting…" and poll `/current` every **3 s** until it returns a session (usually 3–6 s; give up after 60 s with "The charger did not start. Try again or tap a card.").
- `accepted=false` → show `message`.
- 400 with `detail` when: the station does not offer app charging; the plug is empty ("Plug the cable into the car first"); the plug is busy; the charger is offline; the user already has a running session.

The driver's virtual tag (`CBL-U{userId}`) is created automatically on first use. Nothing to store on the device.

### 1.4 Live session screen (make it feel alive: big animated kW figure, battery bar, elapsed clock ticking on the device, "updated N s ago" — the partner web live panel is the reference look)

`GET /api/users/me/ocpp-sessions/current` (auth) — poll every **10 s** while open. Returns the literal `null` when nothing is running.

```json
{ "id": 29, "chargingPointId": 223, "stationName": "…", "chargerId": 1, "chargerName": "…", "connectorId": 1,
  "startedAt": "2026-10-09T10:44:30Z", "stoppedAt": null, "durationSec": 22, "energyKwh": 2.5, "isOpen": true,
  "startSource": "App", "stopReason": null, "stopReasonText": null, "stopReasonTextAr": null,
  "costFils": null, "costJod": null, "tariffVersion": null, "price": null,
  "powerW": 30000, "socPercent": 45, "lastSampleAt": "2026-10-09T10:44:51Z" }
```
Show: power now (`powerW` / 1000 kW), battery `socPercent` (may be null or 0 on cars that do not report it), energy so far `energyKwh`, elapsed `durationSec`, "updated X s ago" from `lastSampleAt`. Cost is **not** known while charging (null); it appears in the history after the stop.

Also show this screen for **card** sessions when the driver's card is linked to the account: the API returns them the same way (`startSource = "Card"`).

### 1.5 Stop from the app

`POST /api/users/me/ocpp-sessions/{id}/stop` (auth) → `CommandResult` (`accepted`, `resultStatus`). After `accepted=true`, keep polling `/current`; it becomes `null` within ~5 s. Only the caller's own sessions can be stopped (404 otherwise). The driver may always stop at the charger with the card too.

### 1.6 History and receipt

`GET /api/users/me/ocpp-sessions?page=1&pageSize=20` (auth) → paged `{ items, totalCount, page, pageSize, totalPages, hasNextPage, hasPreviousPage }`, same item shape as 1.4 plus, once closed:

```json
"stoppedAt": "…", "isOpen": false, "stopReason": "Remote", "stopReasonText": "Stopped from the app", "stopReasonTextAr": "أُوقف من التطبيق",
"costFils": 458, "costJod": 0.458, "tariffVersion": 1,
"price": [{ "key": "offPeak", "nameEn": "Off-peak", "nameAr": "خارج الذروة", "kwh": 2.5, "priceFils": 183, "fils": 458 }]
```
Receipt = station, charger, plug, start / end, duration, energy, `price[]` lines (window name × kWh × fils/kWh) and the total. **No payment is taken** — the price is informational until payment is built.

### 1.7 Push token and price alerts (already specified elsewhere)

`PUT /api/notification-token` with `appType` (driver) and `language` (`"ar"` | `"en"`). Needed for the "your car finished charging" alert and for price alerts. Tariff: `GET /api/pricing/tou`; preferences: `GET/PUT /api/users/me/price-alerts`.

### 1.8 Pushes the driver may receive

| Push | When | Data |
|---|---|---|
| Car parked after charging | plug `Finishing` / `SuspendedEV` for 20 min (per-station value) and the session is the driver's | opens the live session / station page |
| Price alert | before a tariff window starts, per the user's preferences | opens the tariff screen |

---

## 2. Partner app (station owner / managers)

Reference implementation: partner web → "Cable Connect". All endpoints need the provider login; the owner-or-manager check is per station (403 otherwise). The web polls the live picture and the sessions every **15 s**.

### 2.1 Station live picture

`GET /api/provider/charging-points/{id}/live` — same shape as 1.1 **without the driver gates**, plus `reliabilityPct` (number, may be null until the first full day) and, per plug, `openSessionId` (the running session on that plug, for Stop).

KPIs on the web: chargers online / total, plugs free / total, cars charging (`state = Busy`), reliability %.

### 2.2 Charger card (per cabinet)

- Title: `displayName ?? "Charger {ordinal}"`, editable → `PUT …/chargers/{chargerId}/display-name` `{ "displayName": "…" | null }` (max 40 chars; null restores the default). Drivers see this name.
- Online / offline chip, `updatedAt`.
- Cabinet actions: **Refresh** (`trigger-message` `{ "requestedMessage": "StatusNotification" }`), **Restart** (`reset` `{ "type": "Soft" }`).
- Per plug: state chip + `ocppStatus` text + plug type + kW, then:
  - **Start session** — only when `ocppStatus = "Preparing"` and no `openSessionId`: `POST …/chargers/{chargerId}/commands/remote-start` `{ "connectorId": n }`. On `Available` show "Plug in first" instead of the button (the API refuses an empty plug).
  - **Stop** — when `openSessionId` is set: `POST …/commands/remote-stop` `{ "transactionId": openSessionId }`.
  - **Unlock cable** — `POST …/commands/unlock-connector` `{ "connectorId": n }`; disabled while `Charging`.
  - **Out of service / Back in service** — `POST …/commands/change-availability` `{ "connectorId": n, "type": "Inoperative" | "Operative" }`.
- Every command needs a confirm dialog (texts in `connect.json` → `confirm.*`) and returns:
  ```json
  { "commandId": 50, "action": "RemoteStartTransaction", "status": "Answered", "resultStatus": "Accepted",
    "responsePayload": "{\"status\":\"Accepted\"}", "errorCode": null, "errorDescription": null, "elapsedMs": 118, "accepted": true }
  ```
  `status != "Answered"` → the charger did not answer (`NotConnected`, `Timeout`, `Disconnected`, `Unreachable`): toast in red. `accepted=false` → the charger refused (`resultStatus`). After a success, refetch the live picture at +2 s, +5 s, +10 s so the new state shows before the next poll.
- Limits enforced by the API (show the `detail`): 10 commands per charger per minute; the same unconfirmed command is refused for 90 s.
- Command history: `GET …/chargers/{chargerId}/commands?take=20` → list of `{ id, action, status, resultStatus, requestedByName, createdAt, completedAt, confirmedAfterSec, errorDescription }`. `completedAt` null on an accepted command = "not confirmed yet" (the charger has not yet sent the message that proves it).

### 2.3 Sharing switch

`GET /api/provider/charging-points/{id}/live-visibility` →
```json
{ "visibleToDrivers": true, "subscriptionOn": true, "ownerSharing": true, "ownerDecidedAt": "…", "adminBlocked": false, "adminBlockedAt": null, "adminBlockReason": null }
```
Three gates: subscription (Cable team), Cable approval (`!adminBlocked`, reason shown when blocked), the owner's switch. `PUT …/live-visibility/share` `{ "share": true | false }` returns the same object.

### 2.4 Sessions with price

`GET /api/provider/charging-points/{id}/sessions?page=1&pageSize=20[&chargerId=&from=&to=]` → paged items:
```json
{ "id": 30, "ocppChargePointId": 1, "chargerName": "…", "connectorId": 1, "idTag": "CBL-S223",
  "startedAt": "…", "stoppedAt": "…", "durationSec": 85, "energyKwh": 3.0, "stopReason": "Remote", "isOpen": false,
  "startSource": "Operator", "stopReasonText": "Stopped from the app", "costFils": 639, "costJod": 0.639 }
```
Columns on the web: started (+ "running" chip), charger, plug, duration, energy, price (JOD, 3 decimals), started by (`Card` / `Driver app` / `You / staff`), ended (`stopReasonText`). Show `idTag` only in a detail view; it is a card number or a virtual tag.

### 2.4b Live session detail (the "wow" screen)

`GET /api/provider/charging-points/{id}/sessions/{transactionId}/live` — poll every **5 s** while `isOpen`, once when closed.
```json
{ "id": 33, "chargerName": "Test 1", "connectorId": 1, "startedAt": "…", "isOpen": true, "durationSec": 184, "startSource": "Operator",
  "energyKwh": 2.03, "powerW": 39600, "socPercent": 46, "voltageV": 398.5, "currentA": 99.4, "lastSampleAt": "…", "secondsSinceSample": 3,
  "maxPowerW": 40800, "avgPowerW": 39500,
  "costFils": 392, "costJod": 0.392, "currentRateFils": 193, "currentWindowNameEn": "Partial peak", "currentWindowNameAr": "ذروة جزئية",
  "price": [{ "key": "night", "nameEn": "Partial peak", "nameAr": "…", "kwh": 2.03, "priceFils": 193, "fils": 392 }],
  "series": [{ "at": "…", "powerW": 39600, "socPercent": 46, "energyKwh": 2.03 }] }
```
The partner web shows it under a busy plug: power now (big, animated), energy so far, battery % bar, elapsed clock ticking every second, **cost so far** at the current rate, "updated N s ago", and a power / battery curve from `series` (≤ 120 points). The same endpoint renders a closed session (final figures + curve) when a row of the sessions list is tapped. The station live endpoint also carries `today` for owners: `{ sessions, energyKwh, costFils, costJod, running }` for three "today" tiles.

### 2.5 Accepted cards

- List: `GET /api/admin/ocpp/authorized-tags?chargingPointId={id}&includeDisabled=true` → `[{ id, chargingPointId, idTag, label, isEnabled, expiresAt, createdAt }]`
- Add: `POST /api/admin/ocpp/authorized-tags` `{ "chargingPointId": id, "idTag": "12345678", "label": "…", "expiresAt": null }` — the number is upper-cased and trimmed by the API; max 20 characters. The chargers receive the new list within seconds.
- Switch off / on: `PUT /api/admin/ocpp/authorized-tags/{cardId}/enabled` `{ "isEnabled": false }`.
- When a customer taps an unknown card, the charger refuses it; the number appears in the admin raw log. Tell the owner to ask the customer for the number printed on the card.

### 2.6 Push token — **required**

`PUT /api/notification-token` with `appType: 2` (partner) after login and on every token refresh. Without it the owner gets inbox entries only and no push for: charger offline > 15 min, plug faulted (immediately and after 15 min), session open > 6 h, car parked after charging (20 min after the driver was told, or immediately when no driver is known), and "back online / fault cleared". The minutes are per station (admin-configurable).

### 2.6b Worker permissions (owner decides what a worker sees)

`GET /api/provider/my-assets` now returns `access[]`, one entry per asset for the caller:
```json
"access": [{ "providerType": "ChargingPoint", "providerId": 210, "isOwner": false,
             "privileges": ["ConnectView", "PointOfSale", "ViewFeedback"] }]
```
Owners get every key. Use it to hide screens and buttons for a worker. Keys: `ConnectView` (Cable Connect read), `ConnectControl` (start / stop / unlock / service / restart, cards, sharing switch, names), `PointOfSale` (QR / transactions), `ManageOffers`, `SendAnnouncements`, `ViewWallet`, `ViewFeedback`, `ViewStatistics`, `ManageStation` (edit requests). Station details and the workers screen rule stay as they are: details always visible, workers owner-only. The API itself refuses `ConnectView` / `ConnectControl` / `PointOfSale` calls from a worker without the key (403 with a message), so the UI gate is never the only one.

Owner side: `GET /api/workers?providerType=&providerId=` now includes `privileges[]` and `usesAllPrivileges`; `PUT /api/workers/{providerManagerId}/privileges` `{ "privileges": ["ConnectView", …] }` (empty list = station details only; all keys = everything, including keys added later). `GET /api/workers/privileges` lists the keys. The partner web shows this as a checkbox list on the worker card.

### 2.7 Not in the partner app (admin only)

Registering a charger, the connection sheet, reading / changing charger settings, the raw message log, alert thresholds, blocking a station's live data.

---

## 3. Flows to test on dev (station 223 "Cable Demo Station (Test)", simulator `CBL-TEST-001`)

1. Driver: open station 223 → plugs shown → wait for the simulator to show a connected car (`Preparing`) → Start → `/current` fills → Stop → history shows the price.
2. Driver: Start on an empty plug → 400 "Plug the cable into the car first".
3. Driver: second Start while one is running → 400 "You already have a running session…".
4. Partner: Start for a customer on a `Preparing` plug → session with `startSource = Operator`; Stop; the sessions list shows the price.
5. Partner: switch sharing off → the driver's station page shows `available=false`, `unavailableReason = NotShared`.
6. Partner: add a card → it appears enabled; switch it off → the charger refuses it.

The Cable team can attach the simulator to dev on request:
`dotnet run --project Cable.Ocpp.TestClient -- --url wss://ocpp-dev.cable-app.com/ocpp16/ --id CBL-TEST-001 --idle 0 --run 30 --remote 1800 --charge-seconds 600`
(keeps a car connected on plug 1 and waits for a start from either app).
