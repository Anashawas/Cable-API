# Sprint 5 — Live charging (B): stickers, session tracking, issues, receipts, plug reports

Backend for `LIVE_CHARGING_SESSION.md` §10, §11.a, §11.b, §11.d and §13.1–13.3, with the default decisions of §8.
SQL: `Scripts/Sprint5_LiveCharging_B.sql` (idempotent). Apply it **before** deploying the API and Cable.Ocpp builds of this
change, because both read the new columns.

All texts the driver sees come in Arabic (default) or English, from `Accept-Language`. Dates in JSON are shown in Jordan
time, as everywhere else. Errors use the usual problem-details body, and business errors add a `code`:

| HTTP | `code` | When |
|---|---|---|
| 404 | `CODE_NOT_FOUND` | The scanned or typed text is not a Cable plug code, or its charger was removed |
| 410 | `CODE_REVOKED` | The code was revoked and renewed (the sticker was replaced) |
| 422 | `CODE_MISMATCH` | A start with `connectorCode` names a different plug than the code |
| 409 | `ISSUE_ALREADY_OPEN` | The session already has an open "problem with this session" |
| 422 | `ISSUE_WINDOW_CLOSED` | The session ended more than 7 days ago |
| 422 | `SESSION_OPEN` | Asked for a receipt while the session is still running |
| 404 | `RECEIPT_NOT_FOUND` | Verify: no receipt was issued with this number |
| 409 | `sessionAlreadyOpen` | (existing) the driver already has a running session |

---

## 1. Plug stickers `CHG-` (§10)

- One code per plug (connector 1..n): `CHG-` + 6 characters from `23456789ABCDEFGHJKMNPQRSTUVWXYZ`. The look-alikes 0 O 1 I L are left out. Codes are random (cryptographic RNG) and unique, and are never issued twice, revoked codes included.
- Codes are issued in three places:
  1. Cable.Ocpp creates one when it first sees a plug (StatusNotification).
  2. The SQL script backfills every existing plug.
  3. Listing a station's codes fills in any plug still missing one.
- The QR holds `https://cable-app.com/c/CHG-XXXXXX`. A `SharedLinkType` row `charger-sticker` (BaseUrl `https://cable-app.com/c`) is seeded so it sits with the other shared links. The App Link / Universal Link and the web fallback page for `/c/…` are web/mobile work: that page calls the public resolve below.

### Resolve (driver app, no sign-in)

`GET /api/charging-points/connectors/resolve?code=CHG-7K2M9Q`

`code` accepts the code or the full link, in any case. Spaces and dashes are ignored, so `chg 7k2m-9q`, `7K2M9Q` and `https://cable-app.com/c/CHG-7K2M9Q?x=1` all work.

```json
{
  "code": "CHG-7K2M9Q",
  "chargingPointId": 37, "stationName": "محطة الخليفة لشحن المركبات",
  "chargerId": 8, "chargerName": "الشاحن الأيمن", "chargerOrdinal": 1,
  "connectorId": 2, "plugTypeId": 3, "plugTypeName": "CCS2", "powerKw": 120,
  "state": "Busy", "canStartFromApp": true, "cannotStartReason": null,
  "suspectedIssue": false
}
```

`cannotStartReason` has two families:
- Plug reasons, the same as `/live`: `offline | plugCable | busy | outOfOrder`.
- Station reasons, when the station does not offer app charging: `noSubscription | notShared | blocked`. With a station reason, `state` is `Unknown` (live data is not shared).

The resolve errors are 404 `CODE_NOT_FOUND` and 410 `CODE_REVOKED`.

### Start from a sticker (§11.d)

`POST /api/users/me/ocpp-sessions/start` takes the existing body plus an optional `connectorCode`:

```json
{ "chargingPointId": 37, "chargerId": 8, "connectorId": 2, "connectorCode": "CHG-7K2M9Q", "chargeLimitKind": "Jod", "chargeLimitValue": 7 }
```

The code must belong to that very charger and plug. Otherwise the start fails with 404 `CODE_NOT_FOUND`, 410 `CODE_REVOKED` or 422 `CODE_MISMATCH`. The proximity rule (another change) still applies.

How the session gets `startMethod = "Sticker"`:
1. The API parks `Sticker` on the plug (`OcppConnector.PendingStartMethod`).
2. Cable.Ocpp's StartTransaction takes it into the session if the start is an App start within 10 minutes. This is the same hand-over as the pending charge limit.
3. A later plain app start on that plug clears a stale parked value.

