# Sprint 5 — Mobile handoff (driver app + partner app)

**Who this is for:** the mobile developers and their AI assistant. This one file is enough to build the mobile side of
the three Sprint 5 features. You do not need the other Sprint 5 backend docs.

| Feature | Original team spec | Backend status |
|---|---|---|
| 1. Live charging session | `LIVE_CHARGING_SESSION.md` | Done, on dev |
| 2. Partner billing | `PARTNER_BILLING.md` | Done, on dev |
| 3. Cable Gifts + promo codes | `CABLE_GIFTS_AND_PROMOS.md` | Done, on dev |
| — Worker tracker | (4th spec) | **Postponed — not built, ignore it** |

The backend is published on **dev** (2026-10-30). Production is unchanged until the team finishes testing.

> **Rule for the AI reading this:** where this file and the original team spec differ, **this file wins**. Section 0.2
> lists every decision that overrides the spec.

---

## 0. Read first

### 0.1 Common rules (all endpoints)

- **Environments.**
  - API base: `https://dev.cable-app.com` (all paths below start with `/api/...`).
  - OCPP server: `ocpp-dev.cable-app.com`. Apps never call it directly.
  - Partner web for comparison: `dev.cable-app.com/partner`. Admin: `dev.cable-app.com/admin`.
- **Auth.** Same JWT as today (`Authorization: Bearer …`). Endpoints marked *anonymous* need no token.
- **Language.** Send `Accept-Language: ar` or `en` on every call. Every name, message, error `detail` and reason text
  comes back in that language. Pushes use the language saved with the FCM token.
- **Dates.** Returned as Jordan local time **without a zone**, e.g. `"2026-10-28T17:00:00"`. Show them as they are; do
  not convert. Dates you send without an offset are read as Jordan time.
- **Money.** JOD with 3 decimals (`2.336`). Some fields are fils (`costFils: 2336`, `priceFils: 175`); 1 JOD = 1000 fils.
- **Errors.** Problem-details JSON. Business errors add a `code`. Always show `detail`: it is ready-to-show text in the
  request language.

  | HTTP | Meaning | Body |
  |---|---|---|
  | 400 | Validation | `errors: { field: [messages] }` |
  | 403 | Not allowed (e.g. a worker calling billing) | — |
  | 404 | Not found (some carry a `code`) | `detail`, `code?` |
  | 409 | Conflict | `detail`, `code` |
  | 410 | Gone (revoked sticker) | `detail`, `code` |
  | 422 | Business rule refused | `detail`, `code`, plus extra fields listed per endpoint |

  ```jsonc
  // 422 example
  { "status": 422, "detail": "لازم تكون عند المحطة لتبدأ الشحن (أنت على بعد 1240 م، والحد 300 م).",
    "code": "TOO_FAR_FROM_STATION", "distanceMeters": 1240, "maxMeters": 300 }
  ```
- **Enums** are strings in JSON (`"giftType": "Physical"`).
- **The apps never compute business numbers.** Discounts, final amounts, points, "points missing", "balance after",
  "expires in N days", cost so far and distances all come from the server. Just display them.
- **Apps.** "Driver app" = the Cable app (FirebaseAppType user app). "Partner app" = StationApp (owners and workers).

### 0.2 Decisions that override the original specs (confirmed 2026-10-28)

| # | Original spec said | Final decision — build this |
|---|---|---|
| D1 | LIVE 4.7: automatic loyalty points when a charging session ends | **Not used.** No automatic points. Do **not** build "+N points" on the session screens. `pointsAwarded` exists on the DTOs but stays `null` — ignore it. |
| D2 | LIVE 4.9: driver links an RFID card to their account | **Removed.** Cards belong to stations and their staff only. No card screen in the driver app. The endpoints do not exist. |
| D3 | PARTNER_BILLING: point value | **50 points = 1 JOD** (the DB conversion rate). The server does the maths; the app shows server numbers only. |
| D4 | PARTNER_BILLING: Connect vs Premium | **A Cable Connect subscription includes Premium.** Paying Connect also extends Premium to the same date. Treat `features[]` as the truth (§2.1). |
| D5 | LIVE §13.1: issue photo sent as `imageFileName` in the issue body | **Changed.** Create the issue first, then upload the photo to its own endpoint (§1.9). `imageFileName` in the body is ignored. |
| D6 | User app partner-code responses had commission | `commissionAmount` / `commissionPercentage` are **always 0** in every user-app response (partners list, partner details, preview, scan result, my transactions). Stop reading them; they will be removed. |
| D7 | — | **iOS (App Store rule):** in the partner app on iOS, hide all prices and every upgrade / renew / trial / buy button. Show the plan, the state, the features and the counters only. The API returns the same data on both platforms. |

### 0.3 Open items the mobile team must answer

1. **Deep link scheme.** The sticker web page (`/c/{code}`) opens the app with the placeholder `cable://charge/{code}`.
   Confirm the real scheme, or we change the page.
2. **App Links / Universal Links** for `https://cable-app.com/c/*` and `/r/*`. We need:
   - Android: the package name and the SHA-256 signing fingerprint (for `assetlinks.json`);
   - iOS: the Team ID and the bundle ID (for `apple-app-site-association`).

---

## 1. Live charging session (driver app + partner app)

### 1.1 Station live picture — can this plug start from the app?

`GET /api/charging-points/{chargingPointId}/live` (existing endpoint, new fields on every plug):

| Field | Values | UI |
|---|---|---|
| `canStartFromApp` | `true` / `false` | Show the "Start" button only when `true` |
| `cannotStartReason` | `offline` · `plugCable` · `busy` · `outOfOrder` · `null` | `plugCable` = "connect the car to the plug first". "Start" appears once the car is plugged in (plug `Preparing`). |
| `suspectedIssue` | bool | Warning badge "drivers reported a problem with this plug". It does **not** block starting. |
| `openReports` | int | Number of open "plug didn't work" reports |

Station-level reasons (no subscription, not shared) stay in the existing `unavailableReason`.

### 1.2 Station price (tariff)

`GET /api/charging-points/{id}/tariff` — signed in, driver app.

