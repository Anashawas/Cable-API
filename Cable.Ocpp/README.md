# Cable.Ocpp — OCPP 1.6J central system

Standalone .NET 10 WebSocket server that EV chargers connect to. Runs as its **own
IIS site / app pool** (`ocpp-dev.cable-app.com`, later `ocpp.cable-app.com`) so
publishing the main API never drops a charger. Full plan: `Docs/OCPP-Connect-Implementation-Plan.md`.

## Endpoints

| Path | Purpose |
|---|---|
| `WS /ocpp16/{chargePointId}` | the OCPP endpoint — chargers are configured with `wss://host/ocpp16/` and append their id |
| `GET /health` | `{ ok, uptimeSec, connected }` — target of the SmarterASP keep-alive task |
| `GET /status` | connected chargers, their last messages, restart count (header `X-Api-Key`) |

## What it does (phase B1)

- **Handshake**: the id must exist in `OcppChargePoint` (registered via `POST /api/admin/ocpp/charge-points`),
  be enabled and not locked. Chargers with a `PasswordHash` must send HTTP Basic (username = id);
  10 bad passwords → 15-minute lockout. `PasswordHash = NULL` admits by id alone (Security Profile 0).
- **Persists everything**: boot info → `OcppChargePoint`; connector states → `OcppConnector`;
  sessions → `OcppTransaction` (identity = the OCPP transactionId); samples → `OcppMeterValue`;
  every frame → `OcppRawMessage`; every process start → `OcppProcessStart`.
- **Authorize / StartTransaction** answer `Accepted` only for tags in `OcppAuthorizedTag` for that
  station (`Ocpp:AuthorizeMode = List`). `AcceptAll` is for the simulator; `RejectAll` for a
  connection test on a live station where nobody should charge through us.
- **Faulted** → enqueues `IBackgroundJobService.NotifyOcppFaultAsync` (push + inbox to the owner and
  active managers), executed by the API's Hangfire server; de-duplicated 30 min per connector+error.
- Rules R1–R7 from `BE_OCPP_16_REQUIREMENTS.md`: charger timestamps everywhere, Boot never resets a
  session, replayed samples are no-ops (unique index), late Stop closes the session, unknown Stop →
  orphan row, socket loss never touches connector rows.

## Run locally

```
dotnet run --project Cable.Ocpp
dotnet run --project Cable.Ocpp.TestClient -- --url ws://localhost:5300/ocpp16/ --id CBL-TEST-001 --idle 0 --run 1 --replay
```

`appsettings.Development.json` (git-ignored) holds `ConnectionStrings:Cable`, `Database` and
`Ocpp:StatusApiKey`. Dev has two simulator chargers (`CBL-TEST-001/002`, station 223) and the
allowed tag `CBLTEST0001`.

## Test client scenarios

| Flag | Proves |
|---|---|
| `--plug` | full session: Authorize → Start → 3× MeterValues → Stop; `EnergyKwh = 1.500` |
| `--replay` | socket dropped mid-session, reboot on reconnect, samples 1–3 resent with identical timestamps → **no duplicates**, `EnergyKwh = 2.500`, 6 meter rows |
| `--fault` | `Faulted` on connector 1 → Hangfire job enqueued; recovery to `Available` |
| `--tag X` | a tag not on the station's list → `Invalid`, `WasRejected = 1` |
| `--idle 25` | the socket survives the shared host's 20-minute idle rule |
| `--user/--password` | Basic auth against a charger registered with `requirePassword = true` |

## Deploy to SmarterASP (dev)

1. Websites → `+ Sub Domain` → `ocpp-dev.cable-app.com`, own application pool, .NET 10.
2. SSL panel → free certificate for the subdomain.
3. Scheduled Tasks → `https://ocpp-dev.cable-app.com/health`, every **10 minutes**.
4. Publish this project with a Web Deploy profile for that site; put the connection string,
   `Database` section and `Ocpp:StatusApiKey` in the site's `appsettings.Production.json` (not in git).
5. Apply `Scripts/OcppConnect_Phase0.sql` then `Scripts/OcppConnect_Phase1.sql` to the target DB.

## Real-charger test (RH4)

1. Register it: `POST /api/admin/ocpp/charge-points { chargingPointId, chargePointId: "RH4", requirePassword: false }`.
2. Set `Ocpp:AuthorizeMode = RejectAll` on `ocpp-dev` for the first connection test.
3. After hours: write down the current SafeerSoft URL + port, set `wss://ocpp-dev.cable-app.com/ocpp16/`
   port `443`, reboot, watch `/status` and `OcppRawMessage` for BootNotification → StatusNotification
   (both connectors) → Heartbeat → MeterValues. Restore the SafeerSoft URL afterwards.
4. Before go-live: load the station's cards into `OcppAuthorizedTag`, switch to `AuthorizeMode = List`.
