# Bug fix report — Oct 11, 2026 (Cable Connect / worker permissions)

> Reply to the four issues reported from testing on dev. All four were real.
> Code fixes: commit `859253f` on `main` (Cable-API). Verified locally against the dev database.
> **They reach dev only after the next publish** — see "Status" at the end.

---

## 1. Removing all of a worker's permissions gave the worker every permission

**Reported:** `PUT /api/workers/{id}/privileges` with `{"privileges": []}` stored `""`, and afterwards
`GET /api/workers` and `my-assets → access[]` returned all 9 privileges; `usesAllPrivileges` was `false`.
Tried on worker `providerManagerId = 9` (worker100), station 37.

**Cause:** the server read an empty stored value the same way as "never set". "Never set" means
*everything* (so workers created before permissions existed keep their access), so "nothing" became "everything".
The same rule was used by the API's own permission check, so the worker could also call the protected endpoints.

**Fix:**

| Stored value | Meaning |
|---|---|
| `null` (never saved) | every privilege — existing workers unchanged |
| `""` (owner switched everything off) | **no privilege** — station details only |
| list | exactly those keys |

- `usesAllPrivileges` is now `true` only for a never-restricted worker or one holding all 9 keys.
- The API checks (`ConnectView`, `ConnectControl`, `PointOfSale`) follow the same rule, so an empty list now gets **403**.
- Partial lists were already correct and are unchanged.

**Verified:** empty list → `privileges: []`, `usesAllPrivileges: false`, `access[].privileges: []`, Cable Connect → 403.
Saving `[]` → `[]`; one key → that key; all keys → 9 keys, `usesAllPrivileges: true`.

> Note: worker 9 still has the empty value you saved, so after the publish it will correctly have **no**
> permissions. Switch them back on from the owner's Workers screen if you need it for more tests.

---

## 2. Commands from dev never reached the charger ("CBL-SIM-37 is not connected to this server")

**Reported:** every command returned `NotConnected` while the admin showed the charger Online;
`webSocketBaseUrl` on dev returned `wss://ocpp.cable-app.com` (the production address) instead of ocpp-dev.

**Cause — configuration, not code:** the dev API was still set to send commands to the **AWS** OCPP server
(`ocpp.cable-app.com`), left over from the RH4 station visit on Oct 8. The simulator was connected to
**ocpp-dev**. AWS has no socket for that charger, so it answered "not connected". The admin showed Online
because both OCPP servers write the same dev database. The credentials sheet takes its address from the same
setting, hence the production URL.

**Fix:** the dev API's settings now point to `https://ocpp-dev.cable-app.com`. That fixes both the commands and
the `webSocketBaseUrl` / sheet address.

**Rule from now on:** dev API ↔ **ocpp-dev**, production API ↔ **AWS**. Chargers and simulators used with dev
connect to `wss://ocpp-dev.cable-app.com/ocpp16/`.

---

## 3. `NotConnected` came back with `accepted: true`

**Cause:** `accepted` was computed only from the charger's reply word. When the charger did not answer at all
(`NotConnected`, `Timeout`, `Disconnected`, `Unreachable`) the reply was empty and counted as a yes.
The same flag drives the driver app's start message, which would have said "The charger accepted".

**Fix:** `accepted` is now `true` only when `status = "Answered"` **and** the charger said yes
(`Accepted`, `Scheduled`, `Unlocked`, or a GetConfiguration reply).

**Verified:** a command to an unreachable charger returns `status: "Unreachable"`, `accepted: false`.

> Apps: keep checking `status` first, then `accepted` (the partner web and admin already do). Nothing else changes.

---

## 4. After the session ended, `costFils` / `costJod` / `price` were `null`

**Reported:** session #37 showed 0.092 JOD while charging, then `null` after it stopped.

**Cause:** pricing at the moment of the stop runs inside the OCPP server, and **ocpp-dev still runs the old build**
(up since Oct 9). It closed the session without a price; the background job priced it **5 minutes later**
(#37 is now stored as 92 fils). So the gap was temporary, but visible.

**Fix (two parts):**
- The session detail (`GET …/sessions/{id}/live`, used by the "Session finished" card and the session dialog)
  now **computes the price on the spot** when the session is closed but not priced yet.
- The background pricing job now runs **every minute** instead of every 10 minutes.
- With the new OCPP server build on ocpp-dev, the price is stored the moment the stop arrives, as on our local tests.

**Verified:** a closed session with its stored price removed still returns its price in the detail (159 fils), and
the job stores it again shortly after.

---

## Status

| Item | State |
|---|---|
| Code fixes 1, 3, 4 | ✅ done, pushed (`859253f`), verified against dev data |
| Config fix 2 | ✅ done in the dev API settings file (not in git — deployed with the API publish) |
| **Publish WebApi to dev** | ⏳ needed for 1, 2, 3 and 4 to be live on dev |
| **Publish Cable.Ocpp to ocpp-dev** (and AWS) | ⏳ needed for app starts (virtual card authorisation) and price-at-stop on dev |

## Retest on dev after the publish

1. Owner → Workers → switch everything off → Save. Worker: no Cable Connect menu item; a direct
   `GET /api/provider/charging-points/{id}/live` returns 403. `GET /api/workers` returns `privileges: []`, `usesAllPrivileges: false`.
2. Connect the simulator to `wss://ocpp-dev.cable-app.com/ocpp16/` → Refresh / Start / Stop from the admin or the partner
   portal now reach it; the credentials sheet shows `ocpp-dev`.
3. Stop the simulator, then send a command → `status: "NotConnected"`, `accepted: false`.
4. Run a session to the end → the "Session finished" card and the sessions list show the price immediately.
