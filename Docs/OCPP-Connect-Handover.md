# Cable Connect (OCPP 1.6J) — Handover & Test Guide

> For the engineer who will test on dev and then connect the first real charger (RH4).
> Status: phases 0, B1, B2 done and deployed to **dev**. Last updated 2026-10-03.
> Deeper docs: `Docs/OCPP-Connect-Implementation-Plan.md` (design), `BE_OCPP_16_REQUIREMENTS.md`
> (message samples, rules R1–R7), `Cable.Ocpp/README.md` (server), `Scripts/OcppConnect_Phase*.sql`.

---

## 1. What this feature is, in one paragraph

EV chargers speak a standard called **OCPP 1.6** (JSON over WebSocket). A charger opens **one
permanent connection to one server** and reports everything: "I booted", "plug 1 is charging",
"here's the meter reading", "a session ended", "I have a fault". That server is called the
**CSMS**. Cable Connect makes **Cable the CSMS** for stations that subscribe: the station's
chargers are pointed at `wss://ocpp.cable-app.com/ocpp16/`, Cable stores what they report, and
the admin (now), the partner app and the driver app (later) show it live.

**Phase 1 is read-only**: we receive and answer; we never send commands (no remote start/stop,
no reset, no payments). The one decision we do make is **who may charge** — see §5.

## 2. The pieces

```
Charger ──wss──► Cable.Ocpp (separate site, ocpp-dev.cable-app.com) ──► SQL (dev DB)
                                                                           ▲
Admin web ──REST──► WebApi (dev.cable-app.com)  /api/admin/ocpp/* ─────────┘
```

| Piece | Where | Notes |
|---|---|---|
| **`Cable.Ocpp`** | solution project, .NET 10, deployed to `ocpp-dev.cable-app.com` (SmarterASP site `anashawas-001-site3`) | the WebSocket server chargers connect to. Own app pool so API publishes never drop chargers. Runs as **Staging** → reads `appsettings.Staging.json` (git-ignored; dev DB + `Ocpp:StatusApiKey`) |
| **WebApi** | `dev.cable-app.com` | 14 admin endpoints under `/api/admin/ocpp`, 2 Hangfire jobs, the fault-notification job |
| **Admin UI** | `dev.cable-app.com/admin` → sidebar **كيبل كونكت**, and a **كيبل كونكت** tab on every station page | Cable-Admin repo `src/features/ocpp` |
| **`Cable.Ocpp.TestClient`** | solution project (Utilities) | a fake charger for testing — see §6 |
| **7 tables** | dev DB | `OcppChargePoint`, `OcppConnector`, `OcppTransaction`, `OcppMeterValue`, `OcppAuthorizedTag`, `OcppRawMessage`, `OcppProcessStart` |

### Endpoints of Cable.Ocpp

| URL | What |
|---|---|
| `wss://ocpp-dev.cable-app.com/ocpp16/{chargePointId}` | **the OCPP endpoint**. The charger is configured with `wss://ocpp-dev.cable-app.com/ocpp16/` and appends its own id |
| `https://ocpp-dev.cable-app.com/health` | `{ ok, uptimeSec, connected }` — the keep-alive target (SmarterASP scheduled task every 10 min) |
| `https://ocpp-dev.cable-app.com/status` | header `X-Api-Key: <Ocpp:StatusApiKey>` → connected chargers, what each last said, restart count |

## 3. The tables (what to open while a charger talks)

| Table | One row per | Overwritten or appended? |
|---|---|---|
| `OcppChargePoint` | registered charger | overwritten — identity + hardware facts from boot + live flags (`IsConnected`, `LastMessageAt`, `LastBootAt`…) |
| `OcppConnector` | plug of a charger (**connector 0 = the charger itself**, 1..n = plugs) | overwritten on every `StatusNotification` — current state only |
| `OcppTransaction` | charging session (`Id` **is** the OCPP transactionId) | one row per session; closed by `StopTransaction` |
| `OcppMeterValue` | reading inside a session (every 15 s on RH4) | appended; unique on (charger, transaction, `MeasuredAt`) so replays don't double |
| `OcppAuthorizedTag` | card / password allowed at a **station** | admin-managed |
| `OcppRawMessage` | every frame in/out + CONNECT/DISCONNECT/REFUSED | appended, purged after 30 days — **read this first when anything looks odd** |
| `OcppProcessStart` | start of the `Cable.Ocpp` process | appended — counts how often the shared host recycles us |

Handy queries:

```sql
-- the wire, newest first
SELECT TOP 100 Id, Direction, [Action], MessageType, MessageId, Payload, CreatedAt
FROM dbo.OcppRawMessage WHERE ChargePointId = 'RH4' ORDER BY Id DESC;

-- sessions
SELECT t.Id, c.ChargePointId, t.ConnectorId, t.IdTag, t.MeterStartWh, t.MeterStopWh, t.EnergyKwh,
       t.StartedAt, t.StoppedAt, t.StopReason, t.IsOpen, t.IsStale, t.IsOrphan, t.WasRejected
FROM dbo.OcppTransaction t JOIN dbo.OcppChargePoint c ON c.Id = t.OcppChargePointId ORDER BY t.Id DESC;

-- one session's curve
SELECT MeasuredAt, Context, EnergyWh, PowerW, CurrentA, VoltageV, SocPercent
FROM dbo.OcppMeterValue WHERE OcppTransactionId = 8 ORDER BY MeasuredAt;

-- how often does the host restart us
SELECT StartedAt, MachineName, EnvironmentName FROM dbo.OcppProcessStart ORDER BY Id DESC;
```

Column meanings: `*At` columns named after the charger (`StartedAt`, `StatusUpdatedAt`,
`MeasuredAt`) are **the charger's clock** from inside the message; `Received*`/`CreatedAt` are
ours. After an outage the charger replays its queue, so expect charger times minutes older than
ours. `EnergyWh` is the charger's cumulative meter (odometer); session energy is
`(MeterStopWh − MeterStartWh)/1000` = `EnergyKwh`.

## 4. Message flow (what happens on the wire)

```
charger → GET /ocpp16/RH4  (Upgrade: websocket, Sec-WebSocket-Protocol: ocpp1.6, Basic auth if set)
   server: id registered? enabled? not locked? password ok?  → 101 (else 404/403/429/401 + REFUSED row)
charger → BootNotification        ← Accepted, currentTime (UTC), interval=60
charger → StatusNotification ×n   ← {}          (connector rows)
charger → Heartbeat every 60 s    ← currentTime
driver taps a card:
charger → StatusNotification Preparing
charger → Authorize {idTag}       ← Accepted | Invalid | Blocked | Expired   (OcppAuthorizedTag)
charger → StartTransaction        ← transactionId (= OcppTransaction.Id), status
charger → StatusNotification Charging
charger → MeterValues every 15 s  ← {}          (OcppMeterValue rows)
charger → StopTransaction         ← Accepted    (EnergyKwh computed)
charger → StatusNotification Available
fault:
charger → StatusNotification Faulted, errorCode   ← {}  + push/inbox to owner & managers (de-dup 30 min)
```

Every frame in and out is in `OcppRawMessage`. Unknown actions get `NotImplemented` and the
socket stays open. `DataTransfer` (vendor-private) → `UnknownVendorId`.

**The seven rules** (from `BE_OCPP_16_REQUIREMENTS.md` §7, all implemented): R1 use the
charger's timestamp, never arrival · R2 a Boot never resets an open session · R3 no duplicate
samples · R4 match replies by messageId · R5 offline ≠ no session (connector rows untouched on
disconnect) · R6 accept a late Stop · R7 store both clocks.

## 5. Who may charge — the allowed-cards list (important for a live station)

When Cable is the server, **we** answer the charger's "may this card charge?". With no list we'd
have to say yes to everyone — free charging for any RFID card. So:

- Admin → station page → **كيبل كونكت** tab → **البطاقات المسموح بها** → add the station's cards /
  passwords (`idTag`, up to 20 chars, matched trimmed + case-insensitive).
- Not on the list → `Invalid` (the charger refuses; the attempt is still recorded with
  `WasRejected = 1`). Switch off → `Blocked`. Expiry passed → `Expired`.
- The list is **per station**: a card works on every charger of that station.

Server-wide switch `Ocpp:AuthorizeMode` in `appsettings.Staging.json`:

| Mode | Use |
|---|---|
| `List` | normal — the table decides (dev and prod) |
| `RejectAll` | **first connection test on a live station** — nobody can charge through us until the owner's cards are loaded |
| `AcceptAll` | simulator on a developer machine only. Never on a server. |

## 6. Testing on dev with the simulator

Everything below talks to the **deployed** server and the **dev DB**. Watch results in
`https://dev.cable-app.com/admin` → كيبل كونكت (auto-refreshes every 15 s), and in SQL.

Already registered on dev (station 223 *Cable Demo Station (Test)*): `CBL-TEST-001` and
`CBL-TEST-002` (no password), allowed card `CBLTEST0001`. Station 37 has `RH4-TEST`
(password-protected — rotate it from the UI to get a fresh one) with card `PILOT-CARD-1`.

