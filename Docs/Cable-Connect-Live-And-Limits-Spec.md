# Cable Connect — live updates, new readings, charge limit (mobile spec)

From the first client's review (Oct 2026). The backend is done and tested on dev. This page lists what
the **partner app** and the **Cable (driver) app** need to change. Keep both languages for every new text.

| # | Client asked for | Where it shows |
|---|---|---|
| 1 | Amps reading | live session: `currentA` (+ `voltageV`) |
| 2 | No delay on screen | partner app: SignalR live push (section 1); driver app keeps polling |
| 3 | Battery % at start and end | `socStartPercent` → `socEndPercent` (live session, sessions list, history) |
| 4 | Start and finish time | `startedAt` / `stoppedAt` (already returned; show both) |
| 5 | Temperature | live session: `temperatureC` (null when the charger does not send it) |
| 7 | Stop at an amount (e.g. 7 JOD) | charge limit (section 3) |

Dates are Jordan local time without a zone, like every other API date.

---

## 1. Live updates over SignalR (partner app)

The server pushes a small "something changed" message the moment a charger reports it (under a second).
The app then **refetches** what it shows. Polling stays as a fallback, slower while connected.

- Hub: `{apiBase}/hubs/ocpp`. Token: the same JWT as the REST calls (`accessTokenFactory` — read the
  current token every time, so a refreshed token is used on reconnect). Flutter: `signalr_netcore`.
- After connecting, call `JoinStation(chargingPointId)` for the station on screen; `LeaveStation(id)`
  when leaving it. Allowed for the owner and for workers with `ConnectView`; others get an error →
  just keep polling.
- Server → app method **`ocpp`** with:

```json
{ "type": "session", "chargingPointId": 210, "chargerId": 7, "connectorId": 1,
  "transactionId": 56, "status": "updated", "at": "2026-10-20T20:33:49.599" }
```

| type | status values | refetch |
|---|---|---|
| `connector` | the OCPP status: Available, Preparing, Charging, SuspendedEV, Finishing, Faulted, Unavailable… | station live picture |
| `session` | `started`, `updated` (new reading), `limit-reached`, `stopped` | the live session (`transactionId`), plus live picture + sessions list on started / stopped |
| `charger` | `online`, `offline`, `booted` | station live picture |

- Coalesce: several messages within ~300 ms → one refetch.
- **Reconnect forever.** The default automatic reconnect gives up after ~40 s (verified on a server
  restart). Retry with backoff up to every 30 s, and after reconnecting call `JoinStation` again and
  refetch once (messages sent while disconnected are not replayed). The server closes the connection
  when the JWT expires; reconnect with the refreshed token.
- Polling while connected: 60 s for the live picture, 30 s for an open session. When not connected:
  today's 15 s / 5 s.

The driver app does not join the hub (drivers are not station members); it keeps polling `/current`
every 10 s as today.

## 2. New fields

### Partner — `GET /api/provider/charging-points/{cp}/sessions/{tx}/live`
New: `temperatureC`, `socStartPercent`, `socEndPercent`, `chargeLimit`; each `series[]` point now
also has `currentA`, `voltageV`, `temperatureC`. Existing: `currentA`, `voltageV`, `powerW`, `socPercent`,
`energyKwh`, `costJod`, `startedAt`, `stoppedAt`…

Show tiles: Power (kW), Energy (kWh), **Amps (A)**, **Voltage (V)**, **Temperature (°C)**,
**Battery start → now** (e.g. 40% → 63%), Cost, Started at (and Finished at when closed).
Hide a tile when its value is null — not every charger sends temperature or battery %.

### Partner — sessions list `GET /api/provider/charging-points/{cp}/sessions`
New per row: `socStartPercent`, `socEndPercent`, `chargeLimit`. Show start time, end time and
"battery 40% → 80%".

### Driver — `GET /api/users/me/ocpp-sessions/current` and history `GET /api/users/me/ocpp-sessions`
New: `socStartPercent`, `socEndPercent`, `currentA`, `voltageV`, `temperatureC`, `chargeLimit`.

### `chargeLimit`
```json
{ "kind": "Jod", "value": 7, "text": "7 JOD", "textAr": "7 دينار",
  "setAt": "2026-10-20T20:36:44", "reachedAt": null }
```
`null` when the session has no limit. Show `textAr` in Arabic. When it stopped the session,
`stopReasonText` / `stopReasonTextAr` already say "Charge limit reached (7 JOD)" /
"وصل إلى الحد المطلوب (7 دينار)". `reachedAt` set on a still-open session = the stop is being sent
→ show "Stopping — limit reached".

## 3. Charge limit — stop at an amount

