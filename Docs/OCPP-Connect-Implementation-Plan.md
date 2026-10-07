# Cable Connect — OCPP 1.6J Implementation Plan

> Cable becomes the central system (CSMS) for subscribing stations' chargers and shows
> live charger data in the Partner app, the Admin web and the B2C app.
> Read-only in phase 1. Companion docs: `BE_OCPP_16_REQUIREMENTS.md` (message samples,
> rules R1–R7) and `OCPP_PROJECT_REPORT.md` (business context, open questions).
> Date: 2026-09-26

---

## 1. Goal and scope

**Product:** a paid per-station add-on ("Cable Connect"). A station points its chargers at
Cable's OCPP server; in return the owner gets live monitoring in the Partner app and drivers
see live plug availability in the B2C app.

| In scope (phase 1) | Out of scope (phase 1) |
|---|---|
| Receive + store everything the charger sends | Remote start / stop, reservations |
| Live connector status, live session, session history, faults, stats | Payment, tariffs, billing, RFID management |
| Fault push notification to the owner | Firmware / diagnostics |
| Admin: charger registration, credentials, health | Smart charging |
| B2C: "N of M plugs free" on station | OCPI (stations already on a commercial platform) |
| Subscription gating (`OcppConnect` entity type) | Any change to how drivers charge today |

**Multi-tenant from day one:** tenant = `ChargingPoint`. Nothing charger-related is shared
across stations. Onboarding a new station is data entry, not code.

---

## 2. Architecture

```
                 wss://ocpp.cable-app.com/ocpp16/{chargePointId}
 ┌──────────┐  one persistent WebSocket per charger   ┌──────────────────┐
 │ Charger  │ ───────────────────────────────────────►│   Cable.Ocpp     │  NEW project
 │ (any     │ ◄───────────────────────────────────────│  (own IIS site / │  own process
 │  brand)  │        replies only (phase 1)           │   own app pool)  │
 └──────────┘                                         └────────┬─────────┘
                                                               │ EF Core (same DbContext)
                                                               ▼
                                                    ┌────────────────────┐
                                                    │   SQL Server (DB)  │  5 new tables
                                                    └────────┬───────────┘
                                                             │ read only
      ┌──────────────┐   REST   ┌──────────────────┐         │
      │ Partner app  │ ◄──────► │  WebApi (existing)│ ◄──────┘
      │ Admin web    │          │  new routes only  │
      │ B2C app      │          └──────────────────┘
      └──────────────┘
```

Key decisions:

- **`Cable.Ocpp` is a separate deployable.** Publishing the API must never drop chargers.
  Same binary runs under IIS (shared plan, pilot) or as a service on a VPS (paid service).
- **No message queue, no cache layer.** Chargers → SQL directly; apps poll SQL through the
  existing API every 10 s on the live screen. Volume is tiny (≈1.5k rows/charger/day).
