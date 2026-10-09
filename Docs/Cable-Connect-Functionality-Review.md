# Cable Connect — functionality by app (for team review)

> One table per app. **Status**: ✅ built · 📱 API ready, mobile screen to build · 💡 proposed, not built.
> **Keep / Remove / Change** is for the reviewer — mark your decision in the last column.
> Rules that apply across apps are in section 5. Reviewed against the build of Oct 9, 2026.

---

## 1. Admin portal (Cable team)

| # | Function | Who | Status | Keep / Remove / Change |
|---|---|---|---|---|
| A1 | Cable Connect dashboard: chargers online / reconnecting / offline, plugs free, cars charging, faults, kWh today, open alerts | Admin | ✅ | |
| A2 | Chargers table with search and filters; commissioning chip for units that never connected; reliability % per charger | Admin | ✅ | |
| A3 | Register a charger: id (max 20 chars), password optional (off by default), connection sheet with server URL + port | Admin | ✅ | |
| A4 | Charger page: identity (vendor, model, firmware, serial), connection state, last boot, raw message log | Admin | ✅ | |
| A5 | Plugs: status, error code, plug type (dropdown) and kW per plug | Admin | ✅ | |
| A6 | Remote control: Refresh (StatusNotification / Heartbeat / MeterValues / Boot), Soft and Hard reset, unit or plug out of service / in service, Unlock cable | Admin | ✅ | |
| A7 | **Start session** on a plug with a car connected (no card), **Stop** a running session | Admin | ✅ | |
| A8 | Command history with "confirmed after N s" (the charger's own follow-up proves the effect) | Admin | ✅ | |
| A9 | Charger settings: read all configuration keys, change a key inline; protected keys (server address, identity, security) refused | Admin | ✅ | |
| A10 | Cards on the unit: push the station's card list (SendLocalList) now; status Synced / Pending / Failed / NotSupported | Admin | ✅ | |
| A11 | Sessions on the charger page: duration, energy, **price**, **started by** (card / driver app / operator), stop reason in words | Admin | ✅ | |
| A12 | Station → Cable Connect tab: chargers, Cable Connect subscription (record / renew / switch off), allowed cards, live-data gates (owner switch on request, admin block with reason), **alert thresholds per station** | Admin | ✅ | |
| A13 | Alerts panel: charger offline, plug faulted, session too long, car parked after charging; open / resolved; push + inbox to admins, owner and managers | Admin | ✅ | |
| A14 | Reliability: daily 30-day score per charger, recompute button, breakdown (online %, fault-free %, incidents) | Admin | ✅ | |
| A15 | Price alerts page (time-of-use tariff versions, quiet hours, subscribers, sent log, dry run) — not OCPP but the tariff prices every session | Admin | ✅ | |
| A16 | Link a physical card to a driver account (so card sessions appear in the driver's history and alerts reach the driver) | Admin | 💡 parked | |
| A17 | Reports: energy and sessions per station per day / week / month, plug utilisation, faults per month, Excel export | Admin | 💡 | |
| A18 | Push to owner / driver on every session start and stop (today only problem alerts are pushed) | Admin config | 💡 | |

## 2. Partner web portal (station owner and managers) — new menu "Cable Connect"

| # | Function | Who | Status | Keep / Remove / Change |
|---|---|---|---|---|
| P1 | Tiles: chargers online, plugs free, cars charging, reliability % (30 days) | Owner, manager | ✅ | |
| P2 | One card per charger: name (editable, shown to drivers), online / offline / **not connected yet**, last update | Owner, manager | ✅ | |
| P3 | Per plug: state (free / busy / out of order), the charger's own status in words (car connected, charging, finished still plugged…), plug type, kW | Owner, manager | ✅ | |
| P4 | **Start session** for a walk-in customer — only when a car is connected ("Plug in first" otherwise) | Owner, manager | ✅ | |
| P5 | **Stop** the running session on a plug | Owner, manager | ✅ | |
| P6 | Unlock cable (not while charging), plug out of service / back in service | Owner, manager | ✅ | |
| P7 | Charger: Refresh, Soft restart | Owner, manager | ✅ | |
| P8 | Command history per charger with confirmation state and who sent it | Owner, manager | ✅ | |
| P9 | Live data for drivers: the three gates (subscription, Cable approval, owner switch) and the owner's switch | Owner, manager | ✅ | |
| P10 | Sessions table: started, charger, plug, duration, energy, **price (JOD)**, started by (card / driver app / you-staff), ended (reason in words) | Owner, manager | ✅ | |
| P11 | Accepted cards: list, add (number + label), switch a card off / on; chargers updated within seconds | Owner, manager | ✅ | |
| P11b | **Live session panel** under a busy plug: power now (animated), energy so far, battery bar, elapsed clock, **cost so far** at the current rate, power / battery curve; refresh every 5 s while active | Owner, manager | ✅ | |
| P11c | Today tiles: sessions, kWh, revenue; a session row opens its detail with the curve and price breakdown | Owner, manager | ✅ | |
| P12 | Register a charger from the portal (today admin only) | Owner | 💡 | |
| P13 | Hard reset, read / change charger settings, raw log (kept admin-only on purpose) | — | ✗ not offered | |
| P14 | Export sessions to Excel / CSV | Owner | 💡 | |
| P15 | Owner-visible alert list (today: push + inbox only) | Owner, manager | 💡 | |

## 3. Partner mobile app (same owner functions as the web)

| # | Function | Status | Keep / Remove / Change |
|---|---|---|---|
| M1 | Station live picture (P1–P3) | 📱 | |
| M2 | Start for a customer / Stop / Unlock / out of service / Refresh / Restart (P4–P7) | 📱 | |
| M3 | Command history (P8) | 📱 | |
| M4 | Live-data switch (P9) | 📱 | |
| M5 | Sessions with price (P10) | 📱 | |
| M6 | Accepted cards (P11) | 📱 | |
| M7 | Charger name (P2) | 📱 | |
| M8 | **Register the push token with the partner app type** — required for every alert push | 📱 required | |
| M9 | Pushes received: charger offline / back online, plug faulted / cleared, session too long, car parked after charging | ✅ backend | |

Endpoints and payloads: `Docs/Cable-Connect-Mobile-Spec.md` §2.

## 4. Driver app (Cable)

| # | Function | Status | Keep / Remove / Change |
|---|---|---|---|
| D1 | Station page: plugs free / busy per plug type, charger names, "Reliable" badge | 📱 | |
| D2 | Map / list badges: "CCS2 · 1 of 2 free" | 📱 | |
| D3 | **Start charging from the app** on a plug with the cable already in the car (no card) | 📱 | |
| D4 | Live session: power now, battery %, energy so far, elapsed time | 📱 | |
| D5 | **Stop from the app** (own session only) | 📱 | |
| D6 | History and receipt: energy, duration, why it ended, **price** with per-window breakdown (no payment taken) | 📱 | |
| D7 | Push: "your car finished charging, please unplug" (own sessions) | ✅ backend | |
| D8 | Price alerts: tariff screen, opt-in per window and lead time, push before a window starts | 📱 | |
| D9 | Register the push token with `language` | 📱 required | |
| D10 | "I have a card": link a card number to the account from the app | 💡 parked with A16 | |
| D11 | Pay at the end of a session (wallet / card), receipt | 💡 | |
| D12 | Reserve a plug (charger supports it) | 💡 | |
| D13 | Loyalty points per session | 💡 decision pending | |
| D14 | Pushes: charging started / charging complete (not only "parked") | 💡 with A18 | |

Endpoints and payloads: `Docs/Cable-Connect-Mobile-Spec.md` §1.

## 5. Rules that apply everywhere (change here first if the team disagrees)

| Rule | Today |
|---|---|
| Who may start a session | A card from the station's list · the driver app (at stations open to drivers) · the partner app / web / admin for a walk-in customer |
| A car must be connected first | Yes, for every app / operator start. An empty plug is refused ("Plug the cable into the car first") |
| Who may stop | The card that started it · the driver only their own · owner / manager any session on their chargers · admin any |
| Running sessions per driver | One |
| Price | Energy × the time-of-use tariff of the window(s) the session ran in; whole fils; informational only, nothing charged |
| Drivers see live plugs when | Cable Connect subscription active **and** owner sharing on **and** not blocked by Cable **and** a charger online |
| Alerts | Offline 15 min · faulted 15 min · session 6 h · parked 20 min (driver) + 20 min (station) — per station adjustable in the admin |
| Reliability | Minutes reachable and fault-free over 30 days; drivers see only "Reliable" at ≥ 95 % |
| Charger registration | Admin only |

## 6. Open questions for the team

1. Should owners register chargers themselves (P12)?
2. Cards: link cards to drivers (A16 / D10), issue Cable cards, or stay app-only?
3. Session start / stop pushes to owner and driver (A18 / D14)?
4. Payment: wallet in the app, settle with the station, or nothing for the pilot (D11)?
5. Loyalty points per session: amount and rule (D13)?
6. Exports / reports (A17, P14): needed for the pilot or later?