The server checks every reading and stops the session itself. It stops at the reading **before** the
limit would be passed, so the session ends at or slightly under the amount (tested: 0.5 JOD → 0.492,
1 kWh → 0.99), never over.

| kind | value | example |
|---|---|---|
| `Jod` | 0.5 – 200 | 7 → stop at 7 JOD (current tariff) |
| `Kwh` | 1 – 300 | 20 → stop at 20 kWh |
| `Soc` | 10 – 100, whole number | 80 → stop at 80 % battery (only if the car reports battery %) |

Wrong values → 400 with a ready-to-show `detail` in the request language (`Accept-Language`).

**Set it when starting** — optional "Stop at…" in the start dialog:
- Partner: `POST /api/provider/charging-points/{cp}/chargers/{charger}/commands/remote-start`
  `{ "connectorId": 1, "chargeLimitKind": "Jod", "chargeLimitValue": 7 }`
- Driver: `POST /api/users/me/ocpp-sessions/start`
  `{ "chargingPointId": 210, "chargerId": 7, "connectorId": 1, "chargeLimitKind": "Jod", "chargeLimitValue": 7 }`

**Set / change / clear it on a running session** (also works for card and password sessions in the
partner app):
- Partner (`ConnectControl` needed): `PUT /api/provider/charging-points/{cp}/sessions/{tx}/limit`
- Driver (own session): `PUT /api/users/me/ocpp-sessions/{id}/limit`
- Body `{ "kind": "Kwh", "value": 20 }`; `{ "kind": null, "value": null }` clears it.
  Returns the new `chargeLimit` (or null).

## 4. Driver app — charging from the app (added Oct 27)

Backend for `LIVE_CHARGING_SESSION.md` items 4.1, 4.3, 4.5 and 4.6. Tested on dev.

**Can this plug be started from the app?** `GET /api/charging-points/{id}/live` → each plug now has:

| Field | Values |
|---|---|
| `canStartFromApp` | `true` when the car is connected and nothing is running → show "Start" |
| `cannotStartReason` | `offline` · `plugCable` (connect the car to the plug first) · `busy` · `outOfOrder` |

A start into an empty plug times out on the charger, so "Start" appears once the car is plugged in (the plug
shows `Preparing`). Station-level reasons (no subscription, not shared) stay in `unavailableReason`.

**Second session** → `POST /api/users/me/ocpp-sessions/start` returns **409** with
`{ "code": "sessionAlreadyOpen", "detail": "<ready text in the request language>" }` → show "You have a
running session — view it".

**Session details with the curve** → `GET /api/users/me/ocpp-sessions/{id}` (the driver's own sessions only, 404
otherwise). Same shape as the partner's live session: `stationName`, `chargerName`, `energyKwh`, `powerW`,
`currentA`, `voltageV`, `temperatureC`, `socStartPercent` / `socPercent` / `socEndPercent`, `costJod` (so far
while open), `currentRateFils`, `currentWindowNameAr/En`, `price[]` lines, `chargeLimit`, `stopReasonTextAr`, and
`series[]` (up to 120 points: `at, powerW, socPercent, energyKwh, currentA, voltageV, temperatureC`) for the chart.

**Pushes to the driver** (Cable app only, in the token's language; also saved in the inbox):

| When | Inbox type | Example (ar) |
|---|---|---|
| Session started | 8 `charging_session_started` | بدأ الشحن ⚡ — محطة الرشدان · الشاحن الأيمن · المقبس 1 · يتوقف عند 7 دينار |
| Session finished | 9 `charging_session_completed` | انتهى الشحن ✔ — 12.40 kWh · 2.310 د · 1 س 4 د · البطارية 40% ← 80% / السبب: وصل إلى الحد المطلوب (7 دينار) |

Data: `{ "type": "ocpp_session", "status": "started" | "completed", "sessionId": "74", "chargingPointId": "210" }`
→ open the session screen. Only sessions the driver started (app, or a card linked to their account): a start by
station staff never notifies anyone, and sessions a charger replays after being offline are not pushed. There is
no separate "limit reached" push — the finished push follows within seconds and names the limit.

**Partner code preview** (`GET /api/partners/PreviewPartnerCode`, from `PARTNER_BILLING.md` C and D):
`commissionAmount` / `commissionPercentage` in the user app responses are now always 0 (stop reading them; they
will be removed). New: `pointsAvailable`, `pointsUnavailableReason` (`belowMinimumAmount` · `partnerInactive` ·
`partnerQuotaReached` reserved for scan bundles) and `minimumAmountForPoints` for "the minimum for points here is
0.4 JOD".

## 5. Test on dev

Dev charger simulator (Cable.Ocpp.TestClient, demo mode) sends power, amps, voltage, battery % and
temperature every 5 s, so all the new tiles and the limit stop can be tried without a real car.
