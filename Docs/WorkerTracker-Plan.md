# Worker tracker — implementation plan and API (backend)

Source: `WORKER_TRACKER_FEATURE.md` (2026-10-27, Arabic). This file records what we decided to change in it and the
API as it is actually built, step by step. Where this file and the original differ, **this file wins**.

Branch: `worker-tracker` (off `sprint5`). Dev only until the whole feature is tested.

---

## 0. Decisions that change the original document (2026-11-03)

| # | Original | Decision |
|---|---|---|
| D1 | §3.0 — split every phone into two accounts (driver / partner), migrate cars, points and sessions, force-update both apps | **No account split.** One `UserAccount` per phone keeps everything. A worker gets a **second credential** (`PartnerCredential`: partner-app password, temp password, forced change, lockout) that only the partner app's phone login reads. The driver password, email, Google login, cars and points are never touched. No data migration; only the partner app needs a forced update (for the worker list). |
| D2 | §3.0 / decision 3 — a worker can work at several stations | **One station per worker in v1.** A phone that is already a worker anywhere → `409 PHONE_ALREADY_A_WORKER`. The schema (`ProviderManager`) still allows several rows per user, so this is a rule we can relax later. No "add him here too?" step in the wizard. |
| D3 | §3.0 — a worker of another owner gets a link request and approves by OTP | **Deferred.** v1 answers `409 PHONE_ALREADY_A_WORKER` ("contact Cable support"). |
| D4 | — (not covered) | **An owner's phone cannot be added as a worker:** `409 OWNER_CANNOT_BE_WORKER`. Admin accounts: `409 ACCOUNT_NOT_ELIGIBLE`. |
| D5 | §3.2.3 — the OTP goes by WhatsApp first | **SMS only** for now (the only OTP channel the system has). The login answer says `channel: "sms"`. |
| D6 | §3.2.2 — the share message links to `cable-app.com/partner-app` | Links to **`https://cable-app.com/partner/login`** (setting `Workers.PartnerLoginUrl`). |
| D7 | §3.2.3 step 4 — the app shows the forced password-change screen | **Also enforced on the server:** a partner-app token of a worker who still holds the temporary password gets `403 PASSWORD_CHANGE_REQUIRED` on everything except change-password, refresh, logout and `/api/workers/me`. |
| D8 | §2 — privileges stored as `null` meant "everything, including keys added later" | **Always the explicit list.** New privileges (Attendance, BiometricLogin, …) are never switched on by themselves. Existing workers keep what they have today. |
| D9 | Roles | **Nothing changes the role.** Owners stay `Provider`, wizard-created workers are `Worker`, a driver who becomes a worker stays `User`. The partner app decides access from ownership and `ProviderManager` rows, never from `RoleId`; the Cable app ignores roles. |
| D10 | §3.3 — suspend / delete a worker ends their sessions | **Per assignment** (step 2): sessions end at once, the account — and the Cable app for a driver-worker — is untouched. |

Defaults kept from the document (all are `AppSetting` rows, optional): temp password **72 h** (`Workers.TempPasswordHours`), **5** wrong passwords → **15 min** lock (`Workers.MaxFailedLogins`, `Workers.LockMinutes`), trusted device **90 days** (`Workers.TrustedDeviceDays`), OTP rate limit = the existing OTP service's.

---

## 1. Step 1 — worker login (done, on the branch)

SQL: `Scripts/WorkerTracker_1_WorkerLogin.sql` (idempotent). Applied to dev 2026-11-03. Existing workers got a
`PartnerCredential` with their current password hash (decision 29: they log in by phone with the password they have).

### 1.1 Data

- `dbo.PartnerCredential` — `UserId` (unique), `PasswordHash`, `MustChangePassword`, `TempPasswordExpiresAt`,
  `FirstLoginAt` (null = waiting for the first login), `LastLoginAt`, `PasswordChangedAt`, `FailedLoginCount`,
  `LockedUntil`.
