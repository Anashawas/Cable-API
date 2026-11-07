# Worker tracker — Mobile & web handoff (partner app, partner web, admin)

**Who this is for:** the mobile / web developers and their AI assistant. This one file is enough to build the client
side of the worker tracker (`WORKER_TRACKER_FEATURE.md`). The backend is complete and tested on **dev**; production
is untouched until the feature is tested end to end.

> **Rule for the AI reading this:** where this file and `WORKER_TRACKER_FEATURE.md` differ, **this file wins**.
> Section 0.2 lists every decision that overrides the original document.

---

## 0. Read first

### 0.1 Common rules

- **Environment:** `https://dev.cable-app.com` (all paths start with `/api/…`). Partner web: `dev.cable-app.com/partner`. Admin: `dev.cable-app.com/admin`.
- **Auth:** the same JWT as today. Partner **web** sends `X-Client-App: provider-web` on the login calls, as it does now.
- **Language:** send `Accept-Language: ar` or `en`. Every message, `detail`, hint and name comes back in that language.
  Pushes use the language saved with the FCM token.
- **Dates and times:** JSON dates are **Jordan local time without a zone** (`"2026-11-07T11:32:00"`). Show them as they
  are. Dates you send (leave dates, correction times) are read as Jordan time. Calendar dates are `YYYY-MM-DD`.
- **The app never sends a time for a punch** and never computes a balance, a late figure or a distance. The server does.
- **Errors:** problem-details JSON; business errors carry `code` and a ready-to-show `detail`:

  | HTTP | Meaning |
  |---|---|
  | 400 | validation — `errors: { field: [messages] }` |
  | 401 | `INVALID_CREDENTIALS` (worker login), or the session ended (suspended / removed / password changed) → go to login |
  | 403 | `PASSWORD_CHANGE_REQUIRED`, `WORKER_INACTIVE`, `PRIVILEGE_MISSING`, `FEATURE_NOT_IN_PLAN`, `CANNOT_RECORD_SELF`, `CANNOT_DECIDE_OWN`, `OWNER_ONLY`, or no code (not allowed) |
  | 404 | `NOT_A_WORKER` (`/api/workers/me` for a non-worker) or plain not found |
  | 409 | `WORKER_LIMIT_REACHED`, `PHONE_ALREADY_A_WORKER`, `OWNER_CANNOT_BE_WORKER`, `ACCOUNT_NOT_ELIGIBLE`, `PHONE_IN_USE`, `UPGRADE…`, `ALREADY_IN`, `NOT_IN`, `OVERLAPS_EXISTING` |
  | 410 | `TEMP_PASSWORD_EXPIRED` |
  | 422 | `OUTSIDE_ZONE`, `LOW_ACCURACY`, `TOO_EARLY`, `NOTE_REQUIRED`, `INSUFFICIENT_BALANCE`, `LEAVE_NOT_PENDING`, `LEAVE_ALREADY_STARTED`, `ATTACHMENT_NOT_ALLOWED` (+ extra fields listed per endpoint) |
  | 423 | `LOCKED` — `until`, `retryAfterMinutes` |

### 0.2 Decisions that override the original document (confirmed 3 Nov 2026)

| # | Document said | Build this instead |
|---|---|---|
| D1 | §3.0 — split every phone into a driver account and a partner account, migrate data, force-update both apps | **No split.** One account per phone. A worker gets a **second password for the partner app** only. The Cable (driver) app is untouched: no forced update of it, nothing changes for drivers. Only the partner app needs a forced update (its worker screen changes shape). |
| D2 | A worker can work at several stations | **One station per worker.** A phone that is already a worker anywhere → `409 PHONE_ALREADY_A_WORKER`. No "add him here too?" step and no "which station?" question when punching. |
| D3 | A worker of another owner gets a link request + OTP | **Not in v1.** Same 409 as above ("contact Cable support"). |
| D4 | — | An owner's phone cannot be a worker (`409 OWNER_CANNOT_BE_WORKER`); admin accounts neither (`409 ACCOUNT_NOT_ELIGIBLE`). |
| D5 | OTP by WhatsApp first | **SMS only** for now (`channel: "sms"` in the login answer). |
| D6 | Share message links to `cable-app.com/partner-app` | Links to **`https://cable-app.com/partner/login`**. The server builds the whole message (`shareMessage`); do not compose it in the app. |
| D7 | The forced password change is an app screen | **Also enforced by the server:** with the temporary password still in place every call except change-password / refresh / logout / `workers/me` answers `403 PASSWORD_CHANGE_REQUIRED`. |
| D8 | — | Suspending a worker logs them out **immediately** (next request → 401), not "within a minute". |
| D9 | Privileges stored as "everything incl. future keys" | Always an explicit list. New keys are never on by themselves. |
| D10 | Roles | Nothing changes anyone's role. The partner app decides from `/api/provider/my-assets` (owner) and `/api/workers/me` (worker), never from the role. |
| D11 | §3.7 case 13 "today you are on leave — record work?" | The app asks; if yes, the punch is accepted and counts as extra work; the leave stays approved. |

