# Cable Connect (OCPP 1.6J) — Status: done / pending

> One page for the team. Where the feature stands on **Oct 7, 2026**, what is live on dev,
> what is waiting on whom. Detail lives in `OCPP-Connect-Feature-Map.md` (every function with
> its status), `OCPP-Connect-Handover.md` (how to test and operate), `OCPP-Hosting-Decision.md`.
> Your review (`BE_OCPP_FINDINGS_AND_ASKS.md`) is referenced by its item numbers (P0-1, N-5 …).

---

## 1. Where things run right now

| | Build | DB | Notes |
|---|---|---|---|
| `dev.cable-app.com` API + admin | **current** (commit `1c8129f` / admin `17b9ba8`) | dev | alert job runs every 5 min |
| `ocpp-dev.cable-app.com` OCPP server | **current** | dev | SmarterASP; keep-alive task every 5 min |
| `ocpp.cable-app.com` OCPP server (AWS) | **old — Phase 1 only** | dev | republish before the station visit |
| Production API / DB | nothing yet | — | see §5 |

Both repos are committed and pushed (`Anashawas/Cable-API`, `Anashawas/Cable-Admin`, branch `main`).

## 2. Done — built, tested with the simulator, on dev

### OCPP server (`Cable.Ocpp`)
- All nine charger-initiated messages handled and persisted; raw frame log; restart counter; liveness (Online / Reconnecting / Offline by freshness).
- Charger identity: id + optional Basic password, 10 failures → 15-min lock. Subscription gate (`OcppConnect` type).
- **Phase 2 control**: TriggerMessage, Reset, UnlockConnector, ChangeAvailability, GetConfiguration, ChangeConfiguration, **RemoteStopTransaction** (Oct 8) — sent from the API through the internal `/commands` endpoint, reply matched by uniqueId, audited in `OcppCommand`, **confirmed** when the charger's follow-up message proves the effect (`CompletedAt`).
- **Safety (your P1-6)**: **rate limit** (10 commands per charger per minute, API and OCPP server) and **duplicate protection** (an identical Reset / ChangeAvailability / Unlock / RemoteStop is refused while the previous one is unconfirmed and under 90 s old); protected configuration keys refused in code (server address / identity / auth key / security profile / APN…), UnlockConnector refused while a plug is Charging — both in the API and again inside the OCPP server.
- **SendLocalList (your 3.7, "build first")**: the station's cards are pushed into every charger after each card change and when a Pending unit boots; ClearCache follows; status per charger (Synced / Pending / Failed / NotSupported).
- AWS Lightsail production host prepared: Always Running pool, Let's Encrypt, verified 25-min silent socket and 28/28 heartbeats with 0 restarts.

### API (`WebApi`) + jobs
- Admin endpoints under `/api/admin/ocpp`: fleet health, chargers (list / detail / register / update / rotate password / enable / delete), connectors (plug type + kW), allowed cards, raw log, the six commands + history, sync-local-list, alerts.
- Hangfire jobs: stale sessions, raw-log purge, fault push, local-list sync, **alert rules every 5 min**: charger offline > 15 min, plug Faulted > 15 min, session open > 6 h, **parked after charging (your N-5)** > 20 min (driver first when the card is linked, then station). Push + inbox; "back online / fault cleared" on resolve.
- **N-2 live-data visibility (Oct 8)**: `ChargingPoint.ShareLiveStatus` (owner consent, ON at first activation unless decided) + `LiveStatusBlocked` (admin veto with reason). Admin: "Live data for drivers" panel on the station's Cable Connect tab (gates, owner switch on request, block / unblock). Partner app endpoints ready: `GET/PUT /api/provider/charging-points/{id}/live-visibility[/share]`. `OcppLiveVisibility.IsVisibleAsync` is the one check the driver-app endpoint (Phase E) must call.
- Credentials sheet data (your P0-1 / P0-2): `webSocketBaseUrl` and `port` come from the API's `OcppServer:Url`, never from the admin's config.
- Charge Point ID limited to 20 characters (P0-5).

### Admin (`Cable-Admin`)
- Cable Connect screen: KPI tiles (incl. open alerts), **Alerts panel**, chargers table with search / filters, commissioning chip for never-booted units.
- Charger page: **Remote control** (refresh menu, soft / hard reset, unit & per-plug out-of-service, unlock, command history with "confirmed after N s"), **Charger settings** (read keys, supported profiles, inline edit), **Cards on the unit** chip + Sync now, **commissioning card** until the first boot (Waiting / Connected / Refused with reason / Booted, polled every 4 s — P0-4), connectors with **plug type dropdown** (P1-1) and kW, sessions, raw log.
- Register dialog: password **off by default** (P0-3), id max 20, sheet shows URL + **port** with the 4435 warning (P0-1); the new charger opens automatically after the sheet.
- Station page → Cable Connect tab: chargers, OcppConnect subscription panel, allowed cards.
- `config.production.js` has `ocpp.url` (P1-2); `config.staging.js` now holds Cable's staging hosts instead of the old Kuwait project. P2 quality items done: search debounce, per-row pending flags, expiry sent as UTC instant, kWh with two decimals. Arabic + English for everything.