- `dbo.TrustedDevice` — `UserId`, `App` (provider | provider-web), `DeviceId`, `TokenHash` (SHA-256), `LastUsedAt`,
  `ExpiresAt`.
- NotificationType `worker_first_login`.

### 1.2 Owner — add a worker

`POST /api/workers` (owner of the provider)

```jsonc
{ "providerType": "ChargingPoint", "providerId": 223, "name": "ليث عبيدات", "phone": "0791234567",
  "email": null,                               // optional
  "privileges": ["PointOfSale", "ViewWallet"], // keys from GET /api/workers/privileges; [] = station details only
  "tempPassword": null }                       // optional: the owner's own (8+ chars); otherwise generated (8 readable chars)
```

`200`:

```jsonc
{ "workerUserId": 14153, "providerManagerId": 14,
  "tempPassword": "K7M4Q9TX",                  // shown ONCE — the server keeps only the hash
  "tempPasswordExpiresAt": "2026-11-06T15:46:03",
  "shareMessage": "أهلاً ليث 👋\nضافك …",      // the ready text of the share screen, in the request language
  "existingAccount": false }                   // true = the phone already had a Cable (driver) account; nothing of theirs changed
```

| HTTP | `code` | When |
|---|---|---|
| 409 | `WORKER_LIMIT_REACHED` | the provider already has a worker (step 2 makes this plan-based) |
| 409 | `PHONE_ALREADY_A_WORKER` | the phone is a worker at any provider (D2 / D3) |
| 409 | `OWNER_CANNOT_BE_WORKER` | the phone owns a station or service provider |
| 409 | `ACCOUNT_NOT_ELIGIBLE` | admin account, or a deactivated driver account |
| 400 | — | bad phone, unknown privilege key, email in use, temp password too short |

What happens to the phone's account:

| Phone belongs to | Result |
|---|---|
| nobody | new `UserAccount` (role Worker, no driver password) + credential + assignment |
| a driver | credential + assignment added to the existing account; password, email, cars, points untouched |

**Share screen:** `tempPassword`, `tempPasswordExpiresAt` and `shareMessage` are enough for "share on WhatsApp"
(`https://wa.me/962…?text=` + the message), the share sheet and "copy".

**Share again:** `POST /api/workers/{providerManagerId}/reset-temp-password` with `{ "tempPassword": null }` (or no
body) → the same shape as above (`providerManagerId, workerUserId, tempPassword, tempPasswordExpiresAt, shareMessage`).
The old password stops working, the forced change comes back, trusted devices are forgotten and the worker's partner
sessions end. Owner or admin (`/api/admin/workers/{id}/reset-temp-password`).

**Unlock:** `POST /api/workers/{providerManagerId}/unlock` (owner or admin, `/api/admin/workers/{id}/unlock`) clears
the 15-minute lock.

**Worker card:** `GET /api/workers?providerType=&providerId=` now also returns `firstLoginAt`, `mustChangePassword`,
`tempPasswordExpiresAt`, `lockedUntil` and `loginStatus` = `pendingFirstLogin | active | locked` (the badge).
`usesAllPrivileges` is false for every worker created from now on (D8).

### 1.3 Worker — login by phone

```http
POST /api/provider/worker-login
{ "phone": "0791234567", "password": "K7M4Q9TX", "deviceId": "<app-generated id>", "deviceToken": null }
```

- First login / new device → `200 { "otpRequired": true, "sessionToken": "…", "phoneMasked": "079****567",
  "expiresAt": "…", "channel": "sms", "login": null }`. The OTP is already sent; `/api/provider/send-otp` resends.
  Then `POST /api/provider/verify-otp { sessionToken, otpCode }` → the usual login result **plus**
  `mustChangePassword` and `deviceToken`. **Keep `deviceToken` on the device** (secure storage).
- Same device → send `deviceToken` → `200 { "otpRequired": false, "login": { …tokens…, "mustChangePassword": false, "deviceToken": "…" } }`.
- Partner web portal: send the header `X-Client-App: provider-web` as the owner login does.