```jsonc
{
  "chargingPointId": 37,
  "source": "station",            // station = the station's own tariff | national = the national tariff
  "hasOwnTariff": true,
  "tariff": { "version": 2, "timezone": "Asia/Amman", "currency": "JOD", "unit": "fils/kWh",
              "windows": [ { "key": "offPeak", "startMin": 300, "endMin": 1020, "tier": "offPeak",
                             "priceFils": 175, "nameEn": "Off-peak", "nameAr": "خارج الذروة" } /* … */ ] },
  "now": {
    "key": "offPeak", "tier": "offPeak", "nameEn": "Off-peak", "nameAr": "خارج الذروة", "priceFils": 175,
    "endsAt": "2026-10-28T17:00:00",
    "next": { "key": "peak", "priceFils": 213, "startMin": 1020 },
    "textAr": "الآن: خارج الذروة · 175 فلس/kWh",
    "textEn": "Now: Off-peak · 175 fils/kWh"
  }
}
```

- Show `now.textAr` / `now.textEn` on the station page and in the start sheet.
- Windows are minutes since midnight in Jordan time; tiers are `offPeak | partial | peak`.
- Partner app (read-only, same body): `GET /api/provider/charging-points/{cp}/tariff` (needs ConnectView).

### 1.3 Start a session (driver)

`POST /api/users/me/ocpp-sessions/start`

```jsonc
{
  "chargingPointId": 37, "chargerId": 8, "connectorId": 2,
  "lat": 31.95391, "lng": 35.91062, "accuracyMeters": 12,   // phone position — REQUIRED by default
  "carId": 451,                                             // optional — see below
  "connectorCode": "CHG-7K2M9Q",                            // optional — only when started from a sticker (§1.5)
  "chargeLimitKind": "Jod", "chargeLimitValue": 7           // optional — "stop at…" (§1.4)
}
```

`200`:

```jsonc
{ "commandId": 134, "accepted": true, "status": "Answered", "resultStatus": "Accepted",
  "idTag": "CBL-U13795", "message": "The charger accepted. …",
  "distanceMeters": 42,     // phone → station; null if a position is unknown
  "carId": 451 }            // the car recorded on the session; null = none
```

Keep `commandId`: the "plug didn't work" report (§1.10) needs it.

**Location rule.** The server refuses when `distance − min(accuracyMeters, 100) > 300 m` (admin setting). A station
with no coordinates is always allowed. Ask for location permission before calling. Send the best fix you have, with its
accuracy.

**Car.** `carId` is a **UserCar id**. `GET /api/carmanagement/GetAllUserCars` returns it as
`carModels[].plugTypes[].userCarId`. If you send no `carId`, the server uses the driver's only car when they have exactly
one, else none. When the driver has several cars, let them pick one in the start sheet.

**Errors:**

| HTTP | `code` | When | Extra fields | UI |
|---|---|---|---|---|
| 422 | `LOCATION_REQUIRED` | no `lat`/`lng` | `maxMeters` | Ask for location permission / turn on GPS |
| 422 | `TOO_FAR_FROM_STATION` | too far away | `distanceMeters`, `maxMeters` | "You are 1240 m away; you must be within 300 m" (use `detail`) |
| 409 | `sessionAlreadyOpen` | the driver already has a running session | — | "You have a running session — view it" → open `/current` |
| 404 | `CODE_NOT_FOUND` | sticker code unknown | — | |
| 410 | `CODE_REVOKED` | sticker was replaced | — | |
| 422 | `CODE_MISMATCH` | the code belongs to another plug | — | |
| 400 | — | `lat` without `lng`, values out of range, a `carId` that is not yours, bad limit | `errors` | |

### 1.4 Charge limit ("stop at…")

| kind | value | example |
|---|---|---|
| `Jod` | 0.5 – 200 | 7 → stop at 7 JOD at this station's tariff |
| `Kwh` | 1 – 300 | 20 → stop at 20 kWh |
| `Soc` | 10 – 100, whole number | 80 → stop at 80 % battery |

- Show the `Soc` option **only** when the chosen car has `reportsSoc: true` (from `GetAllUserCars`
  `carModels[].reportsSoc`) **and** the charger is DC. `null` = not seen yet → hide it.
- The server stops at or slightly under the limit, never over.
- Set it at start (fields above), or change / clear it on a running session:
  `PUT /api/users/me/ocpp-sessions/{id}/limit` with `{ "kind": "Kwh", "value": 20 }`. To clear it:
  `{ "kind": null, "value": null }`. It returns the new `chargeLimit` (or null).
- Shape on every session DTO:

  ```json
  { "kind": "Jod", "value": 7, "text": "7 JOD", "textAr": "7 دينار", "setAt": "2026-10-20T20:36:44", "reachedAt": null }
  ```

  `reachedAt` set on a still-open session = the stop is being sent → show "Stopping — limit reached".

### 1.5 Plug stickers (QR) — scan to charge

Each plug has a sticker. The QR holds `https://cable-app.com/c/CHG-XXXXXX`.

- Codes are `CHG-` + 6 characters from `23456789ABCDEFGHJKMNPQRSTUVWXYZ`.
- **Scan / type flow in the driver app:**
  1. The driver scans the QR (or types the code; spaces, dashes and case do not matter; the full URL is accepted too).
  2. Call the *anonymous* `GET /api/charging-points/connectors/resolve?code=CHG-7K2M9Q`:

     ```json
     { "code": "CHG-7K2M9Q", "chargingPointId": 37, "stationName": "محطة الخليفة لشحن المركبات",
       "chargerId": 8, "chargerName": "الشاحن الأيمن", "chargerOrdinal": 1,
       "connectorId": 2, "plugTypeId": 3, "plugTypeName": "CCS2", "powerKw": 120,
       "state": "Busy", "canStartFromApp": true, "cannotStartReason": null, "suspectedIssue": false }
     ```

     `cannotStartReason` here can also be a station reason: `noSubscription | notShared | blocked` (then `state` is
     `Unknown`).
  3. Show the plug card. On "Start", call `POST /start` with the ids from step 2 **and** `connectorCode`. The session is
     then recorded with `startMethod: "Sticker"`.
- Resolve errors: 404 `CODE_NOT_FOUND`, 410 `CODE_REVOKED` ("this sticker was replaced — scan the new one").
- **Deep link:** opening `https://cable-app.com/c/{code}` should open the app straight on step 2 (see §0.3). Without the
  app, the web page shows the plug and an "Open in app" button.

