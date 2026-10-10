# Cable app (drivers) — Cable Connect: what to build (mobile spec)

> For the Cable driver-app team. These endpoints are new since the last app release; none of them is
> used by the app yet. Nothing here needs backend work — screens only. The partner web portal's
> live session panel is the visual reference for the "charging now" screen.
>
> API base: dev `http://dev.cable-app.com`, production `https://cable-app.com`. Times are **UTC**.
> Money in **fils** (`costFils`) with `costJod = fils / 1000`. Energy in kWh.
> Errors: RFC 7807 problem details — show `detail`, it is written for the driver.

---

## 0. Summary of work

| # | Screen / change | Endpoint(s) | Login |
|---|---|---|---|
| 1 | Station page: live plugs per type, charger names, "Reliable" badge | `GET /api/charging-points/{id}/live` | no |
| 2 | Map / list badge "CCS2 · 1 of 2 free" | `GET /api/charging-points/live-summary?ids=` | no |
| 3 | Start charging from the app (no card) | `POST /api/users/me/ocpp-sessions/start` | yes |
| 4 | Charging now screen (live) | `GET /api/users/me/ocpp-sessions/current` | yes |
| 5 | Stop from the app | `POST /api/users/me/ocpp-sessions/{id}/stop` | yes |
| 6 | Charging history + receipt with price | `GET /api/users/me/ocpp-sessions` | yes |
| 7 | Price alerts (tariff + preferences) | `GET /api/pricing/tou`, `GET/PUT /api/users/me/price-alerts` | tariff no / prefs yes |
| 8 | Push token with language | `PUT /api/notification-token` | yes |
| 9 | Handle the new pushes | — | — |

---

## 1. Station page — live plugs

`GET /api/charging-points/{id}/live` — anonymous. Poll every **15 s** while the station page is visible.

```json
{
  "chargingPointId": 210, "available": true, "unavailableReason": null, "updatedAt": "2026-10-09T20:58:09Z",
  "reliable": true,
  "plugTypes": [ { "plugTypeId": 17, "name": "CCS2", "family": "EURO", "total": 2, "free": 1, "busy": 1, "outOfOrder": 0, "unknown": 0, "maxPowerKw": 80 } ],
  "chargers": [
    { "id": 6, "ordinal": 1, "displayName": "الكابينة اليمنى", "online": true, "updatedAt": "…",
      "plugs": [
        { "connectorId": 1, "state": "Free", "plugTypeId": 17, "plugTypeName": "CCS2", "plugTypeFamily": "EURO", "powerKw": 40, "ocppStatus": "Available" },
        { "connectorId": 2, "state": "Busy", "plugTypeId": 17, "plugTypeName": "CCS2", "plugTypeFamily": "EURO", "powerKw": 80, "ocppStatus": "Charging" }
      ] }
  ]
}
```

- `available = false` → show no live data and one line by `unavailableReason`:
  `NoSubscription` / `NotShared` / `Blocked` / `NoChargers` → "Live availability is not offered at this station";
  `Offline` → "Charger offline right now". Never show the codes.
- Per plug type: "CCS2 · 1 of 2 free" from `plugTypes[]` (not by counting chargers yourself).
- `reliable: true` → "Reliable" badge. `null` → nothing (drivers never see a bad score).
- Charger title: `displayName` or "Charger {ordinal}". Never show internal ids.
- Plug chip by `state`: Free (green) · Busy (orange) · Out of order (red) · Unknown (grey, charger offline).
- `ocppStatus` is the charger's own status while it is online — needed for "Start" (section 3).

## 2. Map / list badges

`GET /api/charging-points/live-summary?ids=1,2,3` — anonymous, max 50 ids per call. Only stations **with chargers**
are returned; a station missing from the answer gets no badge.

```json
[ { "chargingPointId": 210, "available": true, "unavailableReason": null, "updatedAt": "…",
    "plugTypes": [ { "plugTypeId": 17, "name": "CCS2", "free": 1, "total": 2 } ] } ]
```

Call it for the stations on screen when the map settles; refresh at most every 30 s.

## 3. Start charging from the app

Flow on the station page (logged-in driver):

1. The driver plugs the cable into the car. The plug's `ocppStatus` becomes **`Preparing`** within a second or two
   (`state` turns `Busy`).
2. Only then show **"Start charging"** on that plug. On an `Available` plug show "Plug the cable into your car first".
3. Tap → confirm → `POST /api/users/me/ocpp-sessions/start`

```json
{ "chargingPointId": 210, "chargerId": 6, "connectorId": 1 }
```
`chargerId` = `chargers[].id`, `connectorId` = `plugs[].connectorId` from section 1.

→ 200
```json
{ "commandId": 42, "accepted": true, "status": "Answered", "resultStatus": "Accepted", "idTag": "CBL-U14119",
  "message": "The charger accepted. Plug in if you have not yet; charging starts in a few seconds." }
```

- `accepted = true` → "Starting…" and poll `/current` (section 4) every **3 s** until it returns a session
  (normally 3–6 s). After 60 s without one: "The charger did not start. Try again or use a card."
- `accepted = false` → show `message`.
- **400** with `detail` (show it): the station does not offer app charging · "Plug the cable into the car first" ·
  the plug is busy · the charger is offline · "You already have a running session. Stop it before starting another."

The driver's virtual card (`CBL-U{userId}`) is created by the server on first use. Nothing to store on the phone.

## 4. "Charging now" screen

`GET /api/users/me/ocpp-sessions/current` — poll every **10 s** while the screen is open (and every 60 s from a
"charging" banner elsewhere in the app). Returns the literal **`null`** when nothing is running.