| HTTP | `code` | When | Extra |
|---|---|---|---|
| 401 | `INVALID_CREDENTIALS` | wrong password — **and** unknown phone, not a worker (no enumeration) | |
| 423 | `LOCKED` | 5 wrong passwords | `until` (Jordan time), `retryAfterMinutes` |
| 410 | `TEMP_PASSWORD_EXPIRED` | the temp password is older than 72 h (checked after the password matched) | → "ask the owner to share again" |
| 400 | — | missing field, OTP rate limit | |

**Forced change:** while `mustChangePassword` is true every other call answers `403 PASSWORD_CHANGE_REQUIRED` (D7).
`PATCH /api/users/change-my-password { currentPassword, newPassword }` from the partner app changes the **partner**
password: 8–64 characters, not the phone number, not the current one. On success the forced change is cleared, this
device keeps its session, the other partner surface (web / mobile) is signed out.

**Forgot:** `POST /api/provider/worker-login/forgot { "phone" }` → `200 { message, phoneMasked }` (same answer for
unknown phones; the OTP goes only to workers) → `POST /api/provider/worker-login/reset { "phone", "otp", "newPassword" }`
→ `200`; `401` for a wrong / used OTP. All partner sessions end and trusted devices are forgotten.

**Owner push:** `worker_first_login` (partner app, inbox row) when the worker completes their first OTP login —
data `{ type, providerManagerId, providerType, providerId }`.

**Existing workers (today's 2 in production):** they log in with `worker-login` and their current password, no temp
password, no forced change. Their email login keeps working too.

### 1.4 Tested on dev (2026-11-03, local API against `db_ab1977_cable`)

Owner login · worker list with `loginStatus` · existing worker phone login → OTP → trusted device (no OTP the second
time) · 4 × wrong → 401, 5th → 423, locked with the right password, owner unlock · share again → old password refused,
devices forgotten, `mustChangePassword` → 403 gate → change refused for the phone number / the same temp / a wrong
current → change ok → gate open, same session · forgot → reset → devices forgotten · create: already-a-worker 409,
owner's phone 409, new worker (temp + Arabic / English share message), second worker 409 limit, `pendingFirstLogin`
→ first login → `active` + owner inbox row · the gate on the new worker · own password → app usable.
Test data removed afterwards.

---

## 2. Step 2 — several workers per station (done, on the branch)

SQL: `Scripts/WorkerTracker_2_Workers.sql` (idempotent). Applied to dev 2026-11-03: the one-worker index is gone,
`ProviderManager.SuspendedByPlanAt` / `KeepOnFree` added, existing workers' privileges written out explicitly, the free
plan has `complaints`.

### 2.1 Rules

- **Limit from the plan:** 1 worker when the station's plan has no `workers` feature (free), unlimited with it
  (premium and above). Same feature calculation as `GET /api/provider/subscription`.
- **Effective privileges = granted ∩ allowed by the plan** (mapping confirmed 2026-11-03):

  | Privilege | Needs | Lock reason |
  |---|---|---|
  | `ManageStation`, `ViewFeedback` | nothing | — |
  | `PointOfSale` | an active loyalty agreement or a usable scan bundle | `needsLoyalty` |
  | `ViewWallet`, `ManageOffers`, `SendAnnouncements`, `ViewStatistics`, `Attendance`, `ViewAttendance`, `ApproveLeave`, `ManageAttendance` | `workers` | `needsPremium` |
  | `ConnectView`, `ConnectControl` | `connect` | `needsConnect` |
  | `BiometricLogin` | `biometrics` | `needsPremium` |

  Enforced in the API (`ProviderAccessGuard`): a granted-but-locked privilege answers 403 "not in the station's plan".
  That is the whole backend of biometric login (§3.4) — the app shows the option when `BiometricLogin` is effective.
- **Privileges are always stored as the explicit list.** A new key is never on by itself. Keys: the 9 existing +
  `Attendance`, `BiometricLogin`, `ViewAttendance`, `ApproveLeave`, `ManageAttendance`.
- **Suspend / delete are per assignment** (D10 closed): suspending ends the worker's partner sessions at once (the
  next request answers 401, `worker-login` answers `403 WORKER_INACTIVE`); the account — and the Cable app for a
  driver — is untouched. Delete removes the assignment and the partner credential; a wizard-created account is
  deactivated and comes back if the phone is added again (`existingAccount: true`).