### 1.6 Running session and details

**Current session (driver polls every 10 s):** `GET /api/users/me/ocpp-sessions/current`

New and existing fields while open:

- `costFils` / `costJod` / `price[]` — **cost so far**, at the station's tariff.
- `currentRateFils`, `currentWindowNameEn`, `currentWindowNameAr` — "Now: Off-peak · 175 fils/kWh".
- `tariffSource` — `station | national`.
- `energyKwh`, `powerW`, `currentA` (amps), `voltageV`, `temperatureC`.
- `socStartPercent`, `socPercent`, `socEndPercent`.
- `startedAt`, `stoppedAt`, `chargeLimit`.
- `car: { id, name, reportsSoc }` — e.g. "Hyundai Ioniq 5".
- `stopReasonText` / `stopReasonTextAr` once stopped.

Hide a tile when its value is `null`. Not every charger sends temperature or battery %.

**Session details with the chart:** `GET /api/users/me/ocpp-sessions/{id}` (own sessions only, else 404).

- Fields: `stationName`, `chargerName`, the fields above, `price[]` lines, `receiptNo` once issued.
- `series[]` (up to 120 points): `{ at, powerW, socPercent, energyKwh, currentA, voltageV, temperatureC }` → the
  power / battery chart.

**History:** `GET /api/users/me/ocpp-sessions?page=1&pageSize=20&month=2026-10`

- `month` (YYYY-MM, Jordan month) is optional. Without it you get everything. A malformed month → 400.
- Rows carry `car`, `tariffSource`, `socStartPercent → socEndPercent`, `chargeLimit`, `startMethod`, `receiptNo`.

**Monthly summary (header of the history screen):** `GET /api/users/me/ocpp-sessions/summary?month=2026-10`

```json
{ "month": "2026-10", "sessions": 7, "energyKwh": 142.315, "costJod": 27.612, "pointsAwarded": 330 }
```

- Without `month` = the current month.
- Ignore `pointsAwarded` (D1).
- The month picker drives both the summary and the history `?month=`.

### 1.7 Driver pushes (Cable app)

All are FCM + an inbox row, in the device language. Tapping one opens the session screen (`sessionId`).

| When | Inbox type | `data` |
|---|---|---|
| Session started | `charging_session_started` (id 8) | `{ "type": "ocpp_session", "status": "started", "sessionId": "74", "chargingPointId": "37" }` |
| Session finished (incl. limit stop) | `charging_session_completed` (id 9) | `status: "completed"` |
| Car finished, still plugged ("could you unplug it?") | `charging_session_parked` | `status: "parked"` |
| Admin / partner changed the status or reply of my issue | `ocpp_session_issue_status` | `{ type, issueId, sessionId, chargingPointId, status }` |

- Only sessions the driver started from the app or a sticker trigger these. A start by station staff never notifies
  anyone, and sessions a charger replays after being offline are not pushed.
- There is no separate "limit reached" push: the "finished" push names the limit.

### 1.8 Receipt

| What | Endpoint |
|---|---|
| Driver PDF | `GET /api/users/me/ocpp-sessions/{id}/receipt` → A5 PDF; 422 `SESSION_OPEN` while running |
| Public verification page | `GET /api/receipts/verify/{receiptNo}` (anonymous) → `{ receiptNo, valid, stationName, chargerName, connectorId, startedAt, stoppedAt, energyKwh, totalFils, totalJod, driverFirstName, issuedAt }`; 404 `RECEIPT_NOT_FOUND` |

- The receipt number is `CHS-{year}-{6 digits}`. It is fixed at the first download.
- The PDF's QR points at `https://cable-app.com/r/{receiptNo}` (a web page; an optional App Link, §0.3).
- Driver app: a "Download / share receipt" button on a closed session.
- Partner app (ConnectView): `GET /api/provider/charging-points/{cp}/sessions/{id}/receipt`.

**Email receipts (setting, off by default):**

- `GET /api/users/me/ocpp-settings` → `{ "emailReceipts": false, "hasEmail": true }`.
- `PUT /api/users/me/ocpp-settings` with `{ "emailReceipts": true }`.
- When `hasEmail` is false, disable the switch with "add an email to your account".
- When on, each finished session emails the PDF once.

### 1.9 "Problem with this session" (driver)

Allowed while the session runs, or up to 7 days after it ended. One open issue per session.

1. **Create:** `POST /api/users/me/ocpp-sessions/{id}/issues`

   ```json
   { "reason": "WrongCost", "note": "الشاشة كانت تقول 1.5 دينار" }
   ```

   - `reason` is one of `WrongCost | StoppedEarly | NoEnergy | CableStuck | PlugBroken | Other`.
   - `note` is optional, up to 450 characters.
   - Response: `{ "issueId": 311, "status": "Open" }`.
   - Errors: 409 `ISSUE_ALREADY_OPEN`, 422 `ISSUE_WINDOW_CLOSED`.
2. **Photo (optional, D5):** `POST /api/users/me/ocpp-sessions/{id}/issues/{issueId}/photo`
   - Multipart, form key **`file`**, one jpg / png / webp image, at most 5 MB.
   - Response: `{ "issueId": 311, "imageFileName": "…" }`.
   - A second upload replaces the first.
3. **List:** `GET /api/users/me/ocpp-sessions/{id}/issues`

   ```json
   [{ "issueId": 311, "sessionId": 72, "reason": "WrongCost", "reasonText": "الكلفة غلط", "note": "…",
      "imageFileName": "…", "status": "InReview", "statusText": "قيد المراجعة", "statusCode": 3,
      "reply": "…", "createdAt": "…", "modifiedAt": "…" }]
   ```

   Driver statuses: `Open` (مُرسلة) · `InReview` (قيد المراجعة) · `Resolved` (تم الحل) · `Closed` (مُغلقة). Show
   `statusText` and `reply`.
4. **View the photo** (driver, partner owner / managers, admin): `GET /api/usercomplaints/{issueId}/photo` → the image
   (with the token).

**Partner app:**

- These issues appear in the existing complaints list `GET /api/usercomplaints/GetComplaintsByChargingPointId/{cp}`.
- Each item now has `kind` (`ChargingSession` or `General`), `reason`, `imageFileName`, `reply`, `ocppTransactionId`
  and `session` (charger, plug, times, energy, cost, startMethod, receiptNo).
