# Cable — Session Handover (written 2026-08-02)

Read this FIRST, together with `CLAUDE.md`. It carries the working rules, business context, and exact current state so any new Claude session can continue where the previous one stopped.

---

## 1. What this product is (business)

Cable is an **EV charging marketplace for Jordan** (Arabic/English): EV owners (B2C app) find and rate charging stations; station owners & service providers (Partner app) manage their assets, workers, offers, loyalty, settlements, and fan announcements; a 2-person team runs everything through the Admin portal. Revenue features: **sold ad slots** (home banners, premium station cards, welcome-message takeovers — targeted by location, tracked per impression/click for billing), partner agreements/commissions, and loyalty/points economy.

Key business rules that keep coming up:
- Jordan time = UTC+3; DB stores UTC; responses convert via `JordanDateTimeJsonConverter`.
- Guests matter: most traffic is not logged in — analytics MUST accept tokenless events (`anonymousId` for uniques).
- Premium expiry is **warn-only** (admin demotes manually; `attention-summary` surfaces it).
- Worker sends need owner approval unless `autoApproveWorkerNotifications` (rate limit = 2 ACTUAL sends / 24h per provider).
- `actionType 1..5` contract is frozen everywhere.

## 2. Environments & the deployment ritual (NEVER break this)

| | Dev | Production |
|---|---|---|
| DB | `db_ab1977_cable` (MCP: `MSSQL_MCP_Cable`) | `db_ab1977_cableproduction` (MCP: `MSSQL_MCP_Cable_Production`) |
| API | http://dev.cable-app.com | https://cable-app.com |

- **All changes go to DEV first** (code + DB). Claude may freely change dev.
- **Production DB is READ-ONLY for Claude.** Prod changes ship as **idempotent guarded SQL scripts** in `Scripts/` that the USER reviews and runs in SSMS (backup first), then the USER publishes WebApi via SmarterASP.
- Scripts pattern: every statement guarded (`IF COL_LENGTH(...) IS NULL` / `IF NOT EXISTS`), PRINT progress, verification report at the end. See `Scripts/Deploy_Delta_2026-07-31.sql` (latest) and `Scripts/Deploy_DevToProd_Master.sql` (full master, 15 parts).
- **Never touch on prod:** test records for Apple review — `Test.Review@gmail.com`, `worker3@gmail.com`, station `Cable Demo Station (Test)` (IsTest=1). They stay until Apple approves.
- Prod `UserAccount` has legacy columns `OriginalCity`/`OriginalCountry` not on dev — leave them.

## 3. Working conventions (follow the existing code)

- Clean Architecture + CQRS/MediatR: `record XCommand(...) : IRequest<T>` + primary-constructor handler in the same file; FluentValidation validators beside them.
- Minimal API route chains: `.Produces<T>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesValidationProblem().ProducesInternalServerError().WithName().WithSummary().WithOpenApi()`; `.DisableAntiforgery()` on multipart uploads.
- Admin gating: `AdminRoleGuard.EnsureAdminAsync/IsAdminAsync` (Admin RoleId = 2). Owner/worker access via `ProviderManagers` (`ProviderType` = "ChargingPoint" | "ServiceProvider").
- Lists: optional pagination via `ToOptionallyPaginatedAsync(page, pageSize)` → `PagedResult<T>`; legacy bare-array shape when no params.
- File uploads: `UploadFileService.SaveFileAsync` (GUID names) into `UploadFileFolders.<Folder>`; a folder is PUBLIC only if also in `AllowedUploadFiles` enum (SecureFileServingMiddleware whitelist — a missing entry causes 401/404, this bit us with CableCarTypes). URLs built from config `File:ServerUrl` first.
- Every client-facing batch ends with: SQL script updated → `dotnet build` → live test on `http://localhost:5202` against dev DB → a client-facing `*.md` response doc in repo root.
- Live-test auth: mint HS256 JWTs with the user's current `SecurityStamp` from dev `UserAccount` (stamps ROTATE on every client login — always re-query). Admin user id=2. Test partner: owner 6046 / worker 14112 on ChargingPoint 42.
- DTO gotchas: no `DateTime` non-null defaults in DTO records (OpenAPI generator crashes via Jordan converter — use `DateTime? = null`); no optional ctor args inside EF projections (CS0854).