- **Plan drops to one worker (decision 38):** `WorkerPlanSync` (run by the `billing-watch` job every 10 min and after
  every owner action on workers) keeps the `KeepOnFree` worker — else the oldest active — and sets `SuspendedByPlanAt`
  on the rest (sessions ended, `worker-login` → `403 WORKER_INACTIVE`). When the plan allows them again they come
  back by themselves. Owner suspensions are never touched. Records are kept.

### 2.2 Endpoints

| Method | Path | Who | Notes |
|---|---|---|---|
| GET | `/api/workers?providerType=&providerId=` | owner, admin | **A list now** (`[]` when none), oldest first. Each row: the step-1 fields + `status` (`active` \| `suspended` \| `suspendedByPlan`), `privileges` (granted), `effectivePrivileges`, `suspendedByPlanAt`, `keepOnFree` |
| GET | `/api/workers/me` | worker | `{ providerManagerId, providerType, providerId, providerName, status, privileges (effective), lockedPrivileges, mustChangePassword }`; `404 NOT_A_WORKER` otherwise. The app's one-minute poll |
| GET | `/api/workers/privileges?providerType=&providerId=` | owner, admin | `[{ key, locked, reason }]` — the owner's switches. Without the query: the plain key list |
| GET | `/api/workers/check-phone?providerType=&providerId=&phone=` | owner | `{ canAdd, reason, message }`; reasons `WORKER_LIMIT_REACHED` \| `PHONE_ALREADY_A_WORKER` \| `OWNER_CANNOT_BE_WORKER` \| `ACCOUNT_NOT_ELIGIBLE` \| `INVALID_PHONE`. Never says whether the phone has a Cable account |
| POST | `/api/workers` | owner | as step 1; the limit is plan-based; the answer adds `lockedPrivileges` |
| PUT | `/api/workers/{id}/privileges` | owner | answer carries `effectivePrivileges` |
| PATCH | `/api/workers/{id}/active?isActive=` | owner | per assignment (above) |
| DELETE | `/api/workers/{id}` | owner | per assignment (above) |
| PUT | `/api/workers/{id}/keep-on-free` | owner | who stays on a one-worker plan; applies at once; returns the list. `409 WORKER_INACTIVE` on a suspended worker |
| PATCH | `/api/workers/{id}/phone` `{ phone }` | owner, admin (`/api/admin/workers/{id}/phone`) | `409 PHONE_IN_USE`; ends sessions; the next login's OTP goes to the new phone |
| GET | `/api/admin/workers?providerType=&providerId=` | admin | read-only list, same shape |

`GET /api/provider/my-assets` → `access[].privileges` is now the effective list, and plan-suspended assignments are
left out.

### 2.3 Tested on dev (2026-11-03)

38 checks: list shape · catalog with lock reasons · check-phone cases · 2nd and 3rd worker on a `connect` station ·
worker `me` and `my-assets` with effective privileges · a worker cannot list workers · suspend → next request 401 and
`worker-login` 403 `WORKER_INACTIVE`, account untouched · resume · change phone (409 in use; sessions end; login on
the new phone) · delete and re-add (same account back) · plan switched off: `keep-on-free` keeps one, the others
`suspendedByPlan` and cannot log in, adding refused · free station: effective = ManageStation + ViewFeedback, catalog
reasons, 2nd worker 409, a Connect endpoint 403 for the worker · plan back on: everyone active again.
Test data removed afterwards.

---

## 3. Step 3 — shifts, holidays, punch zones, punching, who is in (done, on the branch)

