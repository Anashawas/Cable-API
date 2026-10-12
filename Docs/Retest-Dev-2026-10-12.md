# Retest on dev — Oct 12, 2026

> Everything below is **live on dev** now (`http://dev.cable-app.com`, API published Oct 12; charger server
> `ocpp-dev` on the new build since Oct 11). We already ran each check once on dev — the expected results are
> what we saw. Please retest from your side and reply with ✅ / ❌ per line (with the request / response for any ❌).
>
> Background: `Bugfix-Report-2026-10-11.md` (the four bugs) and `Banner-Call-Action-BE-Reply.md` (banner type 6).
>
> | You reported / requested | Retest in |
> |---|---|
> | Empty privileges → all 9 (worker 9, station 37), `usesAllPrivileges` false | §1 |
> | `NotConnected` from dev while the admin showed Online; `webSocketBaseUrl` = production | §2 |
> | `NotConnected` returned with `accepted: true` | §3 |
> | Session #37 price `null` after the stop | §4 |
> | `BANNER_CALL_ACTION_BE_SPEC_AR.md` (type 6, number rules, `+962` output, data clean-up) | §5, §7 |
> | (found by us) anyone logged in could change banners | §6 |

---

## 0. Setup

| Item | Value |
|---|---|
| API | `http://dev.cable-app.com` |
| Admin | `http://dev.cable-app.com/admin` |
| Partner web | `http://dev.cable-app.com/partner` |
| **Charger server for dev** | **`wss://ocpp-dev.cable-app.com/ocpp16/`** + charger id (e.g. `CBL-SIM-37`) |