## 4. What is DONE (all live-tested on dev)

Everything in the client docs `BE-requirements.md` (Parts A & B), plus follow-ups:
- **Home ads**: nearby banners layered ranker (radius→city→national + pacing), admin radius setting, `GetNearest`/`GetNearestPremium`/`GetNearestNormal`, premium flags + viewImage flow (upload→pending→admin approve→B2C), welcome announcement (FLAT contract + actionLabels + freq caps + admin CRUD + delete/image/stats), analytics with guest `anonymousId`, 30-SECOND impression dedup, campaign/premium stats, `GET /api/admin/attention-summary` (11 counts, 7-day threshold).
- **Notifications (Part B)**: unified routing — every send builds inbox `deepLink` (`cable://charging-point?targetId=<id>` / `cable://service-provider?targetId=<id>`) + FCM data (`type`+`chargerId`+`deepLink`) from one helper (`NotificationRouting`); admin sends accept `targetType`+`targetId`; partner favorites-notify attaches routing automatically; `NotificationType.DeepLinksTo` routing map; **display names NameEn/NameAr seeded** (client's exact strings) and Arabic auto-title `"{nameAr} من {stationName}"`; F1 history with recipient/delivered/read counts; F2 own-review get/edit/delete (+ security fix: update was open to any user); F3 worker approval workflow; F4 auto-approve toggle; F5 templates CRUD.
- **Car-logo fix**: `CableCarTypes` added to `AllowedUploadFiles` (was 401/404 on prod — client's 62-logo bulk upload waits on deploy).
- **Image URLs** now built from configured `File:ServerUrl` (no more localhost URLs).
- Earlier eras (all shipped to prod already): loyalty admin suite, settlements, workers, social links, terms & conditions, edit-station requests, ChargerBrand M2M, nullable owners, pagination sweep, landing page at API root.

Client response docs in repo root: `BE_REQUIREMENTS_RESPONSE.md`, `BE_ISSUES_REPLY.md`, `BE_REPLY_2026-07-30.md`, `HOME_ADS_FEATURES.md`, `NOTIFICATIONS_PART_B_RESPONSE.md`.

## 5. Where we stopped (2026-08-02) + open items

1. **Prod deploy in flight**: user took prod DB + WebApi backups and was about to run `Scripts/Deploy_Delta_2026-07-31.sql` (dry-run passed on dev, 12 batches) and then publish WebApi. **First thing to check in a new session: did the deploy happen?** Verify: script's verification report all OK; `GET https://cable-app.com/api/notification-types` shows `deepLinksTo/nameEn/nameAr`; anonymous `GET https://cable-app.com/CableCarTypes/<file>.png` returns 200 (files listed in `GetAllCarTypes`).
2. **Git commit** — the ENTIRE working tree (~200 files, everything above) is **still uncommitted**. Recommended repeatedly; user hasn't done it. Suggest committing after the prod deploy is verified.
3. **`.well-known/apple-app-site-association`** 404 on prod (iOS deep links) — fix = serve that content-root folder via StaticFiles (ServeUnknownFileTypes, application/json). User was to check if files survived on SmarterASP.
4. **Apple review OTP**: confirm prod appsettings has `962790000000` in `OtpSettings.TestPhoneNumbers`.
5. **Deferred/future**: `favorite_added` (type 2) reserved as owner-facing "someone favorited your station" — not emitted yet; premium auto-expiry job; admin-configurable `expiringSoonDays`.
6. Client may send next batch docs (they arrive as `BE-*.md` in `C:\Users\an.hawas\Downloads`).

## 6. How the user works with Claude

Short messages, often typos, Arabic-market context. Standing preferences: don't re-ask for permission once granted for a batch; prepare prod scripts but NEVER execute on prod (user runs them); create client-facing MD files they can forward to the client team; answer quick production stats questions (registrations by Jordan day) via read-only prod queries; keep test data cleaned up after live tests.
