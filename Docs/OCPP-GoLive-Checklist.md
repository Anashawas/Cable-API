# Cable Connect — go-live checklist (target: week of Oct 12, 2026)

> What has to be true before real drivers use Cable Connect at محطة الرشدان and the other
> pilot stations. Backend and admin items are done unless marked; the mobile items are the
> teammate's. Keep this file as the single list to tick off. Detail in
> `OCPP-Connect-Status.md`, operations in `OCPP-Connect-Handover.md`.

---

## 1. What the station visit on Oct 8 proved (RH4, hjl DC 2×, firmware 1.0.26.0410)

| Case | Result |
|---|---|
| Connect, boot, heartbeat, 3 connectors | ✅ accepted in 130 ms, heartbeat 60 s |
| Unknown card | ✅ refused (Invalid) |
| Card added in the admin → accepted on the next tap | ✅ list read live, no restart |
| SendLocalList + ClearCache | ✅ accepted (unit holds max **20** entries, `LocalAuthListEnabled` is **false** on the unit) |
| Session on connector 1 (40 kW) and connector 2 (80 kW), meter every 15 s | ✅ 3 sessions, 16.2 kWh, 0 samples lost |
| Stop by card | ✅ reason reported as **`Other`** by this vendor |
| Remote stop from the admin | ✅ twice, accepted ≤ 110 ms, confirmed by StopTransaction 4 s later |
| GetConfiguration | ✅ 54 keys in 200 ms |
| Soft reset | ✅ accepted, reconnected after 15 s, confirmed by BootNotification |
| Availability off / on, connector 1 | ✅ Unavailable / Available within 1 s |
| Internet loss during a session, blocked card, ChangeConfiguration, unplug from the car | ⏳ not run (car had no stop button; time) — next visit |

Fixes that came out of it (built Oct 9): charger-initiated drops are now logged as
`connection lost`, not `server shutting down` (reliability counts them); the local-list push asks
the unit for `SendLocalListMaxLength` above 20 cards and trims to it; vendor stop reasons are
shown as text (`Other` → "Stopped at the charger").

## 2. Backend (API + Cable.Ocpp) — done

- [x] Phase 0–2 + Phase 3 (start without a card, session price) built and tested with the simulator.
- [x] `Scripts/OcppConnect_Phase3.sql` applied to **dev**.
- [ ] **Production DB**: apply `OcppConnect_Phase0.sql`, `Phase1.sql`, `Phase2.sql`, `Phase3.sql`, `OcppConnect_AlertThresholds.sql`, `PriceAlerts_Phase1.sql` (all idempotent, in that order).
- [ ] **Production API config**: `OcppServer: { Url: "https://ocpp.cable-app.com", ApiKey: <the AWS StatusApiKey>, HttpTimeoutSeconds: 35 }` in `appsettings.Production.json` (git-ignored).
- [ ] **Cable.Ocpp on AWS**: `appsettings.Production.json` with the production connection string + `ASPNETCORE_ENVIRONMENT=Production` in IIS; today it runs the Staging settings against the dev DB.
- [ ] Publish API (dev first, then prod) — new Hangfire jobs `price-ocpp-sessions` (10 min) and the Phase 3 columns need the new build on **every** instance that shares the Hangfire DB (handover §9d).
- [ ] Publish Cable.Ocpp to AWS (profile `aws-ocpp`).
- [ ] Set `Ocpp:AuthorizeMode = List` on AWS (it is), confirm `/status` after publish.
- [ ] Uptime monitor on `https://ocpp.cable-app.com/health`.

## 3. Admin portal — done

- [x] Cable Connect screen, charger page (control, settings, cards, sessions with **price and who started**), alerts, reliability, live-data gates, price-alerts page.
- [x] **Start session** button on a free plug (for stations that issue no cards).
- [ ] Record the **OcppConnect subscription for station 210 (الرشدان)** — still missing after the visit; drivers see nothing live and the app cannot start sessions there until it exists.
- [ ] Set the plug types and kW of `RH4`'s two connectors (connector 1 ≈ 40 kW, connector 2 ≈ 80 kW; both DC).
- [ ] Name the cabinet (display name) so the driver app does not show "Charger 1".
- [ ] Decide whether to switch `LocalAuthListEnabled` to true on RH4 (cards keep working offline) — one click in Charger settings.

## 3b. Partner web portal — done (Oct 9)

