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

## 4. Test on dev

Dev charger simulator (Cable.Ocpp.TestClient, demo mode) sends power, amps, voltage, battery % and
temperature every 5 s, so all the new tiles and the limit stop can be tried without a real car.