### 0.3 Open items the mobile team must answer

1. **Partner app forced update** — `GET /api/workers` returns a **list** now; the current build reads one object.
2. **iOS location text** must mention punching (§7.1 #10) and the "Always" keys must go — otherwise App Store review fails.
3. **Biometric storage** — the refresh token must live in biometric-bound storage (Keystore / Keychain) so a newly enrolled fingerprint invalidates it (§3.4). Confirm your library can detect that before relying on it.

---

## 1. Worker login (partner app + partner web)

### 1.1 Owner adds a worker — the wizard in one call

`POST /api/workers` (owner)

```jsonc
{ "providerType": "ChargingPoint", "providerId": 210, "name": "ليث عبيدات", "phone": "0791234567",
  "email": null,                                   // optional
  "privileges": ["PointOfSale", "Attendance"],     // keys from GET /api/workers/privileges?providerType&providerId; [] = station details only
  "tempPassword": null }                           // optional: the owner's own (8+ chars); otherwise generated (8 readable chars, no 0 O 1 I L)
```

`200`:

```jsonc
{ "workerUserId": 14153, "providerManagerId": 14,
  "tempPassword": "K7M4Q9TX",                       // shown ONCE — the server keeps only the hash
  "tempPasswordExpiresAt": "2026-11-06T15:46:03",   // 72 h
  "shareMessage": "أهلاً ليث 👋\nضافك محمد كموظف بـ«…»…", // ready text: phone, temp password, login link, validity
  "existingAccount": false,                         // true = the phone already had a Cable (driver) account — nothing of theirs changed
  "lockedPrivileges": ["PointOfSale"] }             // granted but not allowed by the station's plan (shown locked)
```

- **Step 1 of the wizard** (check before going on): `GET /api/workers/check-phone?providerType=&providerId=&phone=` →
  `{ canAdd, reason, message }` — reasons `WORKER_LIMIT_REACHED | PHONE_ALREADY_A_WORKER | OWNER_CANNOT_BE_WORKER | ACCOUNT_NOT_ELIGIBLE | INVALID_PHONE`.
  It never says whether the phone has a Cable account.
- **Step 2** (privileges): `GET /api/workers/privileges?providerType=&providerId=` → `[{ key, locked, reason }]`;
  `reason` = `needsPremium | needsConnect | needsLoyalty`. Show locked keys as disabled switches with the reason.
- **Success screen:** `tempPassword`, `tempPasswordExpiresAt`, and `shareMessage` → "Share on WhatsApp"
  (`https://wa.me/962…?text=` + message), the share sheet, "copy".
- **Share again:** `POST /api/workers/{providerManagerId}/reset-temp-password` with `{ "tempPassword": null }` or no body →
  same fields (`providerManagerId, workerUserId, tempPassword, tempPasswordExpiresAt, shareMessage`). The old password
  stops working, the forced change comes back, devices are forgotten, sessions end.
- **Unlock** (after 5 wrong passwords): `POST /api/workers/{id}/unlock`.
- **Change the worker's phone:** `PATCH /api/workers/{id}/phone { "phone" }` → 409 `PHONE_IN_USE`; ends their sessions.

### 1.2 Worker list and card (owner)

`GET /api/workers?providerType=&providerId=` → **a list** (`[]` when none), oldest first:

```jsonc
[{ "providerManagerId": 13, "userId": 14118, "name": "…", "phone": "+962790000098", "email": null,
   "isActive": true,                       // the owner's switch
   "status": "active",                     // active | suspended | suspendedByPlan
   "loginStatus": "active",                // pendingFirstLogin | active | locked  → the badge on the card
   "firstLoginAt": "…", "mustChangePassword": false, "tempPasswordExpiresAt": null, "lockedUntil": null,
   "privileges": ["PointOfSale", "Attendance"],      // granted
   "effectivePrivileges": ["Attendance"],            // granted ∩ the station's plan — what works now
   "keepOnFree": false, "assignedAt": "…" }]
```

- `PUT /api/workers/{id}/privileges { "privileges": [...] }` → the row (with `effectivePrivileges`).
- `PATCH /api/workers/{id}/active?isActive=false|true` — suspend / resume (sessions end at once).
- `DELETE /api/workers/{id}` — remove (a driver's account is untouched; a wizard-created one can be re-added later).
- `PUT /api/workers/{id}/keep-on-free` — when the plan drops to one worker, this one stays (default: the oldest).
  `status: "suspendedByPlan"` rows come back by themselves on renewal.

**Limit:** 1 worker on the free plan, unlimited with the `workers` feature → `409 WORKER_LIMIT_REACHED` with a ready
message ("the free plan allows one worker — unlimited on Premium"). On iOS show the message without a buy button.

### 1.3 Worker login (new login tab "worker — by phone")

```http
POST /api/provider/worker-login
{ "phone": "0791234567", "password": "K7M4Q9TX", "deviceId": "<stable app-generated id>", "deviceToken": null }
```

- First login or a new device → `200 { "otpRequired": true, "sessionToken": "…", "phoneMasked": "079****567", "expiresAt": "…", "channel": "sms", "login": null }`.
  The OTP was **already sent**; `POST /api/provider/send-otp { sessionToken }` resends. Then
  `POST /api/provider/verify-otp { sessionToken, otpCode }` → the usual login result **plus** `mustChangePassword` and
  `deviceToken`. **Store `deviceToken` in secure storage** and send it on later logins → no OTP.
- Same device → `200 { "otpRequired": false, "login": { …tokens…, "mustChangePassword": false, "deviceToken": "…" } }`.
- Errors: `401 INVALID_CREDENTIALS` (also for unknown phones) · `423 LOCKED` (`until`, `retryAfterMinutes`) ·
  `410 TEMP_PASSWORD_EXPIRED` → "ask the owner to share again" · `403 WORKER_INACTIVE` → "the owner suspended your account" / "the plan does not allow your login".
- **Forced change:** while `mustChangePassword` is true show the "choose a new password" screen with no back button;
  the server refuses everything else with `403 PASSWORD_CHANGE_REQUIRED`. `PATCH /api/users/change-my-password
  { currentPassword, newPassword }` — 8–64 characters, not the phone, not the temp one (400 with a ready message).
  This device keeps its session.
- **Forgot:** `POST /api/provider/worker-login/forgot { phone }` → always 200 → `POST /api/provider/worker-login/reset
  { phone, otp, newPassword }`. Devices are forgotten: the next login needs an OTP.
- **Existing workers** log in here with the password they already have (no temp password, no forced change).
- **Owners** keep the email login exactly as today.

### 1.4 `GET /api/workers/me` — the worker's home (poll every minute, as the app does today)

```jsonc
{ "providerManagerId": 13, "providerType": "ChargingPoint", "providerId": 210, "providerName": "محطة الرشدان",
  "status": "active",                           // active | suspended | suspendedByPlan
  "privileges": ["PointOfSale", "Attendance"],  // EFFECTIVE — switch the tabs on this
  "lockedPrivileges": ["ConnectView"],          // granted but not in the plan
  "mustChangePassword": false }
```

`404 NOT_A_WORKER` for a non-worker. A worker never needs `features[]` (it answers 403 for workers).

**Tabs by privilege:** `Attendance` → "My shift" (first tab) and "My leave"; `PointOfSale` → transactions; `ViewWallet` → wallet;
`ConnectView` → Connect; `ViewAttendance` → "who is in" + attendance table; `ApproveLeave` → leave approvals;
`ManageAttendance` → "start / end their shift" buttons on who-is-in; `BiometricLogin` → the fingerprint option.

### 1.5 Biometric login (§3.4) — mobile only

Nothing is stored on the server. The fingerprint / face unlocks the stored refresh token; `refresh-access` does the
rest. Offer "enable fingerprint?" after the first successful login **only when** `BiometricLogin` is in
`workers/me.privileges` (owner: `biometrics` in `features[]`). On `refresh-access` 401 → disable the feature and show
the normal login. Refresh tokens live 30 days with sliding renewal; trusted devices 90 days.

### 1.6 Pushes (partner app)

| `data.type` / inbox type | To | Opens |
|---|---|---|
| `worker_first_login` | owner | the worker card (`providerManagerId`) |
| `billing_subscription` / `billing_bundle` | owner | "My plan" (unchanged) |

---

## 2. Shifts, zones, punching (partner app)

### 2.1 Shifts (owner)

`GET /api/workers/{id}/shifts` (owner, admin, or the worker) →

```jsonc
{ "providerManagerId": 13,
  "current":  { "id": 5, "effectiveFrom": "2026-11-01", "effectiveTo": null, "graceMinutes": 10, "earliestPunchMinutes": 60,
                "days": [ { "dayOfWeek": 0, "startTime": "06:00", "endTime": "14:00", "off": false }, …, { "dayOfWeek": 5, "startTime": null, "endTime": null, "off": true } ] },
  "upcoming": null,                                       // a scheduled change, if any
  "today":    { "workDate": "2026-11-07", "startAt": "2026-11-07T06:00:00", "endAt": "2026-11-07T14:00:00", "text": "06:00 – 14:00", "crossesMidnight": false } }
```

`PUT /api/workers/{id}/shifts { "effectiveFrom": "2026-11-15", "days": [...], "graceMinutes": 10, "earliestPunchMinutes": 60 }`.
`dayOfWeek` 0 = Sunday … 6 = Saturday; `endTime ≤ startTime` = crosses midnight (22:00–06:00). Missing days = off.
Default `effectiveFrom`: today for a worker with no schedule, otherwise next Sunday; a past date → 400. Templates
(morning / evening / night, "same for every day") are app-side only.

### 2.2 Public holidays

`GET /api/public-holidays?year=` (anyone) → `[{ id, date, nameAr, nameEn, name }]`. Admin sets them with
`PUT /api/admin/public-holidays { year, days: [{ date, nameAr, nameEn }] }`.

### 2.3 Punch zones (owner; map editor in the app)

- `GET /api/punch-zones?providerType=&providerId=` → `[{ id, name, lat, lng, radiusMeters, isActive, workers: [{ providerManagerId, name }], appliesToNobody, warning }]`
  — `appliesToNobody` = show "disabled"; `warning` = the ready text for < 120 m / > 400 m.
- `POST /api/punch-zones { providerType, providerId, name, lat, lng, radiusMeters (50–800), workerIds? }`,
  `PUT /api/punch-zones/{id}` (same body), `DELETE /api/punch-zones/{id}`, `PUT /api/punch-zones/{id}/workers { workerIds }`.
- **"Try from where you stand":** `POST /api/punch-zones/test { providerType, providerId, lat, lng, accuracyMeters, isMocked? }` →
  `{ zoneName, distanceMeters, radiusMeters, wouldBe: "accepted" | "review" | "rejected", reason, message }`. Nothing is recorded.
- **Attempts list:** `GET /api/punch-zones/attempts?providerType=&providerId=&from=&to=&verdict=rejected|review` →
  `[{ punchId, workerName, direction, at, verdict, reason, distanceMeters, accuracyMeters, isMocked, zoneName, deviceId }]`.

### 2.4 Punch in / out (worker with `Attendance`)

**Before calling:** location permission (explain, then request), GPS on, **one** high-accuracy fix with a 10 s timeout,
no background tracking, needs internet (no offline queue — the time must be the server's). Also read `isMocked` from
the location API and send it.

```http
POST /api/attendance/punch
{ "providerType": "ChargingPoint", "providerId": 210, "direction": "in",
  "lat": 32.5554, "lng": 35.8605, "accuracyMeters": 12, "isMocked": false, "deviceId": "<same id as login>" }
```

`200`:

```jsonc
{ "punchId": 1, "direction": "in", "at": "2026-11-07T11:39:20",
  "verdict": "accepted",              // accepted | review (recorded, awaiting the owner)
  "reason": null,                     // review: mocked | borderline; accepted outside any zone: noZone
  "zoneName": "Station yard", "distanceMeters": 20,
  "lateMinutes": 31, "state": "in", "workedMinutes": 0,
  "openFromYesterday": false,         // true = the last shift had no "out" — show "tell the owner" (case 12); the punch still worked
  "noZone": false,
  "message": "✔ سجّلت حضورك 11:39 ص · متأخر 31 د" }   // ready to show
```

| HTTP | `code` | Extra | Show |
|---|---|---|---|
| 422 | `OUTSIDE_ZONE` | `distanceMeters`, `zoneName`, `radiusMeters` | `detail` ("you are 410 m from …") |
| 422 | `LOW_ACCURACY` | `accuracyMeters` | "weak signal — move to an open area" |
| 422 | `TOO_EARLY` | `shiftStart`, `earliestAt` | "your shift starts 6:00 — you can punch in from 5:00" |
| 409 | `ALREADY_IN` | `since` | "you are punched in since 5:58" |
| 409 | `NOT_IN` | | |
| 403 | `WORKER_INACTIVE` / `PRIVILEGE_MISSING` / `FEATURE_NOT_IN_PLAN` | | sign out / hide the tab |

Refused attempts are recorded and the owner is pushed. Several in/out per day are allowed (after an "out" the button
is "punch in" again). **Consent text on the first punch** (§8): "we read your location only at the moment of punching".

### 2.5 "My shift" screen — `GET /api/attendance/today?providerType=&providerId=`

```jsonc
{ "state": "in",                                   // in | out | finished | leave | off
  "workDate": "2026-11-07", "punchedInAt": "…", "punchedOutAt": null,
  "shift": { "workDate": "…", "startAt": "…", "endAt": "…", "text": "06:00 – 14:00", "crossesMidnight": false },   // null = no shift today
  "lateMinutes": 31, "workedMinutes": 125,         // live while in
  "openFromYesterday": false, "hasZone": true, "isHoliday": false,
  "earliestAt": null,                              // set = too early; show the hint and disable the button
  "canPunchIn": false, "canPunchOut": true,
  "hint": "موجود من 11:39 ص · 2 س 5 د",            // ready text for the status line
  "last7Days": [{ "workDate": "…", "status": "present", "flags": "late", "minutesWorked": 480, "lateMinutes": 0, "firstInAt": "…", "lastOutAt": "…" }] }
```

Poll it when the screen opens and after every punch. `state: "leave"` → ask "you are on leave today — record work
anyway?" before punching (D11).

### 2.6 "Who is in now" — `GET /api/attendance/who-is-in?providerType=&providerId=` (owner, `ViewAttendance`)

```jsonc
[{ "providerManagerId": 13, "name": "ليث عبيدات",
   "state": "late",                 // in | late | notPunched | beforeShift | out | leave | off | holiday
   "since": "2026-11-07T06:22:00", "lateMinutes": 22, "zoneName": "Station yard",
   "shiftText": "06:00 – 14:00", "shiftStartAt": "…", "workedMinutes": 125, "review": false }]
```

Colours: `in` green · `late` orange · `notPunched` red · `leave` / `off` / `holiday` grey · `beforeShift` neutral.
`review: true` → a small "needs a look" mark.

**Manual start / end from this list** (owner, or `ManageAttendance` — never for oneself):
`POST /api/attendance/manual { providerType, providerId, workerId, direction, note }` → `{ punchId, at, direction, state, workedMinutes, message }`.
The note is mandatory (`422 NOTE_REQUIRED`); `403 CANNOT_RECORD_SELF`; `409 ALREADY_IN | NOT_IN`.

### 2.7 Pushes

| type | To | Opens |
|---|---|---|
| `attendance_attempt` | owner + `ViewAttendance` | attempts list (`punchId`) |
| `attendance_reminder` (`kind: in | out`) | worker | My shift |
| `attendance_missing` (`count`) | owner + `ViewAttendance` | who is in |
| `attendance_manual` | owner | the worker's day |
| `attendance_correction` (`attendanceDayId`, `workDate`) | worker | My attendance log |
| `attendance_summary` | owner (21:00) | attendance table |

All carry `providerType`, `providerId`, and `providerManagerId` where it applies.

---

## 3. Attendance table, corrections, export (owner / `ViewAttendance`; worker's own log)

### 3.1 Table — `GET /api/attendance?providerType=&providerId=&workerId=&from=&to=&status=`

Default this month, max 92 days; `status` filter `present | missingOut | absent | leave | holiday | off`.

```jsonc
{ "from": "2026-11-01", "to": "2026-11-07",
  "days": [{ "id": 2,                      // null = a day worked out from the shift (nothing recorded)
             "providerManagerId": 13, "workerName": "…", "workDate": "2026-11-07",
             "status": "present",          // present | missingOut | absent | leave | holiday | off
             "flags": ["late", "corrected"],   // late, review, noZone, extra, manual, corrected
             "firstInAt": "…", "lastOutAt": "…", "minutesWorked": 480, "lateMinutes": 21, "extraMinutes": 0,
             "shiftText": "06:00 – 14:00", "zoneName": "Station yard",
             "method": "app",              // app | manual | correction | — ("طريقة التسجيل")
             "recordedByName": null, "isOpen": false, "punches": 2 }],
  "summary": [{ "providerManagerId": 13, "workerName": "…", "daysPresent": 20, "minutesWorked": 9600, "lateMinutes": 45, "daysLate": 3,
                "daysAbsent": 1, "daysLeave": 2, "daysMissingOut": 1, "extraMinutes": 120, "daysReview": 1 }] }
```

Badges per row from `flags` and `method`: "manual — recorded by X", "corrected", "review". `missingOut` = "no punch-out" (the owner fixes it).

- **Export:** `GET /api/attendance/export?…same…` → CSV (UTF-8 with BOM) — open with the share sheet / save.
- **Worker's own log:** `GET /api/attendance/my?providerType=&providerId=&month=YYYY-MM` → same shape, own rows only.
- **Day detail:** `GET /api/attendance/days/{id}/history` → `{ day, punches: [{ direction, at, verdict, reason, source, zoneName, distanceMeters, accuracyMeters, isMocked, recordedByName, note }], corrections: [{ field, fromValue, toValue, note, correctedByName, correctedAt }] }`.

### 3.2 Corrections (owner only)

- `PATCH /api/attendance/days/{id} { "inAt": "2026-11-07T06:00:00", "outAt": "…", "status": "present", "confirmReview": true, "note": "…" }` — any subset, note mandatory
  (`422 NOTE_REQUIRED`); times are Jordan time; `absent` / `off` cannot be set on a day with punches (400). Returns the row.
- `POST /api/attendance/days { providerType, providerId, workerId, workDate, inAt?, outAt?, status?, note }` — a day with no row.
- Nothing is ever deleted; the history shows every change.

---

## 4. Leave (worker; owner / `ApproveLeave`)

### 4.1 Balance — `GET /api/leave/balance?providerType=&providerId=&workerId=&year=`

Worker: own (no `workerId`). Owner / `ApproveLeave`: pass `workerId`.

```jsonc
{ "providerManagerId": 13, "year": 2026,
  "annual": { "total": 14, "used": 3, "pending": 1, "remaining": 11 },
  "sick":   { "total": 14, "used": 0, "pending": 0, "remaining": 14 },
  "unpaidUsed": 2 }
```

Owner sets the totals: `PUT /api/workers/{id}/leave-settings { annualLeaveDays, sickLeaveDays }` (0–90).

### 4.2 Request flow (worker with `Attendance`)

1. **Preview while the form is filled:** `POST /api/leave/preview { providerType, providerId, kind, fromDate, toDate }` →
   `{ kind, daysCounted, remainingAfter, days: ["2026-11-12"] }` → "3 days will be deducted · 6 left after".
   Off days and public holidays are not counted.
2. **Send:** `POST /api/leave { providerType, providerId, kind: "paid" | "unpaid" | "sick", fromDate, toDate, reason? }` → the request (`status: "pending"`).
   Errors: `422 INSUFFICIENT_BALANCE` (`remaining`, `requested`, `pending`) · `409 OVERLAPS_EXISTING` · 400 for dates
   (paid / unpaid from today, sick up to 7 days back, max 60 days, one calendar year).
3. **Sick attachment (optional):** `POST /api/leave/{id}/attachment` multipart, key `file` (jpg / png / webp / pdf ≤ 5 MB).
4. **My requests:** `GET /api/leave?providerType=&providerId=&status=` →

```jsonc
[{ "id": 3, "providerManagerId": 13, "workerName": "…", "kind": "sick", "fromDate": "2026-11-04", "toDate": "2026-11-06", "daysCounted": 3,
   "reason": "flu", "hasAttachment": true, "status": "approved",       // pending | approved | rejected | cancelled
   "requestedByName": "…", "decidedByName": "…", "decidedAt": "…", "decisionNote": null, "createdAt": "…",
   "canApprove": false, "canCancel": false }]                           // what THIS caller may do
```

5. **Cancel:** `PATCH /api/leave/{id} { "status": "cancelled" }` — while pending, or approved before it starts
   (`422 LEAVE_ALREADY_STARTED` afterwards). The balance comes back.

### 4.3 Approvals (owner, or a worker with `ApproveLeave`)

- List: the same `GET /api/leave?…` returns everyone's requests, pending first; use `canApprove` / `canCancel` to show buttons.
- `PATCH /api/leave/{id} { "status": "approved" | "rejected", "note": "…" }` — `403 CANNOT_DECIDE_OWN`, `403 OWNER_ONLY`
  (a supervisor's own request), `422 LEAVE_NOT_PENDING`.
- The medical report: `GET /api/leave/{id}/attachment` — **owner and the worker only**; a supervisor gets 403 (show the
  kind and dates, no report button).
- **Owner records a leave directly:** `POST /api/leave/direct { providerType, providerId, workerId, kind, fromDate, toDate, reason? }` → approved at once.

### 4.4 Pushes

| type | To | Opens |
|---|---|---|
| `leave_request` (`leaveId`) | owner + `ApproveLeave` (a supervisor's own → owner only) | the request |
| `leave_decision` (`leaveId`, `status`) | worker | My leave |

---

## 5. Partner web (owner)

Same endpoints. Worker login by phone works on the web with the `X-Client-App: provider-web` header on `worker-login`
and `verify-otp` (trusted devices are per surface). Screens: workers list + wizard (Stepper) + success screen with
WhatsApp / copy / **print the worker card** (phone, temp password, QR to the login link); attendance table with the
filters, summary, CSV, corrections; leave approvals; zones list (name + workers only — the map editor is app-only in v1).

## 6. Admin (read-only + support)

`GET /api/admin/workers?providerType=&providerId=` (the list with login state), `POST /api/admin/workers/{id}/reset-temp-password`,
`…/unlock`, `PATCH …/phone`, public holidays. The owner endpoints also accept admins, so attendance / leave screens for
support can reuse them.

---

## 7. Checklists

**Partner app — worker**
- [ ] Login tab "worker — by phone"; OTP; `deviceToken` kept; forced-change gate; forgot password; `WORKER_INACTIVE`, `LOCKED`, `TEMP_PASSWORD_EXPIRED` texts.
- [ ] Biometric enable / use / disable rules (§3.4) gated by `BiometricLogin`.
- [ ] `workers/me` poll → tabs; 401 anywhere → sign out.
- [ ] Onboarding tour by privilege (local flag), "replay the tour" in settings (§3.2.4, no backend).
- [ ] My shift: state, hint, big button, location flow, all 14 cases of §3.7 from the codes above, `openFromYesterday`, leave-day question.
- [ ] My attendance log (`/attendance/my`), day detail.
- [ ] My leave: balance, preview, request, attachment, cancel; `leave_decision` push.
- [ ] Settings: biometrics, change password, reminders on/off (client-side until the server switches exist).

**Partner app — owner / supervisor**
- [ ] Workers list (list shape!), statuses, wizard (6 steps, local draft without the password), success + share, "share again", privileges with locked switches, suspend / resume / delete, keep-on-free when `suspendedByPlan` appears.
- [ ] Shift editor (templates, effectiveFrom), zones map editor + "try from where you stand", attempts list.
- [ ] Who is in (states, colours), manual start / end with note.
- [ ] Attendance table, summary, CSV share, corrections with note, day history.
- [ ] Leave approvals, direct leave, report viewer (owner only).
- [ ] Pushes → the right screens.
- [ ] iOS: no prices / buy buttons on the limit messages; new location-permission text; no "Always" keys.

---

## 8. Testing on dev

- Ask the backend team for the dev logins (owner of station 210, the test worker). Never use personal numbers.
- Station **210** (Rashdan) at 32.555402, 35.860557 — create a zone there; punch from inside (any point within the radius) and from 400 m away to see both answers. Set `accuracyMeters` > 150 to see `LOW_ACCURACY`; `isMocked: true` for the review case.
- Shifts: set today's shift to start a few minutes ago to test "late"; 2 h ahead for `TOO_EARLY`; 22:00–06:00 for a cross-midnight day.
- The job runs every 5 minutes on dev: reminders and the owner alert arrive within that window; a forgotten "out" closes 2 h after the shift end.
- Swagger on dev lists every endpoint with its summary.