```json
{ "id": 29, "chargingPointId": 223, "stationName": "Cable Demo Station (Test)", "chargerId": 1, "chargerName": "…", "connectorId": 1,
  "startedAt": "2026-10-09T10:44:30Z", "stoppedAt": null, "durationSec": 22, "energyKwh": 2.5, "isOpen": true,
  "startSource": "App", "stopReason": null, "stopReasonText": null, "stopReasonTextAr": null,
  "costFils": null, "costJod": null, "tariffVersion": null, "price": null,
  "powerW": 30000, "socPercent": 45, "lastSampleAt": "2026-10-09T10:44:51Z" }
```

Make it feel alive (partner web live panel = reference look):

- Pulsing green dot + "Charging at {stationName}".
- **Power now**: `powerW / 1000` kW, large, animated between polls.
- **Energy so far**: `energyKwh` kWh.
- **Battery**: `socPercent` % with a progress bar; hide when null or 0 ("battery not reported" — many cars do not send it).
- **Elapsed**: a clock ticking every second **on the phone** from `startedAt` (do not wait for the poll).
- "Updated N s ago" from `lastSampleAt`.
- The price is computed when the session ends (shown in the history). While charging, show the current rate from
  `GET /api/pricing/tou` for the window the clock is in, e.g. "193 fils/kWh now — Partial peak".
- **Stop** button (section 5).

Card sessions also appear here when the driver's card is linked to the account (`startSource = "Card"`).

## 5. Stop from the app

`POST /api/users/me/ocpp-sessions/{id}/stop` (id from `/current`) → `{ "accepted": true, "resultStatus": "Accepted", … }`.
Keep polling `/current`; it becomes `null` within ~5 s → open the receipt (section 6, newest item).
Only the driver's own session can be stopped (404 otherwise). Stopping at the charger with the card still works.

## 6. Charging history and receipt

`GET /api/users/me/ocpp-sessions?page=1&pageSize=20` → `{ items, totalCount, page, pageSize, totalPages, hasNextPage, hasPreviousPage }`,
items like section 4 plus, once closed:

```json
"stoppedAt": "…", "isOpen": false, "durationSec": 463,
"stopReason": "Remote", "stopReasonText": "Stopped from the app", "stopReasonTextAr": "أُوقف من التطبيق",
"costFils": 458, "costJod": 0.458, "tariffVersion": 1,
"price": [ { "key": "offPeak", "nameEn": "Off-peak", "nameAr": "خارج الذروة", "kwh": 2.5, "priceFils": 183, "fils": 458 } ]
```

- List row: station, date, energy, duration, price.
- Receipt: station, charger, plug, start → end, duration, energy, why it ended (`stopReasonTextAr` / `stopReasonText`),
  the `price[]` lines (window name × kWh × fils/kWh = fils) and the total in JOD.
- State clearly that **no payment was taken** — the price is informational until payment is built.
- `costFils` is normally set the moment the charger reports the stop. If it is still `null` (the stop arrived late or the tariff was unavailable), a background job prices it within 10 minutes — show "calculating…" and refresh.

## 7. Price alerts

- Tariff (anonymous, cache it, refresh on every app open; `version` increases on every change):
  `GET /api/pricing/tou` → `{ version, effectiveFrom, timezone: "Asia/Amman", currency: "JOD", unit: "fils/kWh",
  windows: [ { key, startMin, endMin, tier: "offPeak"|"partial"|"peak", priceFils, nameEn, nameAr } ] }`
  — minutes from midnight Jordan time; `endMin > 1440` crosses midnight.
- Preferences (logged in): `GET /api/users/me/price-alerts`, `PUT /api/users/me/price-alerts`
  `{ "enabled": true, "leadMinutes": 30, "windows": ["offPeak", "peak"] }` — `leadMinutes` 15 | 30 | 45 | 60; window keys
  from the tariff (unknown key → 400); `enabled: false` keeps the selection but sends nothing.
- Pushes are sent by the server (quiet hours 23:30–06:30 Jordan, no push inside them).

## 8. Push token — send the language

`PUT /api/notification-token` after login and on every token refresh:

```json
{ "token": "<FCM token>", "osName": "ios", "osVersion": "18.1", "appVersion": "3.4.0", "appType": 1, "language": "ar" }
```

`appType: 1` = Cable app. `language` `"ar"` | `"en"` = the app's UI language; server-sent pushes (price alerts, parked
car) use it. Without `language` they arrive in Arabic. Send it again when the user switches language.

## 9. New pushes the app receives

| Push | When | Tap opens |
|---|---|---|
| "Your car finished charging — please unplug" | the driver's plug stays in Finishing / SuspendedEV for 20 min after charging (per-station value) | the station page / last receipt |
| Price alert (e.g. "Off-peak starts in 30 min — 183 fils/kWh", in the user's language) | before a tariff window the driver subscribed to | the tariff screen |

---

## 10. Test on dev

Ask the backend team to run the simulator in demo mode on station **223** or **210** (two cars waiting, a new car 10 s after
every stop). Test driver account: ask the backend team.

1. Station page shows plugs, "1 of 2 free" changes when a session starts / stops elsewhere.
2. Start on a `Preparing` plug → "Starting…" → Charging now screen fills within ~5 s; power, energy, battery move every 10 s; the elapsed clock ticks.
3. Start a second time → 400 "You already have a running session…".
4. Start on an `Available` plug → the app does not offer it (and the API answers 400 if called).
5. Stop → `/current` null → receipt with price and per-window lines.
6. History lists the session; tap → receipt.
7. Price alerts: change preferences, check the tariff screen; push arrives at the next window (or ask backend to run the dry run).
8. Switch the app language → push token re-sent with the new `language`.
