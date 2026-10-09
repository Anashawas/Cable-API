# Cable Connect (OCPP 1.6) — Feature Map: done, pending, possible

> One page for the team: every function the charger link gives us, what is already built,
> what is still pending, and what we can add — and in which app each one lives.
> Status as of Oct 6, 2026. Companion docs: `OCPP-Connect-Implementation-Plan.md` (design),
> `OCPP-Connect-Handover.md` (how to test/operate), `OCPP-Hosting-Decision.md` (hosting).

Legend: ✅ done and tested · ⏳ pending (planned) · 💡 possible add-on (not planned yet)

Apps: **Server** = `Cable.Ocpp` (the OCPP endpoint) · **API** = `WebApi` · **Admin** = Cable-Admin web ·
**Partner** = station-owner mobile app · **Cable app** = driver mobile app

---

## 1. Platform (where things run)

| # | Item | Status | Where | Notes |
|---|---|---|---|---|
| 1.1 | OCPP 1.6J WebSocket server, one socket per charger, `ocpp1.6` subprotocol | ✅ | Server | in-process on IIS, .NET 10 |
| 1.2 | Dev deployment `ocpp-dev.cable-app.com` (SmarterASP, keep-alive task every 5 min) | ✅ | Server | shared plan: idles without the task, recycles daily |
| 1.3 | Production host `ocpp.cable-app.com` (AWS Lightsail Stockholm, Always Running) | ✅ | Server | verified Oct 6: silent socket 25 min, 28/28 heartbeats, 0 restarts |
| 1.4 | Charger identity: Charge Point ID + optional HTTP Basic password, 10 failures → 15-min lock | ✅ | Server | RH4 runs Security Profile 0 → register it **without** password |
| 1.5 | Raw message log (every frame in/out + connect/disconnect), 30-day purge job | ✅ | Server + API | retention to shorten to 7 days (see 7.2) |
| 1.6 | Process-restart counter (`OcppProcessStart`) | ✅ | Server | tells us how often the host restarts us |
| 1.7 | Subscription gate: a station needs an active **OcppConnect** subscription for its charger data to show. **N-2 (Oct 8):** plus the owner's "share live data" switch (on with the first activation unless the owner decided) and an admin veto with a reason — `OcppLiveVisibility` answers the single yes/no the driver app will use | ✅ | API + Admin (+ partner endpoints) | chargers keep working when it lapses; only the views hide |
| 1.8 | Switch AWS to the **production** database | ⏳ | Server | apply `Scripts/OcppConnect_Phase0.sql` + `Phase1.sql` to prod, add `appsettings.Production.json`, republish |
| 1.9 | External uptime monitor on `/health` | ⏳ | — | UptimeRobot free tier; nothing on AWS alerts us by itself |
| 1.10 | Load test (50 simulated chargers) | 💡 | Server | estimate today: ~500 chargers per $22 box; the DB is the real limit (~20–50 chargers/year at 4 GB) |

## 2. Messages the charger sends us (charger → server)

All nine OCPP 1.6 charger-initiated messages are handled and persisted.

| # | Message | What we do with it | Status |
|---|---|---|---|
| 2.1 | BootNotification | accept, store vendor/model/firmware/serials, tell it our heartbeat interval (60 s) | ✅ |
| 2.2 | Heartbeat | liveness; Online / Reconnecting / Offline is derived from message freshness | ✅ |
| 2.3 | StatusNotification | per-plug state (Available, Preparing, Charging, Faulted…) + error code; **Faulted → push to owner & managers** (30-min de-dupe) | ✅ |
| 2.4 | Authorize | answered from the station's allowed-cards list (`AuthorizeMode` = List / AcceptAll / RejectAll) | ✅ |
| 2.5 | StartTransaction | open a session; rejected card → session flagged `WasRejected` | ✅ |
| 2.6 | MeterValues | energy, power, current, voltage, SoC, temperature samples per session (charger timestamps; replay-safe, no duplicates) | ✅ |
| 2.7 | StopTransaction | close the session, energy = meterStop − meterStart, reason; late / orphan stops accepted | ✅ |
| 2.8 | DataTransfer | logged, answered `UnknownVendorId` | ✅ |
| 2.9 | Firmware / Diagnostics StatusNotification | acknowledged and logged | ✅ |
| 2.10 | Stale-session job: a session open > 24 h with no samples is marked stale (hourly) | ✅ |

## 3. Commands we can send the charger (server → charger) — the control layer

The mechanism is built (Oct 7): Cable.Ocpp sends the CALL and awaits the CALLRESULT by
uniqueId; the API forwards through `POST {OcppServer:Url}/commands/{id}` with the shared
key; every call is audited in `OcppCommand`. Adding another command is one Application
handler + one route + one button.

