# Production deployment — 2026-08-22

**Script:** `Scripts/PROD_Deploy_2026-08-22_Master.sql`
**Target:** `db_ab1977_cableproduction`

---

## Do this

1. Run `PROD_Deploy_2026-08-22_Master.sql` against production.
2. Check the verification table it prints — **every row must read `OK`**.
3. Publish the WebApi.

That order matters. See "The one that blocks the publish" below.

---

## How this was built

The delta was **not** written from memory. `INFORMATION_SCHEMA` was diffed
between dev and production — 88 tables, every column, index and foreign key.
Five objects differed; all five are in the script.

It was then **dry-run against dev**, where every object already exists, and
reported every step as `[SKIPPED]` with all verification rows `OK`. That proves
both the syntax and the idempotency. The wrong-database guard was tested
separately by running the production script against dev: it refused, named both
databases, and exited non-zero without touching anything.

One real bug was found and fixed by that dry run: `RAISERROR` will not accept
`DB_NAME()` directly as a substitution argument. It would have failed on the
first statement in production.

---

## What the script changes

| # | Object | Why |
|---|---|---|
| 1 | `UserAccount.ProviderWebSecurityStamp` | **Blocks the publish** — see below |
| 2 | `UserAccount.LastLoginAt`, `LastSeenAt` + index | Last-login and active-user tracking |
| 3 | `UserRate` table (+3 indexes, 3 FKs) | Driver rating — shipped to dev 2026-08-17, never deployed |
| 4 | `LoyaltyBoost`, `LoyaltyBoostProvider` | Points-multiplier campaigns |
| 5 | `PartnerTransaction` +4 columns, FK, index | Multiplier attribution |

Additive only. Nothing dropped, altered or backfilled. Every new column is
nullable or defaulted, so no table is rewritten and existing rows stay valid.

### The one that blocks the publish

Production has `SecurityStamp` and `ProviderSecurityStamp` but **not**
`ProviderWebSecurityStamp`. The current build reads all three on every
authenticated request.

**Publish without running this script and every authenticated request returns
500** — the entire app, not just the new features. This is why the script goes
first.

---

## Nothing to seed

- **Welcome bonus** (double points on a first ever charge) is intrinsic. It
  needs no row in any table and is live the moment the code deploys. To retune
  or switch it off afterwards: `PUT /api/settings/welcome-bonus` with
  `{"multiplier": 1}` to disable, `2` for double, `3` for triple.
- **Boost campaigns** are created through the admin API. Tables ship empty.
- **Admin endpoints** authorise on `RoleId`, not on `Privilage` rows, so no
  permission seeding is required.

---

## Do NOT run these on production

- `LoyaltyBoosts_Phase1.sql` / `LoyaltyBoosts_Phase2_WelcomeBonus.sql` —
  superseded. Phase 2 migrates an older shape of `LoyaltyBoost` that only ever
  existed on dev; the master script creates the table in its final form.
- `UserRating_Phase1.sql`, `UserActivityTracking.sql` — folded in.
- `ProviderSessionStamp.sql` — covers `ProviderSecurityStamp`, which production
  already has. The master script adds the missing `ProviderWebSecurityStamp`.

---

## Web publish

Verified by a real `dotnet publish -c Release`:

- `wwwroot-partner` → **67 files**, `wwwroot-admin` → **342 files**, both present
  in the publish output.
- The partner SPA is already built for production: `virtualPath: "/partner"` and
  a **relative** API url `"/"`, so it calls whichever host serves it. **No
  rebuild needed** — it will hit production automatically.
- `appsettings.Production.json` ships and points at
  `db_ab1977_cableproduction`.

Build: 0 errors.

---

## Expected after deploy

- `neverSeen` in `GET /api/users/activity-stats` starts at almost the full user
  count and falls over the following weeks. Tracking begins at deploy, so every
  existing user reads `NULL` until they next open the app. **The first week
  understates usage — it is not a drop.**
- `loggedInLast30Days` will always be far below `monthlyActiveUsers`. Access
  tokens are long-lived, so active users rarely re-authenticate. By design.

For reference, the current best estimate from the `NotificationToken` proxy is
roughly **500 daily / 6,000 monthly** active against 25,236 users.

---

## Rollback

Rolling the API back needs **no database change** — the previous build ignores
every object the script adds.

---

## Known production quirk (no action)

`UserAccount` will have **28** columns on production versus **26** on dev.
Production carries two legacy columns, `OriginalCity` and `OriginalCountry`,
that dev does not. Both are nullable and unmapped, so the application ignores
them. Documented here so the mismatch is not mistaken for a missing migration
later.

---

## Still outstanding (not in this deploy)

- `.well-known/apple-app-site-association` returns 404 on production.
- No admin UI for the welcome bonus or boost campaigns — API only.
- No unit tests for the multiplier resolution matrix; verified end-to-end on dev.