```bash
cd D:\Cable\Cable
dotnet build Cable.Ocpp.TestClient

# 1. full session: boot → plug in → Authorize → Start → 3 MeterValues → Stop; stays online 2 min
dotnet run --project Cable.Ocpp.TestClient --no-build -- --url wss://ocpp-dev.cable-app.com/ocpp16/ --id CBL-TEST-001 --idle 0 --run 2 --plug
#    expect: Accepted, a new session with 1.500 kWh, charger Online then Reconnecting → Offline after it ends

# 2. outage replay: socket dropped mid-session, reboot, same samples resent → no duplicates
dotnet run --project Cable.Ocpp.TestClient --no-build -- --url wss://ocpp-dev.cable-app.com/ocpp16/ --id CBL-TEST-001 --idle 0 --run 1 --replay
#    expect: ONE session, 2.500 kWh, 6 meter rows although 8 MeterValues frames were sent

# 3. card not on the list
dotnet run --project Cable.Ocpp.TestClient --no-build -- --url wss://ocpp-dev.cable-app.com/ocpp16/ --id CBL-TEST-002 --tag NOTALLOWED --idle 0 --run 0 --plug
#    expect: idTagInfo Invalid, session row with the مرفوضة chip. Add the card in the UI, rerun → Accepted

# 4. fault (keeps connector 1 Faulted for 2 min so you see it; vary --fault-code to get a new push)
dotnet run --project Cable.Ocpp.TestClient --no-build -- --url wss://ocpp-dev.cable-app.com/ocpp16/ --id CBL-TEST-002 --idle 0 --run 1 --fault --fault-code HighTemperature --fault-info "Cabinet 71C" --fault-seconds 120
#    expect: red عطل chip, أعطال tile = 1, Hangfire job NotifyOcppFaultAsync, push + inbox for the station owner/managers

# 5. password-protected charger (register one with "يتطلب كلمة مرور" on, copy the sheet)
dotnet run --project Cable.Ocpp.TestClient --no-build -- --url wss://ocpp-dev.cable-app.com/ocpp16/ --id RH4-TEST --user RH4-TEST --password <from the sheet> --idle 0 --run 2 --plug
#    wrong/missing password → HTTP 401; 10 failures → locked 15 min

# 6. idle survival on shared hosting (25 min silent, then heartbeats must still be answered)
dotnet run --project Cable.Ocpp.TestClient --no-build -- --url wss://ocpp-dev.cable-app.com/ocpp16/ --id CBL-TEST-002 --idle 25 --run 3
```

Admin actions to try while a simulator is connected: **disable** the charger (socket closes
within 60 s, `PolicyViolation: charge point disabled`), re-enable, **new password**, **delete**
(refused while a session is open).

## 7. Admin UI cheat sheet

- **كيبل كونكت** (sidebar): fleet KPIs (online / reconnecting / offline / free plugs / faulted /
  charging now / kWh today), filters, table with an enable switch. Row → detail dialog: actions,
  today's totals, subscription chip, boot/connection info, connectors (inline kW), recent
  sessions, **message log** (the raw frames).
- **Station → كيبل كونكت tab**: that station's chargers, subscription state, **Add charger**
  (station prefilled), **allowed cards**.
- **Add charger** fields: station (search by name) · Charge Point ID (the name the charger
  introduces itself with — leave empty to generate `CBL-{station}-{nn}`, or type the unit's
  existing id to keep it) · display name · heartbeat (s) · require password (off for units on
  OCPP Security Profile 0 — RH4 is one). The **credentials sheet shows the password once**.
- States: **Online** = socket open and a message within 2.5× heartbeat · **Reconnecting** = gap
  under 2 min · **Offline**. A `Cable.Ocpp` restart shows as a brief Reconnecting on every charger.
- Subscription: entity type **OcppConnect** on the Subscriptions screen. Lapsed → apps hide the
  data; **the charger is never cut off** (cutting it off could stop cars).

## 8. Connecting the real charger (RH4) — step by step

**Before touching it** (answers from the site visit so far: OCPP 1.6 over `wss`, 2 DC connectors,
meter values every 15 s incl. SoC, currently on SafeerSoft, URL + port editable, drivers use
password/card, no password on the charger side — Security Profile None):

1. Still needed from the station/vendor: values of `LocalAuthorizeOffline`,
   `AllowOfflineTxForUnknownId`, `AuthorizationCacheEnabled` (so cars keep charging if we are
   down); connectivity (SIM/Ethernet, SIM not vendor-locked); warranty in writing; the vendor's
   OCPP guide/PICS; the station's **card list**.