### Admin

| Endpoint | What |
|---|---|
| `GET /api/admin/ocpp/stations/{cp}/connector-codes?chargerId=` | Active code per plug |
| `POST /api/admin/ocpp/stations/{cp}/connector-codes/{codeId}/renew` | Revoke this code and issue a new one for the same plug. Returns the new row |
| `GET /api/admin/ocpp/stations/{cp}/connector-codes/stickers.pdf?chargerId=` | A4 sticker sheet, 2 × 3 per page |

Each sticker shows the QR, the code, the station, the charger display name (or "Charger N / الشاحن N"), the plug number, the plug type and kW.

The codes list returns rows like this:

```json
[{ "codeId": 12, "code": "CHG-7K2M9Q", "link": "https://cable-app.com/c/CHG-7K2M9Q",
   "chargerId": 8, "chargerName": "الشاحن الأيمن", "chargePointId": "CBL-37-01", "connectorId": 2,
   "plugTypeId": 3, "plugTypeName": "CCS2", "powerKw": 120, "issuedAt": "2026-10-28T19:00:00",
   "revokedCodes": 1, "lastRevokedAt": "2026-10-28T18:55:00" }]
```

### Owner (owner only: workers get 403; admins also pass)

- `GET /api/provider/charging-points/{cp}/connector-codes?chargerId=`
- `GET /api/provider/charging-points/{cp}/connector-codes/stickers.pdf?chargerId=`

---

## 2. Driver details on partner sessions (§11.a)

`GET /api/provider/charging-points/{cp}/sessions` (ConnectView) has new filters and new fields on each row.

New filters:

| Filter | Values |
|---|---|
| `connectorId` | Plug number |
| `startMethod` | `App`, `Sticker`, `Card` or `Operator` |
| `status` | `open` or `closed` |
| `q` | Driver or staff name, or card label |

These come on top of the existing `chargerId`, `from`, `to`, `page` and `pageSize`.

New fields on each row:

```json
{
  "id": 72, "…": "existing fields",
  "driver": { "displayName": "محمد العلي", "phoneMasked": "079****967" },
  "cardLabel": null,
  "startedByUserName": null,
  "startMethod": "Sticker",
  "receiptNo": "CHS-2026-000072"
}
```

Which field is filled depends on how the session started:

| Start | Field |
|---|---|
| App or sticker start, or a card linked to a driver | `driver` |
| Card | `cardLabel` (`OcppAuthorizedTag.Label`) |
| Operator start | `startedByUserName` (the staff member) |

The partner never gets the full phone or the userId.

The owner's single-session view `GET /api/provider/charging-points/{cp}/sessions/{id}/live` carries the same `driver`, `cardLabel`, `startedByUserName` and `startMethod`.

`carName` is **not** included, because `OcppTransaction` has no CarId in this branch. Another change adds the car. Add `carName` to both views once it is merged.

---

## 3. Admin sessions (§11.b)

### List

`GET /api/admin/ocpp/sessions` takes these filters:

| Filter | Meaning |
|---|---|
| `from`, `to` | Start date, in Jordan time when there is no offset |
| `chargingPointId`, `chargerId`, `userId` | Station, charger, user |
| `source` (alias `startMethod`) | `App`, `Sticker`, `Card` or `Operator` |
| `status` | `open` or `closed` |
| `limitKind` | `Jod`, `Kwh`, `Soc` or `none` |
| `q` | Session id (`72` or `#72`), name, phone (`0791234567` / `962…`), idTag or card label |
| `page`, `pageSize` | Default 20, max 200 |

```json
{
  "items": [{
    "id": 72, "chargingPointId": 37, "stationName": "…", "chargerId": 8, "chargerName": "Simulator A", "chargePointId": "SIM-A",
    "connectorId": 2, "startedAt": "…", "stoppedAt": "…", "durationSec": 3840, "isOpen": false, "isStale": false,
    "startSource": "App", "startMethod": "Sticker", "idTag": "CBL-U13795",
    "driver": { "userId": 13795, "name": "…", "phone": "+962791234967" }, "startedByStaff": null, "cardLabel": null,
    "energyKwh": 12.4, "costFils": 2336, "costJod": 2.336, "socStartPercent": 40, "socEndPercent": 80,
    "chargeLimit": { "kind": "Jod", "value": 7, "text": "7.000 JOD", "textAr": "7 دينار", "setAt": "…", "reachedAt": "…" },
    "stopReason": "Remote", "stopReasonText": "Charge limit reached (7.000 JOD)", "stopReasonTextAr": "…",
    "receiptNo": null, "openIssues": 0, "wasRejected": false, "isOrphan": false
  }],
  "totalCount": 1, "page": 1, "pageSize": 20, "totalPages": 1, "hasNextPage": false, "hasPreviousPage": false,
  "totals": { "sessions": 1, "energyKwh": 12.4, "costFils": 2336, "costJod": 2.336, "openNow": 0 }
}
```