- Show the session block and the photo (when `imageFileName` is set).
- `PATCH /api/usercomplaints/UpdateUserComplaintStatus/{id}` takes an optional `{ "status": 3, "reply": "…" }`. Each
  change pushes the driver.
- The owner / managers get a push on a new issue: inbox type `ocpp_session_issue`, data
  `{ type, issueId, sessionId, chargingPointId, status }`.

### 1.10 "The plug didn't work" (driver)

When a start fails (rejected, no answer, screen error…), offer "Report this plug":

`POST /api/charging-points/{cp}/chargers/{chargerId}/connectors/{connectorId}/reports`

```json
{ "reason": "Rejected", "commandId": 134, "note": "…" }
```

- `reason` is one of `NoResponse | Rejected | ScreenError | Damaged | Other`.
- `commandId` = the one from the start response.
- Response: `{ "reportId": 55, "duplicate": false, "openReports": 3, "suspectedIssue": true }`.
- A second report on the same plug by the same driver within 30 minutes returns the first one (`duplicate: true`).
  Show "thanks" either way.

**Partner app:**

- `GET /api/provider/charging-points/{cp}/connector-reports?status=open|resolved|all&page=&pageSize=` (ConnectView).
  The reporter shows as name + masked phone.
- `PATCH /api/provider/connector-reports/{id}/resolve` with `{ "note": "…", "allOnPlug": true }` (ConnectControl) →
  `{ "resolved": n }`.
- Push to the owner / managers: inbox type `ocpp_connector_report`, data
  `{ type, reportId, chargingPointId, chargerId, connectorId }`.
- The `suspectedIssue` badge on plugs in `/live` (3 open reports within 1 hour). It clears when the reports are
  resolved or a session starts on that plug.

### 1.11 Partner app — Cable Connect sessions

**Sessions list:** `GET /api/provider/charging-points/{cp}/sessions` (ConnectView)

- Filters: `chargerId`, `from`, `to`, `page`, `pageSize`, and new ones: `connectorId`,
  `startMethod=App|Sticker|Card|Operator`, `status=open|closed`, `q` (driver / staff name or card label).
- New per row: `driver: { displayName, phoneMasked }` (e.g. `079****967`), `cardLabel`, `startedByUserName`,
  `startMethod`, `receiptNo`, `carName`, `socStartPercent`, `socEndPercent`, `chargeLimit`.
- Who started it:
  - `driver` → an app / sticker start;
  - `cardLabel` → an RFID card of the station;
  - `startedByUserName` → staff started it from the partner app.
- The partner never gets the driver's full phone or userId.

**Live session:** `GET /api/provider/charging-points/{cp}/sessions/{tx}/live`

- Same new fields, plus `temperatureC`, `currentA`, `voltageV`, the battery start → now, and `series[]` (with amps,
  volts and temperature per point).
- Tiles: Power, Energy, Amps, Voltage, Temperature, Battery start → now, Cost, Started / Finished. Hide null tiles.

**Charge limit from the partner app:**

- At start: `POST /api/provider/charging-points/{cp}/chargers/{charger}/commands/remote-start` with
  `{ "connectorId": 1, "chargeLimitKind": "Jod", "chargeLimitValue": 7 }`.
- On a running session: `PUT /api/provider/charging-points/{cp}/sessions/{tx}/limit` (ConnectControl), same body as
  the driver's.

**Stickers (owner only; workers get 403):**

- `GET /api/provider/charging-points/{cp}/connector-codes?chargerId=` — the code of each plug.
- `GET /api/provider/charging-points/{cp}/connector-codes/stickers.pdf?chargerId=` — the A4 sticker sheet; open or
  share the PDF.

**Live updates (SignalR, unchanged):**

- Hub `{apiBase}/hubs/ocpp`, `JoinStation(chargingPointId)`, server method `ocpp` with
  `{ type: connector|session|charger, status, chargingPointId, chargerId, connectorId, transactionId, at }`.
- On a message, refetch (coalesce messages within ~300 ms).
- Reconnect forever with backoff (max 30 s). After a reconnect, call `JoinStation` again and refetch once.
- Polling while connected: 60 s for the live picture, 30 s for an open session. When not connected: 15 s / 5 s.
- The driver app does **not** use SignalR; it polls `/current` every 10 s.

### 1.12 Live charging — mobile checklist

**Driver app**

- [ ] Station page: tariff "now" line; per-plug Start / reason / suspected-issue badge.
- [ ] Start sheet: location permission + position (with accuracy) sent on start; car picker (`userCarId`); "stop at…"
  (JOD / kWh / % only when `reportsSoc` is true and the charger is DC).
- [ ] Handle `LOCATION_REQUIRED`, `TOO_FAR_FROM_STATION` (show the distance), `sessionAlreadyOpen` (open the running
  session).
- [ ] QR scanner + "type the code" → resolve → plug card → start with `connectorCode`; handle 404 / 410 / 422.
- [ ] Deep link `https://cable-app.com/c/{code}` → resolve screen (after §0.3).
- [ ] Running session: cost so far, current rate, amps / volts / temperature / battery, limit (change / clear),
  "Stopping — limit reached".
- [ ] Session details: chart from `series[]`, price lines, car, receipt download / share.
- [ ] History with month picker + monthly summary card (no points).
- [ ] Pushes: started / completed / parked / issue status → open the session.
- [ ] "Problem with this session": reason, note, optional photo (2-step upload), list with status + reply.
- [ ] "Plug didn't work" report after a failed start (send `commandId`).
- [ ] Settings: email receipts switch (disabled without email).
- [ ] Remove any "link my RFID card" screen (D2) and any automatic points text (D1).

**Partner app**

- [ ] Sessions list: new filters, driver (masked phone) / card label / staff name, start method badge (App, Sticker,
  Card, Operator), car name, battery from → to, receipt.
- [ ] Live session: new tiles, battery start → now, limit set / change / clear.
- [ ] Station tariff (read-only).
- [ ] Stickers list + PDF (owner only).
- [ ] Plug reports list + resolve; suspected-issue badge on plugs; push `ocpp_connector_report`.
- [ ] Complaints: charging-session issues with the session block, photo, status and reply; push `ocpp_session_issue`.
- [ ] Receipt download for a session.