2. Admin → the station → كيبل كونكت → **Add charger**: Charge Point ID **`RH4`** (keep its name so
   the technician changes only the URL), **require password OFF**. Add the station's cards.
3. For the **first trial**, set `Ocpp:AuthorizeMode = "RejectAll"` in `appsettings.Staging.json`
   on the server and restart the site → nobody can charge through Cable during the trial.
4. **After hours**, at the charger: write down the current `Central System URL` and `Port`
   (`wss://socket-chargerjo.safeersoft.com/ocpp47`, 4435). Set URL
   `wss://ocpp-dev.cable-app.com/ocpp16/` and port **443**. Reboot the charger.
5. Watch `/status` (or the admin dialog): `RH4` appears → vendor/model/firmware fill in →
   connectors 1 and 2 → heartbeats. `OcppRawMessage` shows the frames. If nothing arrives within
   2 minutes: check the raw log for a `REFUSED` row (wrong id → 404), the charger's own screen
   for a connection error (TLS/port), and `/health`.
6. Optional during the trial: plug a car, tap a card → expect `Invalid` (RejectAll) and a session
   row with `WasRejected = 1` — proves the full path without charging through us.
7. **Restore the SafeerSoft URL** and reboot. Review the raw log together: any `DataTransfer`,
   unknown actions, odd timestamps.
8. Go-live later: `AuthorizeMode = "List"`, cards loaded, offline keys verified, then switch the
   URL for good and watch for 7 days.

## 9. Operations & troubleshooting

| Symptom | Cause / what to do |
|---|---|
| `https://ocpp-dev.cable-app.com/health` → **503 Service Unavailable** | the IIS app pool is stopped (happens after rapid crashes, or sometimes after a publish). SmarterASP panel → the site → restart/recycle the application pool. Then check `logs\stdout_*.log` in the site folder if it dies again. |
| Charger connects then disconnects every ~minute | admin disabled it (`IsEnabled = 0`) — the touch check closes the socket; or the charger's `WebSocketPingInterval` is off and a firewall drops idle sockets. |
| Charger shows **Reconnecting** on every charger at once | `Cable.Ocpp` restarted (new row in `OcppProcessStart`). On shared hosting that's the host recycling the pool; chargers reconnect by themselves. |
| Everything **Offline** but `/health` is fine | the keep-alive task isn't set and the pool idled out → chargers dropped and are still reconnecting; or the charger's network. |
| `Authorize` → `Invalid` for a real card | card not in `OcppAuthorizedTag` for **that station**, or `AuthorizeMode = RejectAll`. |
| Duplicate-looking sessions | normal after a rejected card: the charger starts and stops within seconds (`WasRejected = 1`). |
| Push not received for a fault | same connector + same error within 30 min is de-duplicated; the owner/managers need a registered device; check the Hangfire job `NotifyOcppFaultAsync` on the API. |

Keep-alive: panel → Scheduled Tasks → `https://ocpp-dev.cable-app.com/health` every **10 min**
(SmarterASP idles sites after 20 quiet minutes; an open socket does not count as traffic).
Recycles cannot be disabled on the shared plan — count them in `OcppProcessStart`; if they are
frequent the paid service moves to a VPS (same binary, DNS change).

## 9b. The production host — AWS Lightsail (set up Oct 6, 2026)

`ocpp-dev` stays on SmarterASP for development. The paid service runs the **same binary** on a
small Windows VPS where the app pool is *Always Running* (no idle shutdown, no recycles):

| | |
|---|---|
| Instance | Lightsail **Cable-OCPP**, Windows Server 2025, 2 vCPU / 2 GB ($22/mo), region eu-north-1 Stockholm, AWS account "Cable" |
| Static IP / DNS | `13.61.45.26` → `ocpp.cable-app.com` (A record managed in the SmarterASP DNS panel) |
| Firewall | 80, 443 open to all; 8172 (Web Deploy) only from the office IP; RDP via the Lightsail browser client |
| IIS | site `ocpp` → `C:\inetpub\ocpp`, pool `ocpp` (AlwaysRunning, idle 0, no recycle, preload), Let's Encrypt cert via **win-acme** (auto-renews) |
| Publish | VS → `Cable.Ocpp/Properties/PublishProfiles/aws-ocpp - Web Deploy.pubxml` (Administrator password from Lightsail, not stored) |
| Environment | still **Staging** / dev DB. Switching to production = apply `Scripts/OcppConnect_Phase0.sql` + `Phase1.sql` to the prod DB, add `appsettings.Production.json`, set `EnvironmentName` to Production, republish |
| Check | `https://ocpp.cable-app.com/health`; `/status` shows `MachineName EC2AMAZ-…` in `OcppProcessStart` |
| Verified Oct 6, 2026 | silent socket (no frames, pings only) survived 25 min and then answered heartbeats on the same connection; a second charger got 28/28 heartbeats answered over 28 min; zero restarts in `OcppProcessStart`. No keep-alive task is needed on this host. DB round trip from the box ≈ 25 ms (Stockholm → Amsterdam); message handling 26 ms (Heartbeat) to ~200 ms (StatusNotification/Start) — far inside the chargers' 30 s timeout, so the location stays. |

