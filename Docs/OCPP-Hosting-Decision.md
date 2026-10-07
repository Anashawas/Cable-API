# Cable Connect (OCPP) — Hosting Decision

> Where should the OCPP server (`Cable.Ocpp`) run: on the current SmarterASP shared plan,
> on a SmarterASP VPS, or on a small AWS Lightsail Windows VPS? Written for the team to
> review. Numbers are from the dev deployment (Oct 3–4, 2026) and public price lists on
> Oct 5, 2026 — verify prices at checkout.

---

## 1. What makes this service different from the rest of Cable

The API, admin and landing site answer HTTP requests and then sit idle — perfect for shared
hosting. `Cable.Ocpp` is different: **each charger keeps one WebSocket open 24/7** and
reports over it every 15–60 s. If the process stops, every charger is disconnected; they
reconnect by themselves, but only once the process is back.

So the one hosting property that matters is: **does the process stay up without anyone
asking for it?**

## 2. What we measured on the SmarterASP shared plan (W1050-EU)

The OCPP server is deployed at `ocpp-dev.cable-app.com` (own site + app pool) and works:
TLS, WebSockets, .NET 10, full message flow, faults → push notifications — all verified
against the dev database.

| Test | Result |
|---|---|
| Full session / outage replay / rejected card / auth / fault | ✅ all pass over the internet |
| Charger silent for 25 min, **no keep-alive task** | ❌ IIS shut the app down after **11 minutes** ("server shutting down") — an open WebSocket does not count as activity for IIS's idle timer |
| Same test **with** a scheduled task hitting `/health` every 5 min | ✅ socket alive at 25 min, heartbeats answered |
| Restarts in the first 24 h without the task | 9 — each one = every charger dropped, until some HTTP request woke the app again |

SmarterASP support confirmed in writing: on a shared plan they **cannot** change the idle
timeout, the recycle schedule, the memory limit, or set the pool to *Always Running*.
"You can use our VPS plan and update the pool to always running."

### What that means in practice

| Event | Shared plan |
|---|---|
| Idle shutdown (~10 min without an HTTP request) | prevented by the 5-min scheduled task + the app's own self-ping; if either stops, chargers drop within minutes |
| Scheduled recycle (IIS default every ~29 h) | cannot be changed; every charger drops for ~1 min |
| Memory-limit recycle | cannot be changed or seen |
| Host maintenance / reboot | no notice |
| Alerting when the pool is stopped (503) | none — we only know when someone looks |

Data is **not** lost in any of these (chargers queue and resend; proven by the replay test).
What is lost is the live view during the gap, and the owner's trust if it happens often.

**Verdict:** fine for the pilot (one charger, friendly station, nothing promised). Not fine
for a paid service whose product *is* "live".

## 3. Options

Requirements: Windows Server (keep IIS + Web Deploy as today), 2 vCPU, ≥ 2 GB RAM, EU
region near the SmarterASP SQL server, flat monthly price, no usage metering.

| Option | Spec | Price | Pool control | Verdict |
|---|---|---|---|---|
| **A. Stay on shared W1050** | shared | $0 extra | none | pilot only |
| **B. SmarterASP Windows VPS** (V650) | 2 cores, 8 GB, 200 GB | **$200/mo** (3-mo to 3-yr terms) | full | works; 5–9× the price of C for the same box; only advantage is the same datacenter as the DB |
| **C. AWS Lightsail Windows 2 GB** | 2 vCPU, 2 GB, 60 GB SSD, 3 TB transfer | **$22/mo**, monthly, cancel anytime (often first 3 months free) | full | **recommended start** |
| **C'. AWS Lightsail Windows 4 GB** | 2 vCPU, 4 GB, 80 GB SSD, 4 TB | **$44/mo** | full | the production tier; upgrade from C by snapshot in ~15 min |
| Others checked | IONOS Windows M $16.50 · Contabo VPS 4 Windows ~$16 · Vultr 2/4 + Windows $36 · Azure B2als v2 Windows ~$40 (≈$27 with 1-yr reservation) | | full | all viable; cheaper ones are smaller European hosts with slower support; Azure needs a 1-year commitment for the good price |
| Hostinger | Linux only — no Windows | | | excluded |