---

## 2. Partner billing (partner app)

**Owner only.** Workers get 403 on every billing endpoint. Hide the billing menu for workers.

**iOS (D7):** show the plan, the state, the features and the counters. Hide every price and every
upgrade / renew / trial / buy button.

### 2.1 My plan — `GET /api/provider/subscription?providerType=ChargingPoint&providerId=37`

`providerType` is `ChargingPoint` or `ServiceProvider`.

```jsonc
{
  "providerType": "ChargingPoint", "providerId": 37,
  "tier": "connect",               // key of the highest plan that is on; "free" otherwise
  "tierPlanId": 3, "tierName": "كيبل كونكت",
  "state": "expiringSoon",         // active | expiringSoon | inGrace | expired | switchedOff | none
  "expiresAt": "2026-11-17T00:00:00", "daysLeft": 5,
  "graceEndsAt": null, "isTrial": false,
  "features": ["verifiedBadge","editInfo","appearFirst","images","complaints","analytics","workers","biometrics","connect"],
  "subscriptions": [ { "entityType": "OcppConnect", "status": "ExpiringSoon", "isOn": true, "expiresAt": "…",
                       "planKey": "connect", "subscriptionId": 12 } ],
  "loyalty": {
    "enrolled": true,
    "billingModel": "bundle",      // percentage | bundle
    "commissionPercentage": null,  // the owner's own % on the percentage model; null on bundle
    "minimumTransactionAmount": 2.0,
    "bundle": { "planSubscriptionId": 41, "planId": 4, "planKey": "loyalty_bundle_400", "planName": "حزمة 400 مسحة",
                "scansTotal": 400, "scansUsed": 132, "scansRemaining": 268, "percentRemaining": 67,
                "isUnlimited": false, "isTrial": false, "startsAt": "…", "endsAt": null,
                "state": "active" }       // active | low | exhausted | expired
  },
  "pendingUpgradeRequest": null,   // or { "id": 4, "tier": "premium", "planId": 2, "kind": "upgrade", "months": 3, "priceOptionId": null, "createdAt": "…" }
  "trialAvailable": { "premium": false, "connect": false, "loyalty_bundle_400": false, "loyalty_unlimited": true }
}
```

- **`features[]` is the single source of truth.** Switch features on these keys instead of the old `isPremium` /
  station-plan checks. Keys: `verifiedBadge, editInfo, appearFirst, images, complaints, analytics, workers, biometrics,
  connect`.
- Connect includes Premium (D4), so a Connect station has all the premium keys.
- Home banner:
  - `expiringSoon` → "your plan ends in N days";
  - `inGrace` → "grace until `graceEndsAt`";
  - `expired` / `switchedOff` on the free plan → "your plan ended — renew";
  - `none` → never had a plan.
- Bundle counter: "268 of 400 scans left" plus a bar from `percentRemaining`. `state` `low` / `exhausted` → warning.
  `isUnlimited` → "unlimited".
- `pendingUpgradeRequest` set → show "Request sent — Cable will call you" and disable a new request.

### 2.2 Plans comparison — `GET /api/plans?providerType=ChargingPoint&category=Subscription`

- Any signed-in user. `category` is `Subscription` or `Loyalty`.
- Returns the active, visible plans: `name` (in the request language) + `nameAr` / `nameEn`, features with names,
  price options (`months`, `priceJod`, `scans`, `isUnlimited`), points rule, trial days / scans.
- Use it for the "compare plans" table. Hide prices on iOS.

### 2.3 Payments and history

- `GET /api/provider/subscription/payments?providerType=&providerId=`

  ```json
  [ { "id": 6, "referenceNo": "RCP-2026-000006", "amount": 40.0, "currency": "JOD", "method": "كليك",
      "paidDate": "…", "periodStart": "…", "periodEnd": "…", "planMonths": 1, "entityType": "OcppConnect",
      "receiptDownloadPath": "/api/subscriptions/payments/6/receipt" } ]
  ```

  The receipt PDF is at `receiptDownloadPath` (owner can download; Cable Connect receipts included now).
- `GET /api/provider/subscriptions/history?providerType=&providerId=` — every plan period, newest first. Use `planName`,
  `status`, `startsAt`, `endsAt`, `isTrial`, `pricePaidJod`, `scansIncluded` / `scansUsed`, `receiptDownloadPath`.
  - Statuses: `Active | ExpiringSoon | InGrace | Expired | SwitchedOff | Scheduled | Exhausted | Cancelled | Upgraded | Renewed`.

### 2.4 Request an upgrade / renewal / bundle — `POST /api/provider/upgrade-requests`

```json
{ "providerType": "ChargingPoint", "providerId": 42, "tier": "premium", "months": 3, "note": "…" }
```

- `tier` is a plan key (`premium`, `connect`, `loyalty_bundle_400`, `loyalty_unlimited`, …). Or send `planId`;
  `priceOptionId` is optional.
- Response: `200 { "id": 4, "status": "Pending" }`.
- `409 UPGRADE_REQUEST_PENDING` while one is open.
- There is no in-app payment. Cable's team calls the owner, records the payment, and the request is fulfilled
  automatically → an activation push.
- Hidden on iOS.

### 2.5 Free trial — `POST /api/provider/trials`

```json
{ "providerType": "ChargingPoint", "providerId": 42, "planId": 2 }
```

- Returns the trial record. Show the button only when `trialAvailable[planKey]` is true. Hidden on iOS.
- Errors: `409 TRIAL_ALREADY_USED`, `409 ACTIVE_SUBSCRIPTION_EXISTS`, `422 TRIAL_NOT_AVAILABLE`, `422 PLAN_ARCHIVED`,
  `422 PLAN_NOT_APPLICABLE`.

### 2.6 Scan bundles (loyalty partners)

- A partner is on `percentage` (everyone today) or `bundle` (prepaid scans). Only the admin switches it.
- Counter in other places: `GET /api/offers/GetWalletBalance` → `loyaltyBundle` (same shape as `loyalty.bundle`; null
  when never on a bundle); `GET /api/provider/my-assets` → `loyaltyBundles: [{ providerType, providerId, billingModel,
  bundle }]`.