- [x] "Cable Connect" menu: tiles (online, free, charging, reliability, today's sessions / kWh / revenue), one card per charger with name, plugs, Start for a walk-in customer (car connected only), Stop, Unlock, out of service, Refresh, Restart, command history, **live session panel** (power, energy, battery, elapsed, cost so far, curve), "Session finished" card, sharing switch with a "what drivers see" preview, sessions with price (row → detail with curve), accepted cards.
- [ ] Build the standalone partner web for its host (`Scripts/build-partner.ps1`) and publish it with the API.
- [ ] A real owner account (role Provider) per pilot station, or the owner's staff as workers, so they can log in. Note: one web session per account — a second login logs the first one out.

## 3c. Team test on dev before go-live (the gate)

Order: publish → the team tests on dev with the simulator and with RH4 on its next visit → fix list → publish to production.

- [ ] Publish API + admin to dev, Cable.Ocpp to AWS; attach the simulator to `ocpp-dev` in demo mode (two cars, `--remote`).
- [ ] Admin: register / sheet / commissioning, control buttons, settings, cards sync, sessions with price, alerts, thresholds, reliability, price alerts page.
- [ ] Partner web: everything in 3b with an owner account; two sessions at once; the finished card; sharing off → driver endpoint returns `NotShared`.
- [ ] Driver endpoints with the mobile build (or curl): start on a `Preparing` plug, `/current`, stop, history with price, start on an empty plug refused.
- [ ] Review `Cable-Connect-Functionality-Review.md`: keep / remove / change per row, decide the open questions.
- [ ] Apply the fix list; re-test what changed.
- [ ] Production switch (§2) and the RH4 settings (§3).

## 4. Driver app (Cable) — teammate

Endpoints are live on dev. Everything below is screens only.

| Screen | Endpoint | Notes |
|---|---|---|
| Station page: plugs free / busy per type, reliability badge | `GET /api/charging-points/{id}/live` | `unavailableReason` tells why nothing is shown |
| Map / list badges | `GET /api/charging-points/live-summary?ids=1,2,3` | max 50 ids |
| **Start charging** on a plug | `POST /api/users/me/ocpp-sessions/start` `{chargingPointId, chargerId, connectorId}` | `chargerId` = `chargers[].id`, `connectorId` = `plugs[].connectorId` from the live endpoint. **The cable must be plugged into the car first** (plug state `Busy` with OCPP status Preparing); an empty plug is refused with "Plug the cable into the car first". `accepted=true` → poll `/current` every 3 s until it appears (≈ 5 s). 400 + message when the station does not offer app charging, the plug is busy with another car, the charger is offline, or a session is already running. |
| **Live session** | `GET /api/users/me/ocpp-sessions/current` | `null` when none. `powerW`, `socPercent`, `energyKwh` so far, `durationSec`, `lastSampleAt`. Poll every 10 s. |
| **Stop** | `POST /api/users/me/ocpp-sessions/{id}/stop` | only the caller's own session; the charger confirms a few seconds later |
| **History + receipt** | `GET /api/users/me/ocpp-sessions?page=1&pageSize=20` | `costFils` / `costJod`, `price[]` per tariff window, `stopReasonText` / `stopReasonTextAr`, `startSource` (Card / App / Operator). Card sessions appear here too when the card is linked to the user. |
| Price alerts | `GET /api/pricing/tou`, `GET/PUT /api/users/me/price-alerts` | send `language` with the push token |
| Push token | `PUT /api/notification-token` with `appType` and `language` | needed for the parked-car alert and price alerts |

## 5. Partner app — teammate

| Screen | Endpoint |
|---|---|
| My station live | `GET /api/provider/charging-points/{id}/live` (no gates) |
| Sharing switch | `GET/PUT /api/provider/charging-points/{id}/live-visibility[/share]` |
| Cabinet name | `PUT /api/provider/charging-points/{id}/chargers/{ocppId}/display-name` |
| Sessions with price | `GET /api/provider/charging-points/{id}/sessions` (`costFils`, `costJod`, `startSource`, `stopReasonText`) |
| **Start for a walk-in customer** | `POST …/chargers/{ocppId}/commands/remote-start` `{connectorId}` |
| Stop / reset / unlock / availability / refresh | `POST …/chargers/{ocppId}/commands/{remote-stop\|reset\|unlock-connector\|change-availability\|trigger-message}` |
| Command history | `GET …/chargers/{ocppId}/commands` |
| Cards | `GET/POST /api/admin/ocpp/authorized-tags` (owner-allowed) |
| Push token | `PUT /api/notification-token` with `appType: 2` — **without it owners get no push at all** |

## 6. Rules in force (change here first if the business wants otherwise)

- **Who may start**: a card from the station list; the driver app (virtual tag `CBL-U{userId}`) only at stations whose live status is open to drivers; the partner app / admin (virtual tag `CBL-S{stationId}`) on their own / any charger.
- **A car must be connected first** (plug Preparing) for every app / operator start. The admin shows "Plug in first" on an empty plug instead of the button.
- **Who may stop**: the card that started it; the driver only their own app or linked-card sessions; owner / manager any session on their chargers; admin any.
- **One running app session per driver.**
- **Price** = energy split over the time-of-use tariff windows the session ran through (Asia/Amman), whole fils, stored on the session with the tariff version. Nothing is charged to anyone yet — there is no payment step.
- A virtual tag is accepted by the charger only against the RemoteStart we sent in the last 10 minutes.

## 7. Open decisions

- Automatic loyalty points per session (N-1): now that every session carries a price and (for app / linked-card sessions) a user, the amount and the commission rule are the only open points.
- Payment: wallet / card in the app, or settle with the station — not started.
- Reservation (RH4 supports `Reservation`): not started.
- Raw-log retention 30 → 7 days. (Per-station alert thresholds: done Oct 9, on the station's Cable Connect tab.)