Lightsail is a **fixed bundle**: the price is the same every month regardless of how many
chargers or messages. The only metered items are snapshots (cents) and transfer beyond the
bundle (we use < 1 % of it: ~5–10 MB/day per charger).

## 4. Resource footprint of the service (why the small bundle is enough)

Measured on dev: a frame averages ~115 bytes, a MeterValues frame ~460 bytes. Per charger
with RH4's settings (heartbeat 60 s, samples every 15 s on 2 plugs, ~8 h charging/day):

| | Per charger |
|---|---|
| Memory on the server | ~50–100 KB (app base ~150–200 MB) |
| CPU | < 0.1 % |
| Bandwidth | ~5–10 MB/day |
| **Database** — raw log (7-day retention) | ~70 MB steady state |
| **Database** — meter samples (full 90 days, then 1/min) | ~150 MB/year |

A hundred chargers fit on the $22 box with room to spare. **The database is the real
capacity limit**, not the server.

## 5. The database stays on SmarterASP — and its limits

The OCPP server connects to the existing SQL database over the internet (same as the
local runs and tools do today). SmarterASP support confirmed the plan's limits:

- per-database maximum **6 GB** on this plan (production is now **4,000 MB**, raised free on
  request; dev 1,000 MB); account pool 10,000 MB
- beyond that: $45/year per extra GB, or ">10 GB → consider a VPS"

With the retention settings above, **ten chargers ≈ 2.2 GB after one year** — inside the
4 GB. Fifty chargers would need shorter raw retention and the 6 GB ceiling, which is the
point to consider a dedicated SQL instance (SQL Server Express is free up to 10 GB and
could run on the same Lightsail box). Not a decision for now.

## 6. Recommendation

1. **Pilot (RH4, next weeks):** stay on shared `ocpp-dev` as deployed, with the 5-min
   scheduled task. Cost $0. Use it to read the charger's config keys and prove the hardware.
2. **Before the first paying station:** move `Cable.Ocpp` to **AWS Lightsail Windows 2 GB
   ($22/mo, Frankfurt)**. API, admin, landing and the database stay on SmarterASP unchanged.
   Same binary, same Web Deploy flow; only the server address, an *Always Running* app pool
   and a free Let's Encrypt certificate change. ~1 hour of setup.
3. **When the pilot grows:** snapshot → $44 bundle (4 GB). No code change.
4. **Database:** keep the OCPP retention jobs tight (raw 7 days, samples thinned after 90
   days); revisit at ~30 chargers.

Yearly cost of the recommended path: **$264** (year one often ~$198 with the free months),
versus $2,400 for the SmarterASP VPS and $0 but unreliable for staying shared.

## 7. What the team should sanity-check

- Is a 1-minute drop per host recycle acceptable for the pilot? (shared plan, yes)
- Does anyone prefer Azure for account/billing reasons? Then B2als v2 with a 1-year
  reservation (~$27/mo) is an equivalent choice.
- Who owns the box: Windows Updates, reboots off-peak, an external uptime monitor on
  `/health` (UptimeRobot free tier is enough).

Sources: [Lightsail bundles](https://docs.aws.amazon.com/lightsail/latest/userguide/amazon-lightsail-bundles.html) ·
[SmarterASP VPS](https://www.websiteplanet.com/web-hosting/smarterasp-net/) ·
[Azure B2als v2](https://cloudprice.net/vm/Standard_B2als_v2) ·
[IONOS](https://www.comparevps.com/hosting/ionos) · [Contabo](https://cybernews.com/best-web-hosting/contabo-review/pricing/) ·
[Vultr](https://stackfreeks.com/vultr-pricing-2026/) · SmarterASP support tickets (Sep 24 and Oct 5, 2026).