- The wallet history shows a `bundlePurchase` line (wallet transaction type 20) when a bundle is sold.
- **Bundle used up:** creating a new partner QR code (`CreateTransaction`) returns `409 bundleExhausted` with the
  ready text "خلصت حزمة المسحات — جدّد لتكمل تعطي نقاط لزباينك". Show it with a "Request renewal" action (not on iOS).
  Codes created earlier still confirm, with 0 points.

### 2.7 Billing pushes (partner app, owner)

The inbox types are `billing_subscription` and `billing_bundle`. `data` always has `type`, `providerType` and
`providerId`. Tap → open the "My plan" screen of that provider.

| `data.type` | When |
|---|---|
| `subscriptionSevenDays`, `subscriptionOneDay` | 7 days / 1 day before expiry |
| `subscriptionExpired`, `subscriptionGraceEnded` | Expiry day; features switched off after grace |
| `trialEnding`, `trialEnded` | 2 days before a trial ends; trial ended |
| `subscriptionActivated`, `bundleActivated` | A payment / admin action switched a plan or bundle on |
| `bundleThreshold` | Bundle at 20 %, 10 % or 0 % scans left |

Subscription pushes also carry `entityType` and `subscriptionId`.

### 2.8 Billing — mobile checklist (partner app)

- [ ] "My plan" screen (owner only): tier, state banner, expiry / days left, features list, bundle counter.
- [ ] Gate features on `features[]` (replace `isPremium` / plan checks).
- [ ] Compare plans (`/api/plans`).
- [ ] Request upgrade / renew / bundle; pending state; 409 handling.
- [ ] Trial button from `trialAvailable`.
- [ ] Payments list + receipt PDF; subscription history.
- [ ] Bundle counter on wallet / my-assets; `409 bundleExhausted` when creating a code.
- [ ] Billing pushes → "My plan".
- [ ] **iOS:** no prices, no upgrade / renew / trial / buy buttons.
- [ ] Workers: no billing menu (403 anyway).

---

## 3. Cable Gifts and promo codes (driver app, plus the partner app transaction view)

### 3.1 Cable Gifts — points shop (driver app)

| Method | Path | |
|---|---|---|
| GET | `/api/cable-gifts` | Active gifts for the user's city |
| GET | `/api/cable-gifts/{id}` | One gift |
| POST | `/api/cable-gifts/{id}/order` | Buy with points. **Header `Idempotency-Key: <uuid>`** |
| GET | `/api/cable-gifts/my-orders?status=&page=&pageSize=` | "My gifts", paged (default 20) |

Gift (list item and details):

```jsonc
{ "id": 12, "name": "كاسة كيبل", "description": "…", "howToReceive": "…",
  "giftType": "Physical",            // Physical | ChargeCredit | Voucher | Other
  "pointsCost": 300, "imageUrl": "https://…/CableGifts/….png",
  "remaining": 14,                   // null = unlimited
  "startsAt": null, "endsAt": "2026-12-31T23:59:00",
  "myOrdersCount": 0, "perUserLimit": 1,
  "canOrder": false,
  "cannotOrderReason": "INSUFFICIENT_POINTS",   // or null
  "cannotOrderMessage": "نقاطك ما بتكفي",
  "myBalance": 260,
  "pointsMissing": 40,               // "you need 40 more points"
  "balanceAfterOrder": null }        // "balance after purchase" when affordable
```

- Out-of-stock gifts stay in the list (`remaining: 0`, `cannotOrderReason: "OUT_OF_STOCK"`) → grey card.
- Button state: `canOrder`. Otherwise show `cannotOrderMessage`.

**Order:**

```jsonc
// POST /api/cable-gifts/12/order    Header: Idempotency-Key: 6f1c0e9e-…
{ "contactPhone": "0791234567", "note": "بستلم من عمّان" }   // body optional; phone defaults to the account phone
// 200
{ "orderId": 88, "orderCode": "GFT-7K2M9Q", "status": "Pending", "pointsSpent": 300, "newBalance": 140 }
```

- Make **one UUID per tap** and reuse it on retries. A retry with the same key returns the same order and does not
  charge twice.
- `422` codes: `INSUFFICIENT_POINTS` (+ `pointsCost`, `balance`, `pointsMissing`), `OUT_OF_STOCK`, `LIMIT_REACHED`,
  `GIFT_EXPIRED`, `GIFT_NOT_AVAILABLE`, `LOYALTY_BLOCKED`. Show `detail`.
- The purchase appears in the points history (`GetMyPointsHistory`, action `GIFT_PURCHASE`). A cancel adds a refund
  line (`GIFT_REFUND`), shown in the reader's language.

**My gifts item:**

```jsonc
{ "orderId": 88, "orderCode": "GFT-7K2M9Q", "giftId": 12, "giftName": "كاسة كيبل", "giftImageUrl": "…",
  "giftType": "Voucher", "pointsSpent": 300,
  "status": "Fulfilled",            // Pending | Fulfilled | Cancelled
  "contactPhone": "0791234567", "note": "…",
  "fulfillmentNote": "WASH-1234",   // only when Fulfilled — voucher code / note → add a copy button
  "fulfilledAt": "…", "cancelReason": null, "cancelledAt": null,
  "pointsRefunded": false,          // true for Cancelled
  "createdAt": "…" }
```

**Push:** when Cable fulfils or cancels an order. Inbox type `cable_gift_order`, data
`{ "type": "cable_gift_order", "orderId": "88", "status": "Fulfilled" | "Cancelled" }` → open My gifts / the order.

### 3.2 Promo codes — "My codes" (driver app)

| Method | Path | |
|---|---|---|
| POST | `/api/promos/claim` | `{ "code": "cable 20" }` — case, spaces and dashes are ignored |
| GET | `/api/promos/my?status=Available\|Used\|Expired` | "My codes" tabs (default Available) |
| GET | `/api/promos/my/count` | `{ "available": 2 }` — badge |

Card (claim response and list item):

```jsonc
{ "userPromoId": 31, "campaignId": 4, "title": "غسيل مجاني", "description": "…",
  "effectType": "DiscountPercent",   // DiscountPercent | DiscountFixed | PointsMultiplier | BonusPoints
  "effectValue": 100, "maxDiscountJod": 5,
  "scope": "عند مغاسل إربد",          // where it works — ready text
  "status": "Available", "source": "Code",   // Code (typed) | Assigned (given by Cable)
  "claimedAt": "…", "expiresAt": "2026-11-26T23:59:00",
  "expiresInDays": 29, "expiringSoon": false,  // expiringSoon = within 2 days → highlight
  "usedAt": null, "usedAtProviderName": null, "savedJod": null, "bonusPoints": null }   // filled on Used cards
```