The totals cover everything the filters match. `openNow` counts running sessions under the same filters except status and dates. Rejected and orphan sessions are left out of the list.

### Other admin session endpoints

| Endpoint | What |
|---|---|
| `GET /api/admin/ocpp/sessions/{id}` | The full detail of one session (below) |
| `GET /api/admin/ocpp/sessions/export?…same filters…` | CSV, UTF-8 with BOM, Jordan time, at most 50,000 rows. Cells that start with `= + - @` are quoted to block formula injection |
| `GET /api/admin/users/{userId}/ocpp-sessions?status=&page=&pageSize=` | The user-profile tab: same shape as the list |
| `GET /api/admin/ocpp/sessions/{id}/receipt` | The session's receipt (PDF) |

The detail response has these parts:

| Part | Contents |
|---|---|
| `session` | The list row, with the driver's full phone and userId |
| `live` | The existing SessionLiveDto: energy, cost and price lines, battery, limit, stop text and `series` |
| Station and plug | `stationAddress`, `plugTypeName`, `powerKw` |
| Limit | `limitSetByName` |
| Stop | `stoppedByName` (who sent the RemoteStop), `stoppedByLimit` |
| `commands[]` | The session's RemoteStartTransaction calls (failed attempts included) and RemoteStopTransaction calls, with the charger's answers |
| `issues[]` | The session's complaints |
| `pointsAwarded` | Always `null`: there is no per-session points column yet |

The start location (11.c) belongs to the other change.

---

## 4. "Problem with this session" (§13.1)