| # | OCPP command | What it gives us | Status | Exposed in |
|---|---|---|---|---|
| 3.1 | **GetConfiguration** | read every setting of the unit (heartbeat, sample interval, measurands, offline keys, `SupportedFeatureProfiles`) | ✅ Oct 7 | Admin (charger page → Charger settings) |
| 3.2 | **ChangeConfiguration** | change the safe keys (intervals, measurand list, `AuthorizeRemoteTxRequests`, `LocalAuthListEnabled`…). Keys naming the central system, URL/endpoint, identity, authorization key, security profile, APN are **protected in code** (`OcppProtectedConfigurationKeys`), refused by the API and again by Cable.Ocpp | ✅ Oct 7 | Admin (inline edit in Charger settings) |
| 3.3 | **TriggerMessage** | "refresh now": ask for Boot / Status / MeterValues / Heartbeat on demand | ✅ Oct 7 (Admin) | Admin, Partner ⏳ |
| 3.4 | **Reset** (Soft / Hard) | remote reboot — fixes most "frozen charger" tickets | ✅ Oct 7 (Admin) | Admin, Partner ⏳ |
| 3.5 | **UnlockConnector** | release a stuck cable. **Refused while the plug is Charging** (API checks the DB state, Cable.Ocpp the live state) | ✅ Oct 7 (Admin) | Admin, Partner ⏳, Cable app (own session only) 💡 |
| 3.6 | **ChangeAvailability** | plug or unit Operative / Inoperative ("out of service") | ✅ Oct 7 (Admin) | Admin, Partner ⏳ |
| 3.7 | **SendLocalList / GetLocalListVersion** | push the allowed cards into the charger so it still authorizes when our server is unreachable | ✅ Oct 7 | automatic (Hangfire job) after every card change and when a Pending unit boots; chip + "Sync cards now" in Admin |
| 3.8 | **ClearCache** | wipe cached authorizations after removing a card | ✅ Oct 7 | sent right after every accepted SendLocalList |
| 3.9a | **RemoteStopTransaction** | stop a running session from the admin / partner app — a safety and operations tool (stuck session, car left charging), no payment involved. Confirmed by the unit's own StopTransaction (reason Remote) | ✅ Oct 8 (Admin: "Stop session" on the plug with an open session) | Admin, Partner ⏳ |
| 3.9b | **RemoteStartTransaction** | start charging from an app with a card id we supply (RH4 has `AuthorizeRemoteTxRequests` ON → it asks us to authorize, we answer); needs a user ↔ idTag mapping, tariffs and payment | ⏳ Phase 3 | Cable app, Partner, Admin |
| 3.10 | **ReserveNow / CancelReservation** | hold a plug for a card until a time | ⏳ Phase 3 (RH4 confirmed `Reservation` in its feature list at the site visit) | Cable app |
| 3.11 | **SetChargingProfile / ClearChargingProfile / GetCompositeSchedule** | cap power per plug / per unit by schedule (load management, cheaper tariff hours) | ⏳ later (RH4 confirmed `SmartCharging` at the site visit) | Admin, Partner |
| 3.12 | **UpdateFirmware / GetDiagnostics** | push a firmware file, pull the unit's log file | 💡 | Admin — needs a file server we host |

**What OCPP does not give us (so it is our own app logic):** prices, payments, receipts, invoices,
loyalty; the charger's hardware limits (max power, plug types — read-only); the charger's SIM /
network; and anything while the unit still points at another server (one charger ↔ one server).

## 4. Admin web (Cable-Admin)