Claim errors (`422`, show `detail`):

- `PROMO_NOT_FOUND`, `PROMO_EXPIRED`, `PROMO_ALREADY_CLAIMED`, `PROMO_LIMIT_REACHED`, `PROMO_NOT_ELIGIBLE`,
  `PROMO_NOT_STARTED`, `PROMO_ALREADY_USED`;
- `TOO_MANY_ATTEMPTS` (10 wrong codes in an hour; `retryAfterMinutes: 60`).

**Pushes** (data has `type` and `campaignId`) → open My codes:

- `promo_assigned` — Cable gave the user a code;
- `promo_expiring` — a code is about to expire.

### 3.3 Using a promo when scanning a partner QR (driver app)

The existing flow: the partner creates a `PTR-…` code → the driver scans it → preview → confirm. Add a promo picker
between preview and confirm.

1. **Preview:** `GET /api/partners/PreviewPartnerCode?code=PTR-8KQ2M4`

   ```jsonc
   { "transactionAmount": 4.000, "basePoints": 40, "multiplier": null, "pointsToBeAwarded": 40,
     "commissionAmount": 0, "commissionPercentage": 0,          // always 0 now (D6) — ignore
     "pointsAvailable": true,
     "pointsUnavailableReason": null,   // belowMinimumAmount | partnerInactive | partnerQuotaReached | fullyDiscounted
     "minimumAmountForPoints": null,    // e.g. 2.0 → "the minimum for points here is 2 JOD"
     "eligiblePromos": [
       { "userPromoId": 31, "title": "غسيل مجاني", "effectType": "DiscountPercent", "effectValue": 100,
         "maxDiscountJod": 5, "expiresAt": "…", "discountJod": 4.000, "finalAmountJod": 0.000, "bonusPoints": 0 },
       { "userPromoId": 35, "title": "نقاط ×2", "effectType": "PointsMultiplier", "effectValue": 2,
         "maxDiscountJod": null, "expiresAt": "…", "discountJod": 0, "finalAmountJod": 4.000, "bonusPoints": 40 } ] }
   ```

   - Show `eligiblePromos` as choices; each card already has its result (discount, final amount, bonus points).
   - When `pointsAvailable` is false, explain the reason:
     - `belowMinimumAmount` → "the minimum for points here is {minimumAmountForPoints} JOD";
     - `partnerInactive` → "this partner gives no points now";
     - `partnerQuotaReached` → "this partner has no points left right now";
     - `fullyDiscounted` → "fully paid by the promo — no points".
2. **When the user picks a promo**, preview again with `&userPromoId=31`:

   ```jsonc
   { "transactionAmount": 4.000,
     "promo": { "userPromoId": 31, "title": "غسيل مجاني", "effectType": "DiscountPercent",
                "discountJod": 4.000, "finalAmountJod": 0.000, "bonusPoints": 0 },
     "basePoints": 0, "pointsToBeAwarded": 0, "pointsAvailable": false, "pointsUnavailableReason": "fullyDiscounted" }
   ```

   Show "You pay {finalAmountJod} instead of {transactionAmount}" and the new points.
3. **Confirm:** `POST /api/partners/ScanPartnerCode?code=PTR-8KQ2M4&userPromoId=31`, or the JSON body
   `{ "code": "PTR-8KQ2M4", "userPromoId": 31 }`. Without `userPromoId` the scan is exactly as before.

   ```jsonc
   { "providerName": "…", "transactionAmount": 4.000, "currencyCode": "JOD", "commissionAmount": 0, "pointsAwarded": 0,
     "promo": { "userPromoId": 31, "title": "غسيل مجاني", "discountJod": 4.000, "finalAmountJod": 0.000, "bonusPoints": 0 } }
   ```

   Scan errors (`422`): `PROMO_NOT_APPLICABLE`, `PROMO_ALREADY_USED`, `PROMO_EXPIRED`, `PROMO_BUDGET_EXHAUSTED`,
   `PROMO_NOT_FOUND`. If the scan fails, the promo stays Available.

- One promo per transaction.
- The partner must collect the final amount from the customer; Cable pays the partner its share later.

### 3.4 Partner app — promo on transactions

`GET /api/partners/provider/GetTransactionById/{id}` and `GET /api/partners/provider/GetProviderTransactions` carry,
for a transaction with a promo:

```jsonc
"promo": { "title": "غسيل مجاني", "originalAmountJod": 4.000, "discountJod": 4.000,
           "finalAmountJod": 0.000,     // "collect from the customer"
           "cableShareJod": 4.000,      // "Cable pays you"
           "providerShareJod": 0.000 }  // the partner's own part of the discount
```

- Show it clearly on the transaction (and on the confirmation screen if you show one): "Collect 0.000 JOD from the
  customer — Cable pays you 4.000 JOD".
- Settlements (`GetProviderSettlement(s)`) have a new field `promoDiscountsPaidByCable`: the line "Discounts paid by
  Cable" (already included in `netBalance`).

### 3.5 Gifts and promos — mobile checklist

**Driver app**

- [ ] Gifts shop: list, details, order with `Idempotency-Key`, all reasons / messages from the server, grey out of stock.
- [ ] My gifts: status, voucher code with a copy button, cancelled = points refunded.
- [ ] Gift push → My gifts.
- [ ] My codes: claim by typing a code, tabs Available / Used / Expired, `expiringSoon` highlight, count badge.
- [ ] Promo pushes → My codes.
- [ ] Partner QR scan: promo picker from `eligiblePromos` → re-preview with `userPromoId` → scan with `userPromoId`.
- [ ] Show the no-points reasons (`pointsUnavailableReason` + `minimumAmountForPoints`).
- [ ] Stop showing commission anywhere in the user app (D6).

**Partner app**

- [ ] Transaction details / list: promo block ("collect from the customer", "Cable pays you").
- [ ] Settlement: "Discounts paid by Cable" line.

---

## 4. Testing on dev