SQL: `Scripts/WorkerTracker_3_Attendance.sql` (idempotent; 7 new tables + NotificationType `attendance_attempt`).
Applied to dev 2026-11-07. Defaults (all `AppSetting` rows, optional): `Attendance.MaxAccuracyMeters` 150,
`Attendance.MinRadiusMeters` 50, `Attendance.MaxRadiusMeters` 800, `Attendance.DefaultGraceMinutes` 10,
`Attendance.DefaultEarliestPunchMinutes` 60, `Attendance.AutoCloseHours` 2.

### 3.1 Rules

- **Plan gate:** every attendance endpoint needs the station's plan to have `workers` → else `403 FEATURE_NOT_IN_PLAN`.
  A worker also needs the effective `Attendance` privilege (`403 PRIVILEGE_MISSING`); a suspended one gets `403 WORKER_INACTIVE`.
- **Time is the server's.** The app sends no time. All JSON dates are Jordan time; the work date is the Jordan date
  of the shift's start — a 22:00–06:00 shift belongs to the day it started, and the 06:00 "out" attaches to it.
- **Shifts:** a weekly schedule per worker (0 = Sunday … 6 = Saturday), versioned by `effectiveFrom`; past days never
  change. `endTime ≤ startTime` = crosses midnight. Grace 10 min, earliest punch 60 min before, per schedule.
- **Position verdict, in this order (first rule wins):** accuracy > 150 m → `rejected lowAccuracy` · no zone for
  the worker → accepted, flagged `noZone` (mocked → review) · distance − accuracy > radius → `rejected outsideZone`
  · `isMocked` → `review mocked` · distance > radius → `review borderline` · inside → `accepted`.
  Rejected attempts are recorded (no day) and the owner + `ViewAttendance` workers get a push (`attendance_attempt`);
  review punches are recorded on the day, flagged `review`, and pushed too. **No automatic penalty** — the owner decides.
- **Shift rules on "in":** before start − earliest → `422 TOO_EARLY` (`shiftStart`, `earliestAt`) · after start + grace
  → accepted with `lateMinutes` (from the shift start) and flag `late` · no shift / holiday → accepted, flag `extra`.