`POST /api/users/me/ocpp-sessions/{id}/issues` (the driver's own session)

```json
{ "reason": "WrongCost", "note": "الشاشة كانت تقول 1.5 دينار", "imageFileName": "a1b2c3.jpg" }
```

`reason` is `WrongCost`, `StoppedEarly`, `NoEnergy`, `CableStuck`, `PlugBroken` or `Other`. `note` can be up to 450 characters. `imageFileName` is optional: upload the photo first through the file endpoints.

The response is `{ "issueId": 311, "status": "Open" }`.

Rules:
- Allowed while the session is open, or up to 7 days after it ended. After that the answer is 422 `ISSUE_WINDOW_CLOSED`.
- One open issue per session. A second one gets 409 `ISSUE_ALREADY_OPEN`. "Open" means the complaint status is New, Opened or FollowUp.

`GET /api/users/me/ocpp-sessions/{id}/issues` returns the driver's issues on that session:

```json
[{ "issueId": 311, "sessionId": 72, "reason": "WrongCost", "reasonText": "الكلفة غلط", "note": "…", "imageFileName": "a1b2c3.jpg",
   "status": "InReview", "statusText": "قيد المراجعة", "statusCode": 3, "reply": "…", "createdAt": "…", "modifiedAt": "…" }]
```

The driver sees `status` mapped from the complaint status:

| Driver `status` | Complaint status | Arabic text |
|---|---|---|
| `Open` | New | مُرسلة |
| `InReview` | Opened, FollowUp | قيد المراجعة |
| `Resolved` | Solved | تم الحل |
| `Closed` | NotComplaint, Unsolved, SystemIssue | مُغلقة |

### Built on the existing complaints

Each issue is a `UserComplaint` with `kind = "ChargingSession"`, `ocppTransactionId`, `reason`, `imageFileName` and `reply`. `Note` holds the driver's words, or the reason when they wrote none.

The existing lists now return, for every complaint: `kind`, `reason`, `imageFileName`, `reply`, `ocppTransactionId` and `session` (charger, plug, times, energy, cost, startMethod, receiptNo). The lists are:
- `GET /api/usercomplaints/GetComplaintsByChargingPointId/{cp}` (partner)
- `GET /api/usercomplaints/GetAllUserComplaints` (admin), which also takes a new filter `?kind=ChargingSession`
- `GET /api/usercomplaints/GetMyComplaints`

`PATCH /api/usercomplaints/UpdateUserComplaintStatus/{id}` now takes an optional `{ "status": 3, "reply": "…" }`. On a ChargingSession complaint, every status or reply change pushes the driver.

Pushes and inbox rows:

| Event | Recipients | Inbox type |
|---|---|---|
| New issue | Station owner, active managers and admins | `ocpp_session_issue` |
| Status or reply change | The driver | `ocpp_session_issue_status` |

The data carries `issueId`, `sessionId`, `chargingPointId` and `status`.

---

## 5. Session receipt (§13.2)

`GET /api/users/me/ocpp-sessions/{id}/receipt` returns a PDF (A5, in the style of the subscription receipts).

The receipt number is `CHS-{Jordan year of the start}-{session id, 6 digits}`, for example `CHS-2026-000072`. It is stored on the session at the first download and never changes. A running session gets 422 `SESSION_OPEN`.

The receipt shows:
- Header: logo, receipt number and date
- Station: name, address, charger display name, plug number and type
- Driver: name (not shown for walk-in operator starts)
- Session: start, end, duration, battery from → to
- Price lines, e.g. `Off-peak · خارج الذروة   10.200 kWh × 183 fils   1.867 JOD`, and the total
- The note "Estimated cost — pay the station directly / كلفة تقديرية — الدفع للمحطة مباشرة"
- A QR to `https://cable-app.com/r/{receiptNo}`. That web page is still to be built: it should call verify below

The same receipt is available to:
- The owner or managers (ConnectView): `GET /api/provider/charging-points/{cp}/sessions/{id}/receipt`
- The admin: `GET /api/admin/ocpp/sessions/{id}/receipt`

### Public check

`GET /api/receipts/verify/{receiptNo}` needs no sign-in. Only receipts that were actually issued answer.

```json
{ "receiptNo": "CHS-2026-000072", "valid": true, "stationName": "…", "chargerName": "…", "connectorId": 2,
  "startedAt": "…", "stoppedAt": "…", "energyKwh": 12.4, "totalFils": 2336, "totalJod": 2.336,
  "driverFirstName": "محمد", "issuedAt": "…" }
```

An unknown number gets 404 `RECEIPT_NOT_FOUND`.

**Not done: "email me receipts".** An email service exists, but this needs a per-user setting, a hook on session completion and PDF attachments. That is more than trivial, so it is left out.

---

## 6. "The plug didn't work" (§13.3)

`POST /api/charging-points/{cp}/chargers/{chargerId}/connectors/{connectorId}/reports` (signed in)

```json
{ "reason": "Rejected", "commandId": 134, "note": "…" }
```

- `reason` is `NoResponse`, `Rejected`, `ScreenError`, `Damaged` or `Other`.
- `commandId` is the `commandId` from the start's answer. The report then copies the command's `status`, `resultStatus` and `errorCode`.
- The report also stores the plug's last status and error code, and whether the charger was online.

The response is `{ "reportId": 55, "duplicate": false, "openReports": 3, "suspectedIssue": true }`.

The same driver reporting the same plug again within 30 minutes gets the first report back (`duplicate: true`). So one person cannot raise the warning alone.

The station owner and managers get a push and an inbox row (type `ocpp_connector_report`): «سائق ما قدر يشحن من المقبس 2 بالشاحن الأيمن — الشاحن ردّ: Rejected», or in English "A driver could not charge from plug 2 on … — the charger answered: Rejected".

### Reports for the station and the admin

| Endpoint | Who | What |
|---|---|---|
| `GET /api/provider/charging-points/{cp}/connector-reports?status=open\|resolved\|all&page=&pageSize=` | ConnectView | Paged reports. The reporter shows as name and masked phone |
| `PATCH /api/provider/connector-reports/{id}/resolve` | ConnectControl, or admin | Body `{ "note": "…", "allOnPlug": true }`. Resolves every open report on that plug by default. Returns `{ "resolved": n }` |
| `PATCH /api/admin/ocpp/connector-reports/{id}/resolve` | Admin | Same |
| `GET /api/admin/ocpp/connector-reports?status=open\|all&chargingPointId=&from=&to=` | Admin | Reports grouped per plug |

Each group in the admin list has: `openReports`, `reportsToday`, `totalReports`, `suspectedIssue`, `lastReportAt`, `lastReason`, `lastCommandResult` and the latest 5 reports. The busiest plugs come first.

### In `/live`

Every plug in `/live` now has `suspectedIssue` and `openReports`. This applies to both the driver view and the owner view.

`suspectedIssue` is true when at least 3 open reports fall within 1 hour of each other, all newer than the plug's last successful session start. So it clears when the reports are resolved, or when a session starts on the plug. It is a warning only: `canStartFromApp` does not change.

---

## 7. Data changes (`Scripts/Sprint5_LiveCharging_B.sql`)

| Object | Change |
|---|---|
| `dbo.OcppConnectorCode` | New table. Code is unique. One active code per plug (filtered unique index) |
| `dbo.OcppConnectorCode` backfill | One code for every plug with ConnectorId > 0 |
| `dbo.OcppConnectorReport` | New table |
| `OcppTransaction` | `StartMethod` (backfilled from `StartSource`), `ReceiptNo` (filtered unique index), `ReceiptIssuedAt` |
| `OcppConnector` | `PendingStartMethod`, `PendingStartMethodAt` |
| `UserComplaints` | `Kind` (default `General`), `OcppTransactionId` (FK and index), `Reason`, `ImageFileName`, `Reply` |
| Seeds | NotificationType `ocpp_connector_report`, `ocpp_session_issue`, `ocpp_session_issue_status`; SharedLinkType `charger-sticker` |

The final SELECT should show `ActiveCodes = Plugs` and `SessionsWithoutMethod = 0`.

---

## 8. Dev test steps (station 37, simulator chargers)

Use a test driver account, not a personal number.

1. **Script.** Run the SQL on dev, then check the final SELECT.
2. **Stickers.**
   1. As admin, call `GET /api/admin/ocpp/stations/37/connector-codes`: every plug has a code.
   2. Download `stickers.pdf` and scan a QR with a phone: it opens `https://cable-app.com/c/CHG-…`.
   3. Call `GET /api/charging-points/connectors/resolve?code=<code>` without a token. Try lower case, a missing dash and the full link.
   4. Revoke and renew: the old code answers 410 `CODE_REVOKED` and the new one resolves.
   5. A random `CHG-ABCDEF` answers 404 `CODE_NOT_FOUND`.
   6. As the station owner, the provider list and PDF work. As a worker of the station they return 403.
3. **Sticker start.**
   1. Plug the simulator in (Preparing), then start with `connectorCode` and limit 1 kWh.
   2. The admin list and the partner list show `startMethod: "Sticker"`.
   3. A start with the code of another plug gets 422 `CODE_MISMATCH`.
   4. A plain start afterwards shows `App`.
4. **Partner list.**
   1. Call `GET /api/provider/charging-points/37/sessions?startMethod=Sticker&status=closed&q=<driver name>`.
   2. Check `driver.phoneMasked` looks like `079****967` and there is no userId.
   3. Add a card with a label at the station, charge with it, and check `cardLabel`.
   4. Start from the partner app and check `startedByUserName`.
5. **Admin.**
   1. Call `GET /api/admin/ocpp/sessions?q=0791234967` and `?q=72`, and check the totals.
   2. Open `GET /api/admin/ocpp/sessions/72`: the full phone, the RemoteStart/RemoteStop commands, `live.series`.
   3. Download `/export` and open it in Excel: the Arabic shows correctly.
   4. Call `GET /api/admin/users/{id}/ocpp-sessions`.
6. **Issues.**
   1. Report an issue on a finished session: you get `{ issueId, status: "Open" }` and the owner, managers and admins get a push.
   2. A second report gets 409 `ISSUE_ALREADY_OPEN`.
   3. The partner sees it in `GetComplaintsByChargingPointId/37` with `session`.
   4. As admin, call `UpdateUserComplaintStatus` with `{ "status": 3, "reply": "…" }`: the driver gets a push and `GET …/issues` shows `InReview` and the reply.
   5. On a session older than 7 days the answer is 422 `ISSUE_WINDOW_CLOSED`.
7. **Receipt.**
   1. On a running session the receipt is 422 `SESSION_OPEN`.
   2. After the stop, the PDF shows the right numbers and `receiptNo` appears in the lists.
   3. `GET /api/receipts/verify/CHS-2026-0000xx` returns the first name only.
   4. The owner and admin receipt endpoints return the same PDF.
8. **Plug reports.**
   1. Stop the simulator and start: the start fails. Report with the start's `commandId`: the owner gets the push with the charger's answer.
   2. Report from 3 accounts within an hour: `/live` shows `suspectedIssue: true` and `openReports: 3`.
   3. Resolve the report, or start a session successfully on that plug: the flag clears.
   4. Check the admin grouping at `GET /api/admin/ocpp/connector-reports`.