- **Accounts:** ask the backend team for the dev test logins (driver, partner owner, partner worker). Use one session
  per account. Never use personal numbers.
- **Stations:**
  - **37** is the main test station (charger simulator, partner agreement);
  - **210** is the Rashdan station with the simulator.
  - Ask the backend team to run the charger simulator (`Cable.Ocpp.TestClient`) when you need a live session. It sends
    power, amps, volts, battery % and temperature every 5 s, so every tile and limit can be tested without a car.
- **Location rule:** to start from the app you must send coordinates within 300 m of the station. For testing, send the
  station's own coordinates, or ask the backend team to switch `requireLocation` off on dev.
- **Stickers:** the admin prints codes from `dev.cable-app.com/admin` (station → sticker codes). Test the web page at
  `dev.cable-app.com/c/{code}` and the receipt page at `dev.cable-app.com/r/{receiptNo}`.
- **Gifts / promos:** the admin creates a gift and a promo campaign in the admin web. Give the test user points with
  admin "adjust points".
- **Billing:** the admin records a payment or creates a subscription in the admin web; the partner app should update
  "My plan" and receive the activation push within ~10 minutes.
- **Swagger** on dev lists every endpoint above with its summary.

---

## 5. Quick reference — all new / changed endpoints for mobile

| App | Method | Path | § |
|---|---|---|---|
| Driver | GET | `/api/charging-points/{id}/live` (new plug fields) | 1.1 |
| Driver | GET | `/api/charging-points/{id}/tariff` | 1.2 |
| Driver | POST | `/api/users/me/ocpp-sessions/start` (lat/lng/accuracy, carId, connectorCode, limit) | 1.3 |
| Driver | PUT | `/api/users/me/ocpp-sessions/{id}/limit` | 1.4 |
| Driver | GET | `/api/charging-points/connectors/resolve?code=` (anonymous) | 1.5 |
| Driver | GET | `/api/users/me/ocpp-sessions/current` | 1.6 |
| Driver | GET | `/api/users/me/ocpp-sessions/{id}` | 1.6 |
| Driver | GET | `/api/users/me/ocpp-sessions?month=&page=&pageSize=` | 1.6 |
| Driver | GET | `/api/users/me/ocpp-sessions/summary?month=` | 1.6 |
| Driver | GET | `/api/carmanagement/GetAllUserCars` (`userCarId`, `reportsSoc`) | 1.3 / 1.4 |
| Driver | GET | `/api/users/me/ocpp-sessions/{id}/receipt` | 1.8 |
| Driver | GET | `/api/receipts/verify/{receiptNo}` (anonymous) | 1.8 |
| Driver | GET / PUT | `/api/users/me/ocpp-settings` | 1.8 |
| Driver | POST / GET | `/api/users/me/ocpp-sessions/{id}/issues` | 1.9 |
| Driver | POST | `/api/users/me/ocpp-sessions/{id}/issues/{issueId}/photo` | 1.9 |
| Both | GET | `/api/usercomplaints/{id}/photo` | 1.9 |
| Driver | POST | `/api/charging-points/{cp}/chargers/{chargerId}/connectors/{connectorId}/reports` | 1.10 |
| Driver | GET | `/api/cable-gifts`, `/api/cable-gifts/{id}`, `/api/cable-gifts/my-orders` | 3.1 |
| Driver | POST | `/api/cable-gifts/{id}/order` (Idempotency-Key) | 3.1 |
| Driver | POST / GET | `/api/promos/claim`, `/api/promos/my`, `/api/promos/my/count` | 3.2 |
| Driver | GET | `/api/partners/PreviewPartnerCode?code=&userPromoId=` | 3.3 |
| Driver | POST | `/api/partners/ScanPartnerCode` (+ `userPromoId`) | 3.3 |
| Partner | GET | `/api/provider/charging-points/{cp}/tariff` | 1.2 |
| Partner | GET | `/api/provider/charging-points/{cp}/sessions` (filters + driver fields) | 1.11 |
| Partner | GET | `/api/provider/charging-points/{cp}/sessions/{tx}/live` | 1.11 |
| Partner | PUT | `/api/provider/charging-points/{cp}/sessions/{tx}/limit` | 1.11 |
| Partner | GET | `/api/provider/charging-points/{cp}/sessions/{id}/receipt` | 1.8 |
| Partner | GET | `/api/provider/charging-points/{cp}/connector-codes` (+ `/stickers.pdf`) | 1.11 |
| Partner | GET | `/api/provider/charging-points/{cp}/connector-reports` | 1.10 |
| Partner | PATCH | `/api/provider/connector-reports/{id}/resolve` | 1.10 |
| Partner | GET | `/api/usercomplaints/GetComplaintsByChargingPointId/{cp}` (session issues) | 1.9 |
| Partner | PATCH | `/api/usercomplaints/UpdateUserComplaintStatus/{id}` (+ reply) | 1.9 |
| Partner | GET | `/api/provider/subscription` | 2.1 |
| Partner | GET | `/api/plans` | 2.2 |
| Partner | GET | `/api/provider/subscription/payments`, `/api/provider/subscriptions/history` | 2.3 |
| Partner | POST | `/api/provider/upgrade-requests` | 2.4 |
| Partner | POST | `/api/provider/trials` | 2.5 |
| Partner | GET | `/api/offers/GetWalletBalance` (`loyaltyBundle`), `/api/provider/my-assets` (`loyaltyBundles`) | 2.6 |
| Partner | — | `CreateTransaction` → `409 bundleExhausted` | 2.6 |
| Partner | GET | `GetTransactionById` / `GetProviderTransactions` (`promo`), settlements (`promoDiscountsPaidByCable`) | 3.4 |

### Push / inbox types

| Type (`data.type` or inbox type) | App | Opens |
|---|---|---|
| `ocpp_session` (`started` / `completed` / `parked`) | Driver | Session |
| `ocpp_session_issue_status` | Driver | Session issues |
| `cable_gift_order` | Driver | My gifts |
| `promo_assigned`, `promo_expiring` | Driver | My codes |
| `ocpp_session_issue` | Partner | Complaint |
| `ocpp_connector_report` | Partner | Plug reports |
| `billing_subscription` / `billing_bundle` (`data.type` per §2.7) | Partner | My plan |