- **States:** `409 ALREADY_IN` (`since`) · `409 NOT_IN`. Several in/out per day: minutes add up; lateness is from the
  first "in". A forgotten "out" leaves the older day open; today's "in" is still accepted and the answer says
  `openFromYesterday` (step 4's job closes it as `missingOut`).
- **One lock per worker** (row lock on the assignment) — a double tap cannot punch twice.

### 3.2 Endpoints

| Method | Path | Who | Notes |
|---|---|---|---|
| GET | `/api/workers/{id}/shifts` | owner, admin, the worker | `{ current, upcoming, today }`; `today` = the shift window of today (null = off) |
| PUT | `/api/workers/{id}/shifts` | owner | `{ effectiveFrom?, days: [{ dayOfWeek, startTime "HH:mm", endTime, off }], graceMinutes?, earliestPunchMinutes? }`. Default `effectiveFrom`: today for a first schedule, else next Sunday; a past date → 400. Missing days = off |
| GET | `/api/public-holidays?year=` | signed in | `[{ id, date, nameAr, nameEn, name }]` |
| GET / PUT | `/api/admin/public-holidays` | admin | PUT `{ year, days: [{ date, nameAr, nameEn }] }` replaces the year |
| GET | `/api/punch-zones?providerType=&providerId=` | owner, admin | zones with `workers[]`, `appliesToNobody`, `warning` (< 120 m / > 400 m) |
| POST / PUT `/{id}` | `/api/punch-zones` | owner | `{ providerType, providerId, name, lat, lng, radiusMeters (50–800), workerIds?, isActive? }` |
| DELETE | `/api/punch-zones/{id}` | owner | soft |
| PUT | `/api/punch-zones/{id}/workers` | owner | `{ workerIds }` |
| POST | `/api/punch-zones/test` | owner | `{ providerType, providerId, lat, lng, accuracyMeters, isMocked? }` → `{ zoneName, distanceMeters, radiusMeters, wouldBe, reason, message }` — nothing recorded |
| GET | `/api/punch-zones/attempts?providerType=&providerId=&from=&to=&verdict=` | owner, admin, ViewAttendance | rejected + review punches, newest first (default 30 days) |
| POST | `/api/attendance/punch` | worker (`Attendance`) | `{ providerType, providerId, direction: "in" \| "out", lat, lng, accuracyMeters, isMocked, deviceId }` → `{ punchId, at, verdict, reason, zoneName, distanceMeters, lateMinutes, state, workedMinutes, openFromYesterday, noZone, message }`. 422 `OUTSIDE_ZONE` (`distanceMeters`, `zoneName`, `radiusMeters`) \| `LOW_ACCURACY` \| `TOO_EARLY`; 409 `ALREADY_IN` \| `NOT_IN` |
| GET | `/api/attendance/today?providerType=&providerId=` | worker | `{ state in \| out \| finished \| off, workDate, punchedInAt, punchedOutAt, shift, lateMinutes, workedMinutes (live), openFromYesterday, hasZone, isHoliday, earliestAt, canPunchIn, canPunchOut, hint, last7Days[] }` |
| GET | `/api/attendance/who-is-in?providerType=&providerId=` | owner, admin, ViewAttendance | per usable worker: `state in \| late \| notPunched \| beforeShift \| out \| off \| holiday`, `since`, `lateMinutes`, `zoneName`, `shiftText`, `workedMinutes`, `review` |

### 3.3 Tested on dev (2026-11-07, station 210)

39 checks: privilege gate · shifts (first schedule from today, past date 400, rewrite, worker can read own) · zone create
with the radius warning, radius 20 → 400, worker cannot list · zone test for all five verdicts · punch: 400 m →
`OUTSIDE_ZONE` with the distance, accuracy 200 → `LOW_ACCURACY`, both listed as rejected attempts · 2 h early →
`TOO_EARLY` with `earliestAt` · in 31 min late → accepted + `late` · `ALREADY_IN` · today/who-is-in show "late",
since, zone · borderline out → `review` + owner inbox push · `NOT_IN` · second in/out of the day · `finished` ·
holidays set by the admin, read by the worker, `isHoliday` on today, owner refused.

---

## 4. Step 4 — attendance table, manual entry, corrections, export, the job (done, on the branch)

SQL: `Scripts/WorkerTracker_4_Reports.sql` (idempotent; `AttendanceCorrection`, `AttendanceEvent`, 5 notification
types). Applied to dev 2026-11-07. Hangfire job `attendance-tick` every 5 minutes.

### 4.1 Rules

- **The table** (`GET /api/attendance`) is one row per worker per day: stored days, plus days worked out from the
  shifts when nothing was recorded — `holiday` (public holiday), `off` (no shift that day), `absent` (the shift ended
  with no punch; only for workers granted `Attendance`). Future days are not listed. Max 92 days per call; default this month.
- **Manual entry** (owner, or a worker with `ManageAttendance`): time = now, a note is mandatory (`422 NOTE_REQUIRED`),
  never for oneself (`403 CANNOT_RECORD_SELF`), `409 ALREADY_IN | NOT_IN`. The day is flagged `manual`, the row's
  `method` = `manual` and `recordedByName` says who; the owner is pushed when a supervisor did it (`attendance_manual`).
- **Corrections** (owner only): the first "in", the last "out" (added when missing), the status, or confirming a
  review punch. Note mandatory. Every change is a row in the history (field, from → to, note, who, when); the day is
  flagged `corrected`; totals are recomputed; the worker is pushed (`attendance_correction`). Nothing is deleted.
  `absent` / `off` cannot be set on a day with punches. Times in the body are Jordan time.
- **The job** (every 5 min, each push keyed in `AttendanceEvent` — never twice):
  - forgotten "out" → closed `missingOut` 2 h after the shift end (16 h after the "in" when there was no shift), no
    minutes added; the owner fixes it with a correction;
  - shift ended with no punch → an `absent` row (`holiday` on a public holiday);
  - worker reminders: +5 min after the shift start with no "in" (`attendance_reminder`, kind `in`), +15 min after the
    end while still open (kind `out`);
  - owner + `ViewAttendance` workers: +15 min after the start with no "in" (`attendance_missing`) — one push per
    station per tick listing every worker that crossed the threshold ("3 workers did not punch in");
  - owner daily summary at 21:00 Jordan (`attendance_summary`): present · late · absent;
  - raw coordinates older than 90 days are cleared (`Lat`/`Lng`), zone, distance and verdict stay.

### 4.2 Endpoints

| Method | Path | Who | Notes |
|---|---|---|---|
| GET | `/api/attendance?providerType=&providerId=&workerId=&from=&to=&status=` | owner, admin, ViewAttendance | `{ from, to, days: [row], summary: [perWorker] }`. Row: `id` (null for a worked-out day), `workerName`, `workDate`, `status`, `flags[]` (late, review, noZone, extra, manual, corrected), `firstInAt`, `lastOutAt`, `minutesWorked`, `lateMinutes`, `extraMinutes`, `shiftText`, `zoneName`, `method` (app \| manual \| correction \| —), `recordedByName`, `isOpen`, `punches`. Summary: `daysPresent`, `minutesWorked`, `lateMinutes`, `daysLate`, `daysAbsent`, `daysLeave`, `daysMissingOut`, `extraMinutes`, `daysReview` |
| GET | `/api/attendance/export?…same…` | same | CSV, UTF-8 with BOM: the days, a blank line, the summary |
| GET | `/api/attendance/my?providerType=&providerId=&month=YYYY-MM` | worker | the worker's own month, same shape |
| POST | `/api/attendance/manual` | owner, ManageAttendance | `{ providerType, providerId, workerId, direction, note }` → `{ punchId, at, direction, state, workedMinutes, message }` |
| POST | `/api/attendance/days` | owner | `{ providerType, providerId, workerId, workDate, inAt?, outAt?, status?, note }` — adds a day with no row, then applies the correction rules |
| PATCH | `/api/attendance/days/{id}` | owner | `{ inAt?, outAt?, status?, confirmReview?, note }` → the row |
| GET | `/api/attendance/days/{id}/history` | owner, admin, ViewAttendance, the worker | `{ day, punches: [{ direction, at, verdict, reason, source, zoneName, distanceMeters, accuracyMeters, isMocked, recordedByName, note }], corrections: [{ field, fromValue, toValue, note, correctedByName, correctedAt }] }` |

### 4.3 Tested on dev (2026-11-07, station 210, two workers)

30 checks: table with an open present day and no row for a shift still running · a worker without `ViewAttendance`
refused · **the job**: +5 min reminder to the worker, +15 min "did not punch in" to the owner, both arrived once ·
manual entry: note required, not for oneself, supervisor records B → owner pushed, `ALREADY_IN`, owner records the
out · table shows method manual + who · corrections: supervisor refused, note required, owner moves the "in" before the
shift → late 0, `corrected`, worker pushed · absent on a day with punches → 400 · history rows · owner adds a missing
day (8 h) · worker's month · CSV with BOM and the summary · status filter · **auto-close** of a forgotten out as
`missingOut` by the job, then the owner adds the out → present with the minutes.
Not exercised: the 21:00 summary push (time-of-day; the code path is the same as the others).

---

## 5. Next steps (in order)

| Step | Scope | Status |
|---|---|---|
| 5 | Leave: balances computed from approved leaves, requests, approvals, sick attachment (owner-only, 1-year delete), retroactive sick | |

Open before launch (not code): labour-law and data-protection review of the defaults; `cable-app.com/partner/login`
must exist on production; iOS location-permission text (mobile).