- **Timestamps are UTC everywhere.** `Cable.Ocpp` uses its own `JsonSerializerOptions`;
  the API's `JordanDateTimeJsonConverter` must never touch OCPP payloads
  (`BootNotification.currentTime` sets the charger's clock).
- **Existing building blocks reused:** `ProviderAccessGuard` (who may see a station),
  `AdminRoleGuard`, `Subscription` + `PaymentRecorder` (billing), `INotificationService`
  (push), Hangfire (jobs), password hasher (charger credentials), `Scripts/` (SQL deploys).

---

## 3. End-to-end workflows

### 3.1 Onboarding a station (admin)

```
Sales checklist passed (see §11)
  → Admin: Station → Cable Connect → "Add charger"
      system generates  ChargePointId  = CBL-{STATIONCODE}-{NN}   (≤20 chars, A-Z 0-9 -)
                        Password       = 24-char random, shown ONCE, stored hashed
  → Admin records the OcppConnect subscription (existing payments screen)
  → Technician types into the charger:
        Central System URL : wss://ocpp.cable-app.com/ocpp16/
        Charge Point ID    : CBL-IRB-01
        Username           : CBL-IRB-01
        Password           : ••••••••
      + offline safety keys (LocalAuthorizeOffline=true, AllowOfflineTxForUnknownId=true,
        WebSocketPingInterval=60, MeterValueSampleInterval=60, MeterValuesSampledData=…)
  → Charger reboots → connects → BootNotification
  → Cable.Ocpp fills vendor/model/firmware/serial, marks IsConnected
  → Admin sees the charger green with "last seen 5 s ago"
```

### 3.2 Charger connects (every time)

```
WS handshake  /ocpp16/{id}
  1. Sec-WebSocket-Protocol must contain "ocpp1.6"      else 400
  2. Basic auth: username == {id}, password verifies     else 401  (lockout after 10 failures / 15 min)
  3. OcppChargePoint with that ChargePointId exists, not deleted   else 404
  4. Accept socket, register in ConnectionRegistry (one live socket per id; a new socket
     replaces the old one — chargers reconnect after our recycles)
  5. Set IsConnected=true, ConnectedAt, LastMessageAt
Socket closed → IsConnected=false, DisconnectedAt   (status rows are NOT touched — R5)
```

### 3.3 Message loop (per socket)

```
frame in → RawMessage(in) → parse OCPP-J
  [2, id, action, payload]  CALL        → handler(action) → [3, id, result]   (or [4, id, error])
  [3, id, payload]          CALLRESULT  → match pending CS→CP request by id (phase 2)
  [4, id, code, desc, det]  CALLERROR   → log
unknown action              → [4, id, "NotImplemented", …]   never close the socket
malformed frame             → [4, id?, "FormationViolation", …]
every frame                 → LastMessageAt = now
```

Handlers (phase 1):

| Action | Writes | Reply |
|---|---|---|
| BootNotification | vendor, model, firmware, serial, iccid, imsi, meter serial, LastBootAt | `Accepted`, `currentTime` (UTC), `interval` = HeartbeatInterval |
| Heartbeat | LastMessageAt | `currentTime` |
| StatusNotification | upsert `OcppConnector` (connectorId 0 = the charger itself); auto-create unknown connectors; on `Faulted` → push to owner | `{}` |
| Authorize | nothing | `Accepted` (read-only mode) |
| StartTransaction | insert `OcppTransaction` (identity PK is the transactionId returned), IsOpen=true | `transactionId`, `Accepted` |
| MeterValues | one `OcppMeterValue` per sampled timestamp; measurand missing ⇒ Energy.Active.Import.Register; duplicate (chargePoint, transaction, measuredAt) ⇒ ignored (R3) | `{}` |
| StopTransaction | close the transaction: MeterStopWh, StoppedAt, Reason, EnergyKwh; unknown transactionId ⇒ orphan row (IsOrphan=true) | `Accepted` |
| DataTransfer | raw log only | `UnknownVendorId` |
| FirmwareStatusNotification, DiagnosticsStatusNotification | raw log only | `{}` |

### 3.4 Live view (partner app)

```
Partner app opens Station → Chargers
  GET /api/provider/charging-points/{id}/chargers          every 10 s while on screen
  ← per charger: isOnline (socket open), lastSeenAt, per connector: status, error,
     statusUpdatedAt, current session {energyKwh, powerKw, socPercent?, startedAt, durationSec}
Three display states:  online · reconnecting (socket closed < 2 min) · offline (≥ 2 min)
```

### 3.5 Session lifecycle

```
StartTransaction ──► row IsOpen=true, MeterStartWh
MeterValues ×N   ──► readings; live energy = latest EnergyWh − MeterStartWh
StopTransaction  ──► IsOpen=false, EnergyKwh = (MeterStopWh − MeterStartWh)/1000
no Stop for 24 h ──► Hangfire marks IsStale=true (still open, flagged for admin)
Stop after hours ──► accepted, closes normally (R6)
BootNotification mid-session ──► nothing happens to the session (R2)
```

### 3.6 Fault alert

```
StatusNotification status=Faulted
  → OcppConnector.Status=Faulted, ErrorCode, VendorErrorCode, Info
  → INotificationService → push to station owner + active managers
       "Charger CBL-IRB-01 connector 1: GroundFailure — Residual current detected"
  → NotificationInbox row (existing)
  → de-duplicated: same connector + same error code within 30 min = one notification
```

### 3.7 Cable.Ocpp is down / recycled

```
Charger loses socket → retries on its own (10–60 s)
  charger side: LocalAuthorizeOffline ⇒ cars keep charging; Start/Stop/MeterValues queued
  our side:     IsConnected=false, last known connector status kept with timestamp
Cable.Ocpp back → charger reconnects → queued messages arrive in a burst (0.1 s apart)
  → we use the charger's timestamps, never arrival time (R1)
  → duplicates dropped by the unique key (R3)
Partner app shows "reconnecting…" for gaps < 2 min, "offline since hh:mm" after
```

### 3.8 Subscription lapses

```
Subscription (EntityType=OcppConnect) expires (existing grace rules / kill switch)
  → Cable.Ocpp keeps answering the charger  (NEVER used as a billing lever — cars must charge)
  → API hides charger data from partner + B2C, returns `subscriptionInactive=true`
  → owner notified (existing subscription expiry job)
```

### 3.9 B2C live availability

```
GET nearest / station by id (existing DTOs)
  + liveAvailability: { connectedChargers, totalConnectors, freeConnectors, updatedAt } | null
  null when the station has no active OcppConnect subscription or no charger reported in 10 min
Map card / station page: "2 of 4 free"  ·  "live"
```

---

## 4. What gets added, by layer

### 4.1 Domain (`Domain/Enitites/Ocpp/`)

| Entity | Key columns |
|---|---|
| `OcppChargePoint : BaseAuditableEntity` | `ChargingPointId` FK, `ChargePointId` (unique, ≤20), `PasswordHash`, `DisplayName`, `Vendor`, `Model`, `FirmwareVersion`, `SerialNumber`, `ChargeBoxSerialNumber`, `Iccid`, `Imsi`, `MeterSerialNumber`, `HeartbeatInterval` (default 300), `IsConnected`, `ConnectedAt`, `DisconnectedAt`, `LastBootAt`, `LastMessageAt`, `FailedAuthCount`, `LockedUntil`, `IsEnabled` |
| `OcppConnector : BaseEntity` | `OcppChargePointId` FK, `ConnectorId` (0..n), `Status` (string, 9 OCPP values), `ErrorCode`, `VendorErrorCode`, `Info`, `StatusUpdatedAt` (charger time), `StatusReceivedAt`, `PlugTypeId?` (FK to existing PlugType), `PowerKw?`; unique (`OcppChargePointId`,`ConnectorId`) |
| `OcppTransaction : BaseEntity` | `Id` identity = the OCPP transactionId, `OcppChargePointId`, `ConnectorId`, `IdTag`, `MeterStartWh`, `MeterStopWh?`, `StartedAt`, `StoppedAt?`, `StopReason?`, `EnergyKwh?`, `IsOpen`, `IsStale`, `IsOrphan`, `ReceivedStartAt`, `ReceivedStopAt?`; index (`OcppChargePointId`,`StartedAt` desc) |
| `OcppMeterValue : BaseEntity` | `OcppChargePointId`, `ConnectorId`, `OcppTransactionId?`, `MeasuredAt`, `ReceivedAt`, `EnergyWh?`, `PowerW?`, `CurrentA?`, `VoltageV?`, `SocPercent?`, `TemperatureC?`; unique (`OcppChargePointId`,`OcppTransactionId`,`MeasuredAt`); index (`OcppTransactionId`,`MeasuredAt`) |
| `OcppRawMessage : BaseEntity` | `OcppChargePointId?`, `ChargePointId`, `Direction` (In/Out), `Action`, `MessageId`, `MessageType` (2/3/4), `Payload` (nvarchar(max)), `CreatedAt`; index (`ChargePointId`,`CreatedAt`); 30-day retention |

Constants: `SubscriptionEntityTypes.OcppConnect = "OcppConnect"`; `OcppConnectorStatus`
string constants; `OcppStopReason` (stored as string, not validated).

### 4.2 Infrastructure

- `Persistence/Configurations/Ocpp*Configuration.cs` — table names, indexes, unique keys.
- `IApplicationDbContext` — five new `DbSet`s.
- `Scripts/OcppConnect_Phase1.sql` — idempotent create (same style as `Analytics_Phase1_CreateTables.sql`),
  `SET QUOTED_IDENTIFIER ON`, dev first then prod.
- `Ocpp/OcppCredentialService` — generate id + password, hash/verify via the existing hasher.
- `IBackgroundJobService` additions: `MarkStaleOcppTransactionsAsync` (hourly),
  `PurgeOcppRawMessagesAsync` (daily, > 30 days), `RollupOcppDailyStatsAsync` (daily; kWh,
  sessions, faults, uptime per charger — feeds the stats endpoint cheaply).

### 4.3 `Cable.Ocpp` (new project, `Cable.Ocpp/`)

```
Cable.Ocpp/
  Program.cs                 Kestrel/IIS host, AddInfrastructure(config) for DbContext + hasher,
                             own JsonSerializerOptions (UTC, camelCase), /health, /status
  Transport/
    OcppWebSocketMiddleware  handshake checks (§3.2), accept, pump loop, close handling
    ConnectionRegistry       ChargePointId → live socket (singleton; replaces on reconnect)
    ChargerAuthenticator     Basic auth + lockout
  Protocol/
    OcppFrame                CALL / CALLRESULT / CALLERROR parse + serialize
    OcppErrorCodes           NotImplemented, FormationViolation, InternalError, …
  Handlers/                  one class per action (§3.3), IOcppHandler { Action; HandleAsync }
  Services/
    RawMessageLogger         every frame in/out → OcppRawMessage (fire-and-forget batch)
    FaultNotifier            Faulted → INotificationService (de-dup 30 min)
    ProcessLifetime          restart counter + uptime for /status (measures shared-host recycles)
  appsettings.{Environment}.json   connection string (git-ignored, like WebApi)
  web.config                 generated on publish (IIS in-process)
```

`/health` → 200 `{ ok, uptimeSec, connected }` — the target of the SmarterASP keep-alive task
(every 10 min). `/status` → same + restart count, last restart, connected charger ids
(protected by a static API key header, admin use only).

### 4.4 Application (CQRS, `Application/Ocpp/`)

Admin:

| Command / Query | Purpose |
|---|---|
| `RegisterOcppChargePointCommand` | create row, return id + one-time password |
| `RotateOcppChargePointPasswordCommand` | new password, invalidates the old |
| `SetOcppChargePointEnabledCommand` | disable = refuse handshake (station off-boarded) |
| `DeleteOcppChargePointCommand` | soft delete |
| `GetOcppChargePointsAdminRequest` | list with connection health, filters, paging |
| `GetOcppRawMessagesRequest` | last N frames for a charger (support tool) |
| `GetOcppFleetHealthRequest` | connected / disconnected / faulted counts for the admin dashboard |

Provider (guarded by `ProviderAccessGuard` + active `OcppConnect` subscription):

| Query | Purpose |
|---|---|
| `GetStationChargersRequest` | chargers + connectors + current session (live screen, 10 s poll) |
| `GetChargerSessionsRequest` | paged history (from/to/connector) |
| `GetSessionMeterValuesRequest` | curve for one session |
| `GetChargerStatsRequest` | day/week/month: kWh, sessions, avg kWh, faults, uptime %, peak hours |

B2C: `LiveAvailabilityDto` added to `GetChargingPointById` and nearest DTOs (null when not
subscribed / no data).

### 4.5 WebApi routes

```
/api/admin/ocpp/charge-points                 GET, POST
/api/admin/ocpp/charge-points/{id}            GET, DELETE
/api/admin/ocpp/charge-points/{id}/rotate-password    POST
/api/admin/ocpp/charge-points/{id}/enabled            PUT
/api/admin/ocpp/charge-points/{id}/raw-messages       GET
/api/admin/ocpp/fleet-health                          GET

/api/provider/charging-points/{id}/chargers                          GET   (live)
/api/provider/charging-points/{id}/chargers/{cpId}/sessions          GET
/api/provider/ocpp/sessions/{transactionId}/meter-values             GET
/api/provider/charging-points/{id}/chargers/{cpId}/stats?period=     GET
```

(`/api/partners` is the loyalty partner programme in this codebase — not used here.)

### 4.6 Admin web (Cable-Admin)

- Station page → new **Cable Connect** tab: chargers table (id, brand/model, firmware,
  connected badge, last seen, connectors with status chips), *Add charger* dialog showing the
  four values once with copy buttons, *Rotate password*, *Disable*, *Raw log* drawer.
- Dashboard: fleet health tile (connected / offline / faulted).
- Subscriptions screen: `OcppConnect` appears as a new entity type (no new screen).

### 4.7 Partner app (mobile team)

- Station → **Chargers** screen: per charger card, online/reconnecting/offline, connectors
  with status colours, live session with kW gauge, kWh, time, SoC (hidden when null).
- Session history list + session detail chart (meter values).
- Stats screen (day/week/month).
- Fault push notification → deep link to the charger.

### 4.8 B2C app

- Station card and station page: "N of M free · live" badge when `liveAvailability` is present.

### 4.9 Deployment

| Piece | Where | How |
|---|---|---|
| Tables | dev DB → prod DB | `Scripts/OcppConnect_Phase1.sql` |
| `Cable.Ocpp` | `ocpp-dev.cable-app.com` → `ocpp.cable-app.com` (own site + app pool, free SSL, keep-alive task every 10 min) | new Web Deploy profile |
| `WebApi` routes | existing Cable-API site | normal publish |
| Admin web | existing `/admin` | `Scripts/build-admin.ps1` |
| VPS (later) | when the first paying station signs / pilot shows too many recycles | same binary, DNS change |

---

## 5. Rules R1–R7 → where they live in code

| Rule | Implementation |
|---|---|
| R1 charger timestamp, never arrival | every handler stores both; all ordering/aggregation uses `MeasuredAt` / `StatusUpdatedAt` / `StartedAt` |
| R2 Boot does not reset sessions | `BootNotificationHandler` only updates charger metadata |
| R3 no duplicates | unique index on `OcppMeterValue` (chargePoint, transaction, measuredAt); insert catches the violation and ignores |
| R4 match by messageId | `OcppFrame` carries the id; replies echo it; pending CS→CP requests keyed by id (phase 2) |
| R5 offline ≠ no session | socket close touches only `IsConnected`; connector + open transaction rows stay |
| R6 late StopTransaction | `StopTransactionHandler` closes by transactionId regardless of age; unknown id → orphan row |
| R7 store both clocks, warn on drift | `ReceivedAt` next to every charger timestamp; drift > 5 min logged + shown in admin health |

Extra: meter rollback (negative energy) → stored, `EnergyKwh` set null, flagged; unknown
connectorId → auto-created; open > 24 h → `IsStale`.

---

## 6. Security

- Per-charger credentials, hashed with the existing hasher; username must equal the path id.
- TLS only in production (`wss://`); handshake rejects a missing `ocpp1.6` subprotocol.
- Lockout: 10 failed auths → 15 min; logged.
- `IsEnabled=false` refuses the handshake (off-boarding without deleting history).
- No driver PII: `idTag` stored as an opaque string, never looked up.
- Raw messages kept 30 days for forensics; `/status` behind an API key; no OCPP endpoint is
  reachable through the public API.
- Message size cap (64 KB) and per-socket rate limit (60 msg/min) against a misbehaving unit.

---

## 7. Phases, estimates, acceptance

| Phase | Scope | Est. | Done when |
|---|---|---|---|
| **0 — Smoke test** ✅ built 2026-09-30 | `Cable.Ocpp` skeleton: `/health`, `/status`, WS echo, restart counter; deployed to `ocpp-dev` | 1 day | A socket from outside survives > 20 min idle; restart count observed for a week |
| **B1 — Core CSMS** ✅ 2026-10-01 | tables (+ `OcppAuthorizedTag`), persisting handlers, Basic auth + lockout, fault job, stale/purge jobs, register + allowed-tag endpoints | 3 wk | Simulator scenarios `full`, `offline`, `fault`, `suspend`, two chargers all pass; energy = meterStop − meterStart; zero duplicates after offline replay |
| **B2 — Admin + subscription** | CQRS + routes + admin tab + `OcppConnect` entity type + jobs | 1.5 wk | Admin registers a charger, sees it connect; subscription lapse hides data but the charger keeps working |
| **C — First real charger** | pilot station, one brand | 1 wk + hardware wait | 7 consecutive days of readings from a real unit; offline keys verified via `GetConfiguration` |
| **D — Partner app** | live / history / stats / fault push | 2 wk (mobile) | Owner sees a real session live; fault push received |
| **E — B2C** | live availability badge | 1 wk | "N of M free" on the pilot station in the driver app |
| Later | `Reset`, `UnlockConnector`, `ChangeAvailability`, `TriggerMessage` (owner maintenance buttons); OCPI client; VPS move | — | contract change |

Total to a sellable v1: **≈ 8–9 weeks**, largely in parallel with the pilot's hardware timeline.

---

## 8. Open items (blocking) — answers expected from the client visit

- Manufacturer / model / firmware; OCPP 1.6J confirmed; `wss://` + Basic auth supported.
- Central System URL changeable by the station; SIM not vendor-restricted.
- Written warranty confirmation from the vendor.
- Which configuration keys are exposed (offline safety keys in particular).
- Connectivity type and who pays; one powered charger available for the first test.
- What the station expects from Cable (monitoring only vs. driver visibility vs. control).

Anything red in the first four lines stops the project before B1.