- Point every simulator used with dev at **ocpp-dev** — not `ocpp.cable-app.com` (that is production's server).
- The simulator must **keep counting its meter up** across sessions: a stop reading lower than the start reading is treated as
  a replaced / reset meter → `energyKwh: null`, no price (by design — that is why session #40 has no price).
- One partner **web** session per account: logging in on a second browser logs the first one out. Use two different
  accounts (owner + worker) or a private window per account.

---

## 1. Worker permissions — empty list means *no* permission

Was: `{"privileges": []}` stored `""` and the worker got all 9.

| # | Step | Expected |
|---|---|---|
| 1.1 | Owner: `PUT /api/workers/{providerManagerId}/privileges` `{"privileges": []}` (or switch everything off in the partner web → Workers → Save) | 200, `privileges: []`, `usesAllPrivileges: false` |
| 1.2 | Owner: `GET /api/workers?providerType=ChargingPoint&providerId={station}` | `privileges: []`, `usesAllPrivileges: false` |
| 1.3 | Worker: `GET /api/provider/my-assets` | that station's `access[].privileges` is `[]`, `isOwner: false` |
| 1.4 | Worker: `GET /api/provider/charging-points/{station}/live` | **403** |
| 1.5 | Worker in the partner web | no "Cable Connect", no "Workers"; station details still visible |
| 1.6 | Owner: save `["ConnectView"]` | worker sees Cable Connect **read-only** (no buttons, info line); a command → 403 |
| 1.7 | Owner: save all 9 keys (or "Allow everything") | `usesAllPrivileges: true`; worker has everything again |

> Worker `providerManagerId = 9` (worker100, station 37) still has the empty list from your earlier test → after the
> deploy it correctly has **no** permissions. Switch them back on (1.7) if you need it for other tests.

---

## 2. Commands from dev reach the charger

Was: `NotConnected — "CBL-SIM-37 is not connected to this server"` while the admin showed Online, and the credentials sheet showed `wss://ocpp.cable-app.com`.

| # | Step | Expected |
|---|---|---|
| 2.1 | Connect `CBL-SIM-37` to `wss://ocpp-dev.cable-app.com/ocpp16/` | admin and partner show it Online |
| 2.2 | Admin or partner: **Refresh** on that charger | `status: "Answered"`, `resultStatus: "Accepted"`, `accepted: true`; the simulator logs the TriggerMessage |
| 2.3 | With a car on a plug (`Preparing`): **Start session** | accepted; within a few seconds the plug is Charging and a session appears, started by **Operator** |
| 2.4 | **Stop** that session | accepted; the session closes with reason "Stopped from the app" |
| 2.5 | Admin → Cable Connect → a charger → credentials / connection sheet, or the API: `GET /api/admin/ocpp/charge-points/{id}`, register (`POST /api/admin/ocpp/charge-points`), rotate password | `webSocketBaseUrl` = **`wss://ocpp-dev.cable-app.com`** (not `ocpp.cable-app.com`) |

---

## 3. `accepted` is false when the charger did not answer

Was: `NotConnected` came back with `accepted: true`.

| # | Step | Expected |
|---|---|---|
| 3.1 | Disconnect the simulator, wait until it shows Offline, then send Refresh | `status: "NotConnected"`, **`accepted: false`**, red message in the admin / partner web |
| 3.2 | Driver app start on an offline charger (if you test the driver endpoints) | `accepted: false`, message "The charger did not answer…" (not "The charger accepted") |

---

## 4. Price right after the session ends

Was: session #37 showed 0.092 JOD while charging, then `null` after the stop.

| # | Step | Expected |
|---|---|---|
| 4.1 | Run a session with a growing meter, stop it | in the partner web the **"Session finished"** card shows the price immediately |
| 4.2 | `GET /api/provider/charging-points/{station}/sessions/{id}/live` right after the stop | `isOpen: false`, `costFils` / `costJod` / `price[]` **filled** |
| 4.3 | Sessions list (`…/sessions`) | the new row has its price |

On our run: session **#41** — 0.275 kWh, **50 fils**, stored 1 second after the stop.

---

## 5. Banner action 6 — phone call (admin)

| # | Step | Expected |
|---|---|---|
| 5.1 | Admin → Banners → add: action **«اتصال هاتفي»**, number `0791234567` | saved; `GetAllBanners` returns `actionType: 6`, `actionUrl: "+962791234567"`, `phone: "+962…"` |
| 5.2 | Same with Arabic digits `٠٧٩١٢٣٤٥٦٧` | same result |
| 5.3 | Landline `06 555 1234` / service `911` | saved; returned `+96265551234` / `911` |
| 5.4 | Number `abc` | **400** «رقم الهاتف غير صحيح» |
| 5.5 | `actionType: 7` (via Swagger) | **400** «نوع إجراء البانر غير معروف…» |
| 5.6 | WhatsApp banner (type 2) | `actionUrl` returned as **bare digits** (`962…`, no `+`) — on purpose, wa.me needs it |
| 5.7 | Existing banners 1–5 | behave as before; `phone` now comes back as `+962…` |

> Do **not** publish a type-6 banner to real users until the app update with `case '6'` is in the stores (spec §4).
> Delete the test banners afterwards.

---

## 6. Banners can only be changed by admins

Was: any logged-in account (driver or partner) could add / edit / delete banners and their images.

| # | Step | Expected |
|---|---|---|
| 6.1 | With a **partner or driver** token: `POST /api/banners/AddBanner`, `PUT /api/banners/UpdateBanner/{id}`, `DELETE /api/banners/DeleteBanner/{id}` | **403** |
| 6.2 | Same token: `POST /api/bannerAttachment/AddBanner/{id}` (image upload), `DELETE /api/bannerAttachment/DeleteBanner/{id}` | **403** |
| 6.3 | No login: `GET /api/banners/GetAllBanners`, `GET /api/banners/GetNearbyBanners` | 200 (unchanged) |
| 6.4 | Admin account in the admin portal: add / edit / delete a banner and its image | works as before |

---

## 7. Banner phone clean-up (admin, once)

`POST /api/banners/admin/normalize-phones?dryRun=true` with an **admin** login (Swagger) → report; then `?dryRun=false` to apply.

Expected on dev: banner **19** `0775338750` → `962775338750`, banner **40** `٠٧٩٧٩٤٠١٤٤` → `962797940144` (changed);
banners **50** `00010101001` and **51** `sdffsdsdf` listed as **Invalid** (left untouched — fix them by hand in the admin).
Running it a second time changes nothing.