Things that bit us on first publish (all fixed, keep in mind for a rebuild):

1. The .NET hosting bundle must be installed **after** IIS is enabled, otherwise the
   `AspNetCoreModuleV2` module is missing → every request is a bare **500**. Fix: re-run the
   bundle with `/repair`, then `net stop was /y; net start w3svc`.
2. IIS log showed `500 19 33` = the `handlers`/`modules` sections are locked. Fix:
   `appcmd unlock config -section:system.webServer/handlers` (and `/modules`, `/webSocket`), `iisreset`.
3. The `logs` folder is not published; create `C:\inetpub\ocpp\logs` and grant
   `IIS AppPool\ocpp` modify, or stdout logging silently does nothing.

Remote clients only ever see the generic 500 page — read `C:\inetpub\logs\LogFiles\W3SVC*\*.log`
(status + sub-status columns) or call `http://localhost/health` from the server to get the detail.

## 9c. Phase 2 — remote control (built Oct 7, 2026)

Server → charger commands. Flow: Admin button → `POST /api/admin/ocpp/charge-points/{id}/commands/…`
→ the API POSTs `{action, payload}` to `{OcppServer:Url}/commands/{chargePointId}` (header
`X-Api-Key` = Cable.Ocpp's `Ocpp:StatusApiKey`) → Cable.Ocpp sends the CALL on the live socket
and waits ≤ 30 s for the CALLRESULT (matched by uniqueId, R4) → one row in `OcppCommand`
(who, what, the unit's answer, duration) → result back to the browser as a toast.

| Endpoint (under `/charge-points/{id}/commands`) | OCPP | Unit answers |
|---|---|---|
| `POST trigger-message` `{requestedMessage, connectorId?}` | TriggerMessage | Accepted / Rejected / NotImplemented, then the requested message arrives |
| `POST reset` `{type: Soft | Hard}` | Reset | Accepted — the unit closes, reconnects and boots again (~1 min) |
| `POST unlock-connector` `{connectorId}` | UnlockConnector | Unlocked / UnlockFailed / NotSupported |
| `POST change-availability` `{connectorId, type: Operative | Inoperative}` | ChangeAvailability | Accepted / Scheduled (after the session) / Rejected; connector 0 = whole unit |
| `POST remote-stop` `{transactionId}` | RemoteStopTransaction | Accepted / Rejected; the unit then sends StopTransaction (reason Remote) which closes the session and confirms the command |
| `POST get-configuration` `{keys?}` | GetConfiguration | `configurationKey[] {key, readonly, value}` + `unknownKey[]` |
| `POST change-configuration` `{key, value}` | ChangeConfiguration | Accepted / Rejected / RebootRequired / NotSupported |
| `GET` (list) | — | the audit rows, newest first |

Result shape: `status` = transport outcome (Answered, CallError, NotConnected, Timeout,
Disconnected, Invalid, Unreachable), `resultStatus` = the unit's word, `accepted` = both OK.
Nothing is retried automatically — a Reset that timed out may still have happened.

Config: the API needs `OcppServer: { Url, ApiKey }` in its (git-ignored) appsettings —
Development → `http://localhost:5300`, Staging → `https://ocpp-dev.cable-app.com`,
Production → `https://ocpp.cable-app.com`. Table: `Scripts/OcppConnect_Phase2.sql`
(applied to dev). Cable.Ocpp only accepts the 19 OCPP 1.6 central-system actions.

Admin: charger page → **Remote control** (refresh menu, soft / hard reset, unit and per-plug
out-of-service, unlock, command history) and **Charger settings** (read the keys, supported
profiles as chips, inline edit of writable keys). Buttons are disabled while the charger is
offline.

**Card list on the unit (SendLocalList).** After every card change (add / enable / disable /
remove) the API enqueues `SyncOcppLocalListAsync(stationId)`: the Hangfire server builds the
station's list (enabled, not expired, expiry carried along), sends `SendLocalList` **Full** with
`listVersion` = Unix seconds, then `ClearCache`, to every enabled charger of the station, and
records the outcome on the charger row: `LocalListStatus` Synced / Pending (unit offline — pushed
again when it boots; Cable.Ocpp enqueues on BootNotification) / Failed / NotSupported (never
retried), plus `LocalListVersion`, `LocalListSyncedAt`. Both pushes are audited in `OcppCommand`
with no user. Admin: "Cards on the unit" chip and "Sync cards now" on the charger page
(`POST …/commands/sync-local-list`). Cap: 100 entries (`OcppLimits.LocalListMaxEntries`).

**Credentials sheet (Oct 7).** The register / rotate responses and the charger detail now carry
`webSocketBaseUrl` (e.g. `wss://ocpp.cable-app.com/ocpp16/`) and `port` (443), both derived from the
API's `OcppServer:Url` — so the sheet never depends on the admin's own config. The port is shown
because RH4 has a separate port field that still says 4435 (SafeerSoft); the technician must set it
to 443 or the unit silently never connects. Charge Point ID is limited to 20 characters (OCPP
CiString20) and "require password" is off by default (Security Profile 0 units cannot send one).

**Owner maintenance (Oct 8).** Partner-app endpoints under `/api/provider/charging-points/{id}`:
`chargers/{ocppId}/commands/{reset | unlock-connector | change-availability | remote-stop |
trigger-message}` (same bodies as the admin ones), `chargers/{ocppId}/commands` (history),
`sessions?chargerId=&from=&to=&page=` (newest first, rejected / orphan rows excluded). The guard is
owner-or-active-manager of that station and the charger must belong to it; everything else
(audit row, confirmation, rate limit, duplicate protection, unlock-while-charging refusal) is the
shared `OcppCommandRunner`. Reliability windows now start at the charger's first CONNECT when
that is later than 30 days ago, so a new unit is not scored for weeks it was not with us.

**Reliability score, N-6 (Oct 8).** `ComputeOcppReliabilityAsync` (daily 03:30 UTC, or `POST
/api/admin/ocpp/reliability/recompute`) walks the last 30 days of the raw log per charger in
one-minute buckets: online from CONNECT / DISCONNECT rows (state before the window from the last
row before it), faulted from StatusNotification frames (Faulted until the next status of that
connector), and excludes the minutes after a DISCONNECT whose reason is "server shutting down"
(our restart, not the charger). Pct = good / counted minutes; also online %, fault-free %, offline
and fault incident counts. Station score = its worst charger. Drivers only ever get
`reliable: true` (≥ `OcppLimits.ReliableThresholdPct`, 95) or null.

**Station live picture for drivers, N-3 / N-4 / N-7 (Oct 8).** `GET /api/charging-points/{id}/live`
(anonymous, like the station page) returns, when the N-2 gates are open and at least one charger
is Online: `plugTypes[]` (per plug type: total / free / busy / outOfOrder / unknown / maxPowerKw),
`chargers[]` (ordinal, owner-given `displayName` or null, online, `plugs[]` with Free | Busy |
OutOfOrder | Unknown — Faulted and Unavailable both read OutOfOrder, offline chargers read
Unknown). Otherwise `available=false` with `unavailableReason` NoSubscription | NotShared |
Blocked | NoChargers | Offline. `GET /api/charging-points/live-summary?ids=1,2,3` is the badge
form (max 50). Partner: `GET /api/provider/charging-points/{id}/live` (no gates) and
`PUT …/chargers/{ocppId}/display-name`. The OCPP id is never in a driver response.

**Live-data visibility, N-2 (Oct 8).** Four gates between a station's live plug states and a
driver's screen: active OcppConnect subscription (admin) · `ChargingPoint.ShareLiveStatus`
(owner consent; set ON at the first activation unless `ShareLiveStatusSetAt` says the owner
already decided; never switched off by the system) · `!LiveStatusBlocked` (admin veto with a
reason the owner sees; it can hide what the owner shares, never share what the owner hid) ·
freshness per charger (`OcppLiveness`). `OcppLiveVisibility.GetAsync / IsVisibleAsync` is the
one place to ask. Endpoints: admin `GET /api/admin/ocpp/stations/{id}/live-visibility`,
`PUT …/block {blocked, reason}`, `PUT …/share {share}` (on the owner's request); partner app
`GET/PUT /api/provider/charging-points/{id}/live-visibility[/share]`. Backfill in the Phase 2
script turns sharing on for stations that already subscribe.

**Rate limit & duplicates (Oct 8).** The API refuses more than 10 commands to one charger per
minute (`OcppLimits.CommandsPerChargerPerMinute`) and refuses an identical Reset /
ChangeAvailability / UnlockConnector / RemoteStopTransaction while the previous one is still
unconfirmed and younger than 90 s. Cable.Ocpp applies the same per-minute cap on its own.

**Safety guards (Oct 7).** `ChangeConfiguration` refuses protected keys (central system URL,
identity, authorization key, security profile, APN…) and `UnlockConnector` refuses a plug that is
Charging — both in the API command and again inside Cable.Ocpp, so nothing holding the internal
key can bypass them.

**Commissioning state (Oct 7).** Until a charger's first BootNotification the API derives
`onboarding` from the raw log's system rows: Waiting (nothing reached us), Connected (socket
accepted, no boot yet), Refused (handshake rejected, with the HTTP status and reason: unknown
id, bad password, disabled, locked), Booted. The charger page shows it as a card polled every
4 s, the table as a chip instead of "Offline", and the register flow opens the new charger right
after the credentials sheet — so at the station you watch it connect instead of reading JSON.

**Command confirmation (Oct 7).** A CALLRESULT means "received". Cable.Ocpp stamps
`OcppCommand.CompletedAt` when the charger's own follow-up proves the effect: BootNotification
after Reset, StatusNotification with the requested state after ChangeAvailability /
UnlockConnector, the requested message after TriggerMessage (30-minute window, newest open
command of that action). The history shows "confirmed after N s" or "not confirmed yet".

**Alert rules.** Hangfire job `check-ocpp-alerts` every 5 min (`CheckOcppAlertsAsync`):
charger offline (socket gone, or open but silent) > 15 min, plug Faulted > 15 min, session open
> 6 h (thresholds in `OcppLimits`). First time a condition crosses the line it writes one open
`OcppAlert` row and sends push + inbox (type `charging_point_status_changed`, data
`type: ocpp_alert`) to the station owner, its active managers and every active admin; when the
condition clears the row gets `ResolvedAt` and offline / fault alerts send a "back" push. Admin:
Alerts panel on the Cable Connect screen (open by default, "show resolved" toggle, click → charger)
and an "Open alerts" tile. API: `GET /api/admin/ocpp/alerts?openOnly=&chargePointId=&take=`.
Table in `OcppConnect_Phase2.sql`.
Fourth rule (N-5): a plug in Finishing / SuspendedEV for 20 min = a car parked after charging.
If the last session's idTag is linked to a user in `OcppUserIdTag`, the driver gets the push
(user app) first and the station owner / managers 20 min later (`EscalatedAt`); if the card is
not linked, the station is told at once. Closes silently when the cable comes out. Admins are
not included in this one.

Simulator: answers all the commands while it runs (`--run N` keeps it alive): Reset really
drops the socket and reboots 4 s later; ChangeAvailability flips the plug to Unavailable and
keeps that across the reboot; GetConfiguration returns an RH4-like key set; SendLocalList /
GetLocalListVersion / ClearCache keep an in-memory card list and print it.

## 9d. Shared Hangfire storage — publish the API before relying on a new job

The dev database is also the Hangfire storage of **every** API instance pointed at it: the
deployed `dev.cable-app.com`, a developer's local run, a second local run that was not stopped.
All of them pull from the same queues and all run the recurring-job scheduler. Consequences:

- A job method that exists only in a newer build (e.g. a new `IBackgroundJobService` method)
  can be picked up by an older instance → `JobLoadException: … does not contain a method with
  signature …`, the recurring job gets an `Error` and its next run is pushed back; an enqueued
  job fails and retries. It works again once an instance with the new build takes it, but
  the timing becomes random.
- So: **publish the API to dev before counting on a new job there**, and when testing a new job
  locally expect the deployed instance to steal (and fail) some runs. `HangFire.Server` shows
  who is alive (`win6061…` = SmarterASP, your machine name = local).
- Production has only the production API, so this does not apply there — as long as nobody
  points a local run at the production database.

## 9e. Price alerts (not OCPP — PRICE_ALERTS_BE_SPEC, Oct 8)

Tariff source of truth: `TouTariff` (one active row, `Version` bumps on edit) + `TouTariffWindow`
(stable `Key`, minutes from midnight Asia/Amman, `EndMin` > 1440 crosses midnight, `PriceFils`,
names en/ar). `GET /api/pricing/tou` is public; `PUT /api/admin/pricing/tou` replaces the windows
as a new version (must cover 1440 min, unique keys). Preferences: `UserPriceAlert`
(`GET/PUT /api/users/me/price-alerts`: enabled, leadMinutes 15|30|45|60, window keys — unknown
key or other lead → 400). Job `send-price-alerts` every 5 min: looks back 10 min, one multicast
per language (device language stored with the push token — the app must send `language` on
`PUT /api/notification-token`; null = Arabic), FCM chunks of 500, quiet hours from AppSetting
`PriceAlerts.QuietFrom` / `QuietTo` (23:30–06:30; an alert inside is cancelled, never delayed),
`PriceAlertLog` unique on (user, window, date) so a late or repeated run never sends twice. Texts
per spec §5.2, prices filled from the tariff at send time. Dry run for any Jordan time:
`GET /api/admin/pricing/price-alerts/preview?at=2026-10-09T16:30`. Jordan has no DST (UTC+3
since 2022). Script: `Scripts/PriceAlerts_Phase1.sql`.

## 9f. Phase 3 — start without a card + session price (built Oct 9, 2026)

**Start.** `RemoteStartTransaction` is sent with a *virtual* idTag: `CBL-U{userId}` when the driver
app asks (`POST /api/users/me/ocpp-sessions/start`), `CBL-S{stationId}` when the partner app or the
admin asks (`…/commands/remote-start`). The unit (RH4 has `AuthorizeRemoteTxRequests = true`) sends
`Authorize` and then `StartTransaction` with that tag; `TagAuthorizer` accepts it **only** if an
Accepted `RemoteStartTransaction` with the same tag exists for that charger in the last 10 minutes
(`OcppVirtualTag.RemoteStartAuthorizeWindow`) — a card carrying that text is refused.
`StartTransactionHandler` sets `OcppTransaction.StartSource` (Card / App / Operator) and
`StartedByUserId` (the app user; the admin / owner who pressed the button; or, for a card, the user
it is linked to in `OcppUserIdTag`) and stamps the RemoteStart command `CompletedAt`.
Pre-flight (`OcppRemoteStart.EnsureCanStartAsync`): charger enabled + connected, plug **Preparing**
(a car is connected — an Available plug is refused with "Plug the cable into the car first", because
the unit would wait `ConnectionTimeOut` = 60 s and cancel) with NoError, no open session on the plug; drivers additionally need the station open to
drivers (N-2) and may hold one running session (`OcppLimits.OpenSessionsPerDriver`).
The driver's `OcppUserIdTag` row (`CBL-U…`, label "Cable app") is created on first use.

**Stop.** Drivers: `POST /api/users/me/ocpp-sessions/{id}/stop` — only sessions whose
`StartedByUserId` is the caller. Owners / admins: the existing remote-stop. Both bypass the admin
guard through `OcppCommandRunner.RunAsync(skipAccessGuard: true)`, which only permits
RemoteStart / RemoteStop.

**Price.** `SessionPricer` (Application/Pricing/SessionPricing.cs) walks the meter readings from
`MeterStartWh` to `MeterStopWh`, splits every stretch at the tariff-window boundaries (Jordan
wall-clock, windows may cross midnight) in proportion to time, and prices each window at its
fils/kWh. Whole fils; stored as `CostFils`, `TariffVersion`, `CostBreakdownJson` (`[{key, kwh,
priceFils, fils}]`), `PricedAt`. Done in `StopTransactionHandler` (never fails the reply) and by the
job `price-ocpp-sessions` every 10 min for anything left unpriced. Exposed as `costFils / costJod`
(+ `price[]` for drivers) on admin, owner and driver session DTOs. **Nothing is charged** — there is
no payment step yet.

**Test with the simulator**: `--remote 150 --charge-seconds 40` makes the client wait for a
RemoteStart, run the session with the tag it was given and stop after 40 s (or on RemoteStop).
Script used Oct 9: login a driver, `POST …/start {chargingPointId:223, chargerId:1, connectorId:1}`
→ `accepted`, `/current` shows 30 kW / SoC / kWh, `…/stop` → history shows `costFils 458` for
2.5 kWh off-peak at 183 fils.

**Visit findings fixed the same day**: `OcppWebSocketHandler` labels a peer drop `connection lost`
(only our own shutdown is `server shutting down`, which reliability excludes); `SyncOcppLocalListAsync`
asks the unit for `SendLocalListMaxLength` above `OcppLimits.LocalListProbeAbove` (20) and trims;
`OcppStopReason.Describe/DescribeAr` turns vendor reasons into text (`Other` = card stop on hjl).

## 10. What is NOT built yet

- Phase C — the real charger (above).
- Phase D — provider API (`/api/provider/charging-points/{id}/chargers`, sessions, meter curve,
  stats) and the partner-app screens.
- Phase E — "N of M plugs free" in the driver app.
- Phase 2 (contract change) — control: `Reset`, `UnlockConnector`, `ChangeAvailability`,
  `TriggerMessage`, `GetConfiguration`/`ChangeConfiguration` (set the offline keys ourselves).
- Phase 3 — **payments and reservation** (remote start / stop and the session price are built, §9f).