### Simulator (`Cable.Ocpp.TestClient`)
- Sessions, outage replay, faults, idle survival, answers every central-system command (reset really reboots, availability survives reboot, local list, config list incl. protected keys), `--charge-seconds` hold for UI tests.

### Database
- Tables: `OcppChargePoint`, `OcppConnector`, `OcppTransaction`, `OcppMeterValue`, `OcppAuthorizedTag`, `OcppRawMessage`, `OcppProcessStart`, `OcppCommand`, `OcppAlert`, `OcppUserIdTag`. Scripts `Scripts/OcppConnect_Phase0.sql`, `Phase1.sql`, `Phase2.sql` — applied to **dev only**.

### Answered from your review
- 3.9 split: RemoteStop → Phase 2, RemoteStart → Phase 3 (P1-5). Reservation and SmartCharging moved to "planned" (RH4 supports both).
- P1-4 commit: done (two months of work were uncommitted; now in three commits + two more today).
- P1-3 fault push: backend side is complete. **The partner app must call `PUT /api/notification-token` with `appType: 2`** after login and on token refresh (the endpoint exists; the app has the TODO). Until then owners get inbox entries, no push.

## 3. Pending — backend / admin (no external dependency)

| # | Item | Est. | Source |
|---|---|---|---|
| 1 | Reports page: energy & sessions per station per day / week / month, plug utilization, faults per month, Excel export | 2–3 d | feature map 4.11 |
| 5 | Reliability score, partner-only first; "reliable" badge ≥ 95 % later | 1 d | N-6 |
| 6 | Admin screen to link a card to a user (`OcppUserIdTag` exists, no UI yet) — only needed if automatic points go the card route | 0.5 d | P1-6 item 4 |
| 7 | Alert thresholds configurable per station (constants today: 15 min / 15 min / 6 h / 20 min) | 0.5 d | N-5 |

## 4. Pending — mobile apps

| # | Item | Depends on |
|---|---|---|
| 8 | **Partner app**: register the FCM token (`PUT /api/notification-token`, `appType: 2`) and fill `_handleNotificationTap` — unlocks every push built so far | nothing |
| 9 | Partner app Phase D: live plugs & session, history, allowed cards, maintenance buttons (reset / unlock / out-of-service), **the N-2 sharing switch (endpoint ready)** | provider endpoints (3 d API) |
| 10 | Driver app Phase E: "N of M free" **per plug type**, faults visible, driver-facing charger names, station-level aggregation | B2C station endpoint (2 d API) — N-3, N-4, N-7 |

## 5. Pending — decisions (parked until agreed)

| # | Decision | What it blocks |
|---|---|---|
| 11 | **Automatic loyalty points (N-1)**: which card path (station's own cards with manual linking vs. start-from-app in Phase 3), amount = kWh × `ChargingPoint.Price`?, no commission / no partner transaction for OCPP sessions, minimum kWh | the whole N-1 build (~2 d) |
| 12 | **Phase 3 commerce**: RemoteStart from the app with a virtual tag per user, tariffs, payment, reservation | pricing agreement with the station; the unit must point only at us |

## 6. Pending — operations

| # | Item | Who |
|---|---|---|
| 13 | Republish `Cable.Ocpp` to AWS (`aws-ocpp` profile) so production matches dev | backend |
| 14 | **Station visit (RH4)**: point the unit at `wss://ocpp.cable-app.com/ocpp16/`, port **443**, id `RH4` (register it without password — Security Profile 0); set `Ocpp:AuthorizeMode = RejectAll` for the watch-only phase; read its settings with the new panel; then 7 days of readings. Confirm whether customers tap their own cards or the worker taps a shared one (decides N-1) | backend + site |
| 15 | Production switch: apply Phase 0–2 scripts to the production DB, add `OcppServer` config to the production API, `appsettings.Production.json` + `EnvironmentName` for Cable.Ocpp, publish both | backend, after the pilot |
| 16 | Uptime monitor on `https://ocpp.cable-app.com/health` (UptimeRobot free tier) | ops |
| 17 | Database retention: raw log 30 → 7 days, thin meter samples after 90 days (the capacity numbers in the hosting doc assume this) | backend, 0.5 d |

## 7. How to check the status yourself

- Dev admin: `https://dev.cable-app.com/admin` → **كيبل كونكت**. Station 223 (demo) has chargers `CBL-TEST-001/002`; a simulator can be attached to `ocpp-dev` any time with
  `dotnet run --project Cable.Ocpp.TestClient -- --url wss://ocpp-dev.cable-app.com/ocpp16/ --id CBL-TEST-001 --idle 0 --run 30 --plug --charge-seconds 600`.
- Server health: `https://ocpp-dev.cable-app.com/health`, `https://ocpp.cable-app.com/health`; `/status` with the `X-Api-Key` header lists connected chargers.
- Jobs: Hangfire dashboard of the dev API; `HangFire.Server` shows which instances are alive (note the shared-storage caveat in the handover §9d).
- Data: `OcppCommand` (every command, who, answer, confirmed), `OcppAlert` (open and resolved), `OcppRawMessage` (every frame).
