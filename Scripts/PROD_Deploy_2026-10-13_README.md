# Production deploy — Oct 13, 2026

Dev and production were compared object by object on Oct 13 (tables, columns, indexes, foreign keys, checks,
procedures, lookup data). `PROD_Deploy_2026-10-13_Master.sql` closes every gap. It was rehearsed on a local copy of
production's structure: run twice, all 29 VERIFY rows OK, second run changed nothing, and the result compared equal
to dev. The only intended differences left: `UserAccount.OriginalCity / OriginalCountry` exist on production only
and are kept.

## Order

| # | Step | Who | Notes |
|---|---|---|---|
| 0 | Publish the API to **dev** and test there first | you + team | The build now carries Yahia's admin + partner redesigns (`develop` fast-forwarded into local `main`, not pushed; portals rebuilt into `wwwroot-admin` / `wwwroot-partner` on Oct 13) and the new `/api/health` endpoints. Check Cable Connect (admin by station, partner redesign + payments screen), station edit, users list. |
| 1 | Backup `db_ab1977_cableproduction` | you | SmarterASP → MSSQL → Backup |
| 1b | `WebApi.csproj` → `<EnvironmentName>Production</EnvironmentName>` for the production publish | you | back to `Staging` afterwards |
| 2 | `WebApi/appsettings.Production.json`: `OcppServer` | ✅ done Oct 13 | `https://ocpp.cable-app.com`, new ApiKey (same as Cable.Ocpp's production `StatusApiKey`), 35 s. Optional: `SmsService.MinBalance` (default 1000). |
| 3 | Run `PROD_Deploy_2026-10-13_Master.sql` in SSMS on **production** | you | ~1 min. Last grid: every row **OK**. It stops by itself on any other database. |
| 4 | Publish the API (+ admin + partner) to production **right after step 3** | you | Step 3 renames `StationPremiumSubscription` → `Subscription`; the old API's premium screens fail until the new build is live. Build the portals first (`build-admin.ps1`, `build-partner.ps1`). |
| 5 | Check | you + Claude | `https://cable-app.com/api/health` and `/api/health/sms` → 200; admin: subscriptions, price-alerts page, Cable Connect page; Hangfire dashboard lists the new jobs |
| 6 | UptimeRobot | you | replace the API monitor with `https://cable-app.com/api/health`; add `https://cable-app.com/api/health/sms` |
| 7 | Banner phones | you | `POST /api/banners/admin/normalize-phones?dryRun=true` (admin) — expected: nothing to change on production |

## Cable Connect switch (AWS charger server → production DB) — same day or later

Until this is done, `ocpp.cable-app.com` keeps writing to the **dev** database and production Cable Connect shows no chargers.

| # | Step | Notes |
|---|---|---|
| a | Lightsail → Cable-OCPP → **Create snapshot** | before any change on the server |
| b | `Cable.Ocpp/appsettings.Production.json` — ✅ created Oct 13 | copy of `appsettings.Staging.json` with: the **production** connection string, `Ocpp.SelfPingUrl = https://ocpp.cable-app.com/health`, a `StatusApiKey` equal to the API's `OcppServer.ApiKey` (step 2) |
| c | Environment = Production **for the AWS profile only** — ✅ set Oct 13 | add `<EnvironmentName>Production</EnvironmentName>` to `aws-ocpp - Web Deploy.pubxml`. Do not change `Cable.Ocpp.csproj` (Staging) — ocpp-dev must stay on the dev DB. |
| d | Run `PROD_Ocpp_RH4_Register.sql` on production | registers RH4 on station 210 **without a password** (as the unit is set up) + its 2 cards. The admin "Register" form would issue a password and RH4 would be refused. |
| e | Publish Cable.Ocpp → `aws-ocpp` | then `https://ocpp.cable-app.com/health` → 200 and `/status` (X-Api-Key) shows RH4 within a minute or two |
| f | Production admin, station 210 | record the real **OcppConnect subscription** payment (switches live sharing on), set the plug types and kW of RH4's 2 connectors, give the cabinet a display name |

After the switch, RH4 appears offline in the **dev** admin — expected; dev keeps using ocpp-dev and the simulators.

## Not part of this deploy

- Dev test data (simulators, demo station 223, loyalty boosts, notification templates, partner terms draft, the
  dev-only station-210 subscription). Production `SystemVersion` is newer than dev and stays.
- `IncreaseSharedLinkMaxUsage_2026-09.sql` — separate one-off with its own dry run (125 links since Sep 1 below the cap).