| # | Feature | Status | Depends on |
|---|---|---|---|
| 4.1 | **Cable Connect** screen: fleet KPIs (online / reconnecting / offline, free plugs, faulted, charging now, kWh today, sessions today, stations without subscription, never connected, stale) | ✅ | — |
| 4.2 | Chargers table: search, state / enabled filters, subscription chip, last seen | ✅ | — |
| 4.3 | Register charger: station search dropdown, id (auto `CBL-{station}-{nn}` or the unit's own id, max 20 chars), heartbeat, password off by default (Security Profile 0) → one-time credentials sheet: URL and **port** as the server reports them, id, username, password | ✅ (port/URL/defaults Oct 7) | — |
| 4.4 | Charger detail: unit info, connection (IP, since, last message), plugs with status / error / **plug type (dropdown)** / power, recent sessions (open / stale / orphan / rejected chips), message log viewer, **commissioning card** until the first boot (Waiting / Connected / Refused with the reason / Booted, polled every 4 s; the register flow opens it automatically) | ✅ (plug type + commissioning Oct 7) | — |
| 4.5 | Edit charger, new / remove password, enable / disable (disable closes its socket within a minute), delete (refused while a session is open), edit plug type & power | ✅ | — |
| 4.6 | Allowed cards per station: add / disable / expire / remove | ✅ | — |
| 4.7 | Station page → **Cable Connect** tab: the station's chargers + **OcppConnect subscription panel** (record / renew payment) | ✅ | republish WebApi to dev to ship the latest build |
| 4.8 | Remote buttons on the charger page: refresh now, soft / hard reset, unlock plug, out-of-service / back in service, command history (who / what / answer / took / **confirmed after N s** — the charger's follow-up message, not just its "Accepted") | ✅ Oct 7 | — |
| 4.9 | Charger settings panel: read the unit's config keys, supported profiles as chips, inline edit of the writable ones | ✅ Oct 7 | — |
| 4.10 | "Cards on the unit" chip per charger (Synced vN · when · count / Pending / Failed / Not supported) + "Sync cards now" | ✅ Oct 7 | — |
| 4.11 | Reports: energy & sessions per station per day, plug utilization %, faults per month, Excel export | 💡 | data already stored |
| 4.12 | Tariffs per station: per kWh, per minute, idle fee, free list (owner's cards) | 💡 Phase 3 | commercial agreement |
| 4.13 | Alert rules: charger offline > 15 min, plug Faulted > 15 min, session open > 6 h → push + inbox to admins, the station owner and managers; "back online / fault cleared" push when it resolves; **parked after charging** (Finishing / SuspendedEV > 20 min → the driver via `OcppUserIdTag`, then the station 20 min later; station directly when the card is not linked); Alerts panel + KPI tile on the Cable Connect screen | ✅ Oct 7 | job `check-ocpp-alerts` every 5 min |
| 4.14 | Remote start / stop from admin (support use) | 💡 Phase 3 | 3.9 |
| 4.15 | Firmware & diagnostics page | 💡 | 3.12 |

## 5. Partner app (station owner / managers)

Nothing is built yet in the mobile app. The admin queries already exist, so the API side is
mostly reuse plus ownership checks (`/api/provider/charging-points/{id}/chargers/...`).

| # | Feature | Status | Depends on |
|---|---|---|---|
| 5.1 | **Fault push** when a plug reports Faulted (owner + active managers, inbox entry) | ✅ backend · ⏳ **partner app must register its FCM token**: `PUT /api/notification-token` `{ token, osName, osVersion, appVersion, appType: 2 }` (2 = StationApp) after login and on token refresh — the endpoint exists, the app has a TODO where the call should be (`notification_service.dart:218/235`). Until then owners get inbox entries but no push | — |
| 5.2 | Live view: each charger's plugs, state, the session in progress (kWh, power, SoC, duration) | ⏳ Phase D (plug states: ✅ `GET /api/provider/charging-points/{id}/live` Oct 8; session detail still ⏳) | provider API |
| 5.3 | Session history: card, energy, duration, stop reason; day / week / month totals | ✅ API Oct 8 (`GET /api/provider/charging-points/{id}/sessions`, filters chargerId / from / to, paged) · ⏳ app screen; totals = app-side sums for now | — |
| 5.4 | Allowed cards: add / disable / expire from the phone | ⏳ Phase D | reuse admin endpoints |
| 5.5 | More pushes: charger offline > 15 min, plug faulted > 15 min, session > 6 h, back online / fault cleared | ✅ Oct 7 (push + inbox already reach the owner; the partner app only needs to open them) | — |
| 5.6 | Maintenance buttons with confirmation: refresh, reset, unlock, out-of-service, **stop session** | ✅ API Oct 8 (`POST /api/provider/charging-points/{id}/chargers/{ocppId}/commands/…`, owner-or-manager guard, same audit / confirmation / rate limit) · ⏳ app screen | — |
| 5.7 | Monthly statement: energy sold, sessions, revenue (once tariffs exist), export | 💡 | 4.12 |
| 5.8 | Remote start / stop for a customer on site | 💡 Phase 3 | 3.9 |
| 5.9 | Power cap per plug / schedule (load management) | 💡 | 3.11 + unit support |
| 5.11 | **Reliability score (N-6)**: % of the last 30 days each charger was reachable and fault-free (our own restarts excluded), daily job + "Recompute" in Admin; owner sees the number (`reliabilityPct` on `GET /api/provider/charging-points/{id}/live`), drivers get only `reliable: true` at ≥ 95 % on the station live endpoint, never a bad number | ✅ API Oct 8 · ⏳ app screens | — |
| 5.10 | Name each cabinet for drivers (N-3) | ✅ API Oct 8 (`PUT /api/provider/charging-points/{id}/chargers/{ocppId}/display-name`) · ⏳ app screen | — |

## 6. Cable app (drivers)

Nothing is built yet. Everything here is gated by the station's OcppConnect subscription.

| # | Feature | Status | Depends on |
|---|---|---|---|
| 6.1 | **"N of M plugs free"** live badge on the station card and page, **per plug type** with max power | ✅ API Oct 8 (`GET /api/charging-points/{id}/live`, `GET /api/charging-points/live-summary?ids=`) · ⏳ app screens | gated by N-2 + freshness |
| 6.2 | "Notify me when a plug is free" | 💡 | 6.1 + small job |
| 6.3 | Charger-level detail on the station page: each cabinet (owner-named, N-3) with its plugs Free / Busy / **OutOfOrder (N-4)** / Unknown, all cabinets in one answer (N-7) | ✅ API Oct 8 · ⏳ app screens | same endpoint as 6.1 |
| 6.4 | **Start charging from the app** on a chosen plug (a virtual card id per app user, no physical card), stop from the app | 💡 Phase 3 | 3.9 + `OcppUserIdTag` (table exists since Oct 7; admin screen to link a card to a user still ⏳) |
| 6.5 | Live session screen: kWh so far, charging power, battery % if the car reports it, elapsed time, cost so far | 💡 Phase 3 | 6.4 + tariffs |
| 6.6 | Pushes: charging started, car stopped drawing, charging complete, cable still plugged | 💡 Phase 3 | 6.4 |
| 6.7 | Unlock my cable | 💡 Phase 3 | 3.5, own session only |
| 6.8 | Reserve a plug for 15 min before arriving | 💡 | 3.10 + unit support |
| 6.9 | Pay at end of session (wallet / card already in the app), receipt, history | 💡 Phase 3 | tariffs, payment provider |
| 6.10 | Rate the charging session (feeds the existing review system) | 💡 | 6.4 |

## 7. Housekeeping still open (backend)

| # | Item | Status |
|---|---|---|
| 7.1 | Commit the backend (Cable.Ocpp, test client, entities, scripts, routes, jobs, docs — ~160 files) | ⏳ |
| 7.2 | Retention: raw log 30 → 7 days; thin meter samples to 1/min after 90 days (the capacity numbers in the hosting doc assume this) | ⏳ |
| 7.3 | `dbRoundTripMs` in `/status` for a one-URL latency check | 💡 |
| 7.4 | Republish WebApi to dev (ships the admin build with the subscription panel) | ⏳ |
| 7.5 | Prepare RH4: clear the password on `RH4-TEST` (or register fresh without one); set `AuthorizeMode = RejectAll` for the watch-only trial | ⏳ before the visit |

## 8. Suggested order

| Step | Scope | Est. | Needs |
|---|---|---|---|
| **Now** | 7.1–7.5, 1.9 | 1 day | — |
| **C — first real charger** | RH4 on `wss://ocpp.cable-app.com/ocpp16/`, 7 days of readings, read `SupportedFeatureProfiles` | 1 wk + site visit | station visit |
| **2 — control** | 3.1–3.8 on the server + API, 4.8–4.10 in Admin | 2 wk | RH4 online to test against |
| **D — partner app** | 5.2–5.6 | 2 wk mobile + 3 days API | provider API |
| **E — driver app** | 6.1–6.3 | 1 wk | B2C endpoint |
| **3 — commerce** | 3.9, 4.12, 6.4–6.7, 6.9, 5.7–5.8 | 4–6 wk | pricing agreement with the station; the unit must point only at us |
| **Later** | 3.10–3.12, 5.9, 6.8 | — | unit feature list, contract |

## 9. First questions to answer from the real unit (RH4)

1. ~~`SupportedFeatureProfiles`~~ **Answered at the site visit:** Core, LocalAuthListManagement, SmartCharging, Reservation, RemoteTrigger.
2. `LocalAuthListMaxLength` — how many cards fit on the unit?
3. ~~`MeterValuesSampledData`~~ **Answered:** SoC, Current.Import, Voltage, Power.Active.Import, Energy.Active.Import.Register, Energy.Active.Import.Interval.
4. Does it keep charging when our server is unreachable (`AllowOfflineTxForUnknownId`, `LocalPreAuthorize`)?
5. Does a Hard Reset interrupt a running session, and does it resume cleanly?

The answers decide which 💡 rows become ⏳.
