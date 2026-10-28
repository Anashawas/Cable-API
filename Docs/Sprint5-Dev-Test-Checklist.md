# Sprint 5 — dev test checklist

Scope: everything in `LIVE_CHARGING_SESSION.md`, `PARTNER_BILLING.md` and `CABLE_GIFTS_AND_PROMOS.md` (worker
tracker postponed). **Dev only — production untouched.** Backend detail per area: `Sprint5-LiveCharging-A.md`,
`Sprint5-LiveCharging-B.md`, `Sprint5-PartnerBilling.md`, `Sprint5-GiftsAndPromos.md`; mobile spec:
`Cable-Connect-Live-And-Limits-Spec.md`.

## Already done on dev
- SQL applied to `db_ab1977_cable` (Oct 28): `Sprint5_LiveCharging_A.sql`, `Sprint5_LiveCharging_B.sql`,
  `Sprint5_PartnerBilling.sql`, `Sprint5_GiftsAndPromos.sql` — all clean (18 sticker codes, 5 plans, 3 subscriptions
  migrated, old rows untouched).
- Backend smoke-tested on a local API against dev (live charging, billing, gifts, promos, scan pipeline, app start
  rules with the simulator).

## Publish to dev (in this order)
1. WebApi → `anashawas-001-subsite2 - Web Deploy` (Staging) — includes the partner + admin bundles.
2. Cable.Ocpp → `anashawas-001-site3 - Web Deploy` (it enqueues jobs only the new API runs).

## Settings that start OFF / at defaults (admin → Cable Connect → live charging settings)
| Setting | Default | Meaning |
|---|---|---|
| `Ocpp.AutoPoints.Enabled` | **off — not used** | Decision Oct 28: no automatic charging points. The admin switch was removed |
| `Ocpp.AppStart.RequireLocation` | on | App start needs the phone's location |
| `Ocpp.AppStart.MaxDistanceMeters` | 300 | Max distance from the station for an app start |
| Point value | 50 points = 1 JOD | From the default points conversion rate (confirmed). The seeded loyalty plans (20/50/100 points per scan) lose money at this value — adjust their tiers in admin → Plans (the calculator shows red / green) |
| `Billing.TargetMarginPercent` | 20 | Bundle margin target |
| `Billing.BundleMinimumTransactionAmount` | 2 JOD | Minimum transaction for bundle partners |

## Test on dev

### Live charging (driver app endpoints — test with the mobile build or the API)
- [ ] `/live`: plug shows `canStartFromApp` / `cannotStartReason`, `suspectedIssue` after 3 plug reports in an hour
- [ ] App start without location → 422 `LOCATION_REQUIRED`; far away → `TOO_FAR_FROM_STATION` with the distance
- [ ] Start from a sticker code (`CHG-…`) → session recorded `Sticker`; another plug's code → `CODE_MISMATCH`
- [ ] Second running session → 409 `sessionAlreadyOpen`
- [ ] Session details with chart, cost so far, station tariff (when set), receipt PDF + verify page
- [ ] Pushes: started, finished, "car finished, unplug"
- [ ] Monthly summary
- [ ] "Problem with this session" (7 days, one open per session); "plug didn't work" report → owner push

### Admin web (`dev.cable-app.com/admin`)
- [ ] Charging sessions list (filters, search by phone / #id, CSV) and detail
- [ ] User profile → Charging tab (sessions), promos (revoke)
- [ ] Station: own tariff (set / remove), sticker codes (renew, PDF), plug reports (resolve)
- [ ] Live charging settings (max distance, require location)
- [ ] Cable Gifts: create (image), order list, fulfil with voucher code, cancel → points back, CSV, dashboard count
- [ ] Promo campaigns: create (scope rules), codes + CSV, assign, report with Cable vs partner split and budget
- [ ] Plans: edit price / points rule (tier validation), margin calculator, history, feature matrix
- [ ] Upgrade requests: record payment from a request (fulfils it), reject
- [ ] Partner profile: subscriptions history, new / trial / cancel / upgrade; agreement billing model + sell bundle

### Partner web (`dev.cable-app.com/partner`)
- [ ] Billing page (owner only): plan, features, request upgrade / renew, trial, payments + receipts, bundle counter
- [ ] Cable Connect sessions: driver (masked phone), car, start method, points, receipt; filters
- [ ] Stickers PDF (owner only), plug reports + resolve, suspected-issue badge
- [ ] Complaints: charging-session issues with the session details and reply
- [ ] Transactions: promo line ("collect from customer", "Cable pays you"); settlement "discounts paid by Cable"
- [ ] Bundle partner with 0 scans → creating a code shows the renew message

### Regression (must be unchanged)
- [ ] Partner QR scan without a promo: same points, commission and settlement as before
- [ ] Percentage partners (everyone today) see no billing-model change
- [ ] Existing subscriptions / receipts in admin still work
- [ ] Cable Connect live page, limits and SignalR updates as before

## Decisions taken from the documents' defaults (change if needed)
- Automatic points: same rules as a QR scan at that station (reward %, rate, commission to the station wallet),
  only app starts and cards linked to a driver; **off** until switched on.
- Gifts: points taken at order, refunded on cancel; Cable's team fulfils from the admin.
- Promos: version 1 applies to partner QR scans only; one code per transaction; the higher of promo multiplier and
  boost (never multiplied); points on the final amount; commission on what the partner received.
- Bundles: a scan is taken when a code completes; at 0 scans new codes are refused (409), earlier codes still
  confirm with 0 points; renewal adds to what is left.

## Known points to decide / follow up
- Gift refund line in the points history is written in the device language (purchase line in the request
  language) — cosmetic.
- At a bundle partner whose bundle is already used up, the preview's promo list may still show points (the scan
  itself awards 0) — cosmetic.
- Separate tasks raised: user-car update/delete ownership check; complaint endpoints access checks.

## Dev test data to remove after testing
- Account 14119 (zz.multidev) is a temporary **admin** — undo: `UPDATE dbo.UserAccount SET RoleId = 3, IsActive = 0, IsDeleted = 1 WHERE Id = 14119`.
- Test gift #1 "Cable mug (test)" (inactive), promo campaign #1 "Free wash (test)" (inactive), session issue #124,
  plug report #1 (resolved), upgrade request #1 (rejected), partner codes PTR-ZZSMK1/2 at station 37 (completed).
