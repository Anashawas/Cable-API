# Sprint 5 — leftovers (backend)

The items left open after the four Sprint 5 changes. Schema: `Scripts/Sprint5_Leftovers.sql` (two new tables, for email
receipts only) — run it on dev before deploying this API build. Cable.Ocpp is unchanged in behaviour (it only builds
against the new entities).

| # | Item | Endpoint / place | Doc |
|---|---|---|---|
| 1 | Admin session alerts (LIVE §11.2) | `GET /api/admin/ocpp/sessions/{id}` → `alerts[]`; list rows → `hasAlerts` | Sprint5-LiveCharging-B §3 |
| 2 | Driver history month filter | `GET /api/users/me/ocpp-sessions?month=YYYY-MM&page=&pageSize=` | Sprint5-LiveCharging-A §4 |
| 3 | Upgrade requests badge | `GET /api/admin/upgrade-requests/pending-count` → `{ pendingCount }` | Sprint5-PartnerBilling §3 |
| 4 | Promo in admin settlement exports | `GET /api/offers/GetSettlementsCsv` (last column), `GET /api/offers/GetSettlementTransactions` | Sprint5-GiftsAndPromos §2.3 |
| 5 | BonusPoints promo vs boost caps | boost caps and the admin boosts "spent" figure | Sprint5-GiftsAndPromos §2.3 |
| 6a | Gift refund history line language | `GET` my points history | below |
| 6b | Preview at a used-up bundle partner | `PreviewPartnerCode` | Sprint5-GiftsAndPromos §2.3 |
| 7 | Email receipts (LIVE §13.2, off by default) | `GET/PUT /api/users/me/ocpp-settings` + the "finished" job | Sprint5-LiveCharging-B §5 |
| 8 | Model compare (PARTNER_BILLING §3.4) | `POST /api/admin/plans/model-compare` | Sprint5-PartnerBilling §5 |
| 9 | Landing deep links | `/c/{code}`, `/r/{receiptNo}` (+ `/ar/…`, `/en/…`) | below |

## 1. Admin session alerts

`alerts[]` on the detail: `{ code, text, textAr, textEn }` — `startedFarAway` (> 80 % of `Ocpp.AppStart.MaxDistanceMeters`),
`repeatedFailedStarts` (the same driver, ≥ 2 refused / unanswered RemoteStarts on any charger of the station in the 30 min
before), `parkedAfterCharging` (`ParkedNotifiedAt` set), `limitStopNotAccepted` (the server's own limit stop was not
accepted — recognised by its "Charge limit reached" audit text), `stale`. List rows get `hasAlerts` from the cheap ones only
(far away, parked, stale). Code: `Application/Ocpp/LiveCharging/AdminSessionQueries.cs` (`AdminSessionAlerts`).

## 2. Month filter

Same Jordan-month range as `/summary?month=` (`JordanMonth`), 400 on a malformed month. No `month` = unchanged.

## 3. Pending count

Counts `PlanUpgradeRequest` rows with status `Pending` (not deleted). Admin only.

## 4. Promo in settlement exports

- CSV: new last column `PromoDiscountsPaidByCable` (= `ProviderSettlement.PromoCableShareAmount`, already in `NetBalance`).
- Line items: `promoDiscountsPaidByCable` on the settlement; per partner line `promoTitle`, `promoDiscountJod`,
  `promoCableShareJod`, `promoProviderShareJod` (null without a promo). The items are now `SettlementLineItemDto`, which
  extends the old `ProviderActivityDto` — every old field is still there in the same place.

## 5. BonusPoints promo on a boosted scan

The boost's spend stays derived from the transactions (awarded − base) minus the `PromoUsage.BonusPoints` of the same scan
(`Application/Loyalty/BoostSpend.cs`), used by the caps (`LoyaltyBoostService`) and the admin boosts list. A winning promo
multiplier already clears the boost; a losing one or a discount adds no bonus of its own, so only BonusPoints promos change
the figure. No schema change; historical scans are corrected too.

## 6. Cosmetic

- **(a) Gift history lines.** Both the purchase (−points) and the refund (+points) are now written with one helper
  (`GiftHistoryTexts`): the purchase in the buyer's request language, the refund in the user's device language (never the
  admin's). And the user's points history re-renders every `GiftOrder` line in the reader's language from the order
  (bilingual at read), so the two lines always match. Admin lists show the stored note.
- **(b) Used-up bundle preview.** `PreviewPartnerCode` shows `basePoints: 0` with `pointsToBeAwarded: 0`, and
  `PromoApplicator.DecoratePreviewAsync(…, bundleQuotaReached)` quotes every promo from 0 base points (only a BonusPoints
  promo still adds its bonus, as the scan does); `pointsUnavailableReason` stays `partnerQuotaReached` with a promo chosen.

## 7. Email receipts

- `GET /api/users/me/ocpp-settings` → `{ emailReceipts, hasEmail }`; `PUT` `{ emailReceipts }`. No row = off.
- `BackgroundJobService.NotifyOcppDriverSessionAsync` (kind Completed — after pricing and the points job) calls
  `SessionReceiptEmailer.SendIfOptedInAsync`: opted in + valid email + not yet emailed → builds the receipt PDF (assigns
  the receipt number if needed), sends it with `IEmailService` (attachments were already supported), records
  `OcppReceiptEmail` (Sent / Failed). Subject / body in the device language (latest token, Arabic default). Never throws.
- Replayed sessions (offline > 15 min) get no "finished" push and so no email.

## 8. Model compare

See Sprint5-PartnerBilling §5. Pure maths in `ModelCompareMath` (`Application/Billing/Plans/ModelCompare.cs`).

## 9. Landing deep links

`LandingPageMiddleware` rewrites GET `/c/{code}[/]` → `/c/index.html`, `/r/{receiptNo}[/]` → `/r/index.html`, and
`/{ar|en}/c|r/{x}[/]` → `/{ar|en}/c|r/index.html` — only when that file exists under wwwroot, never for anything else
(so `/api/…` is never touched). The pages read the code from the URL and call `GET /api/charging-points/connectors/resolve?code=`
and `GET /api/receipts/verify/{receiptNo}`, both now explicitly `AllowAnonymous` (same origin; the default CORS policy
applies). `Scripts/build-landing.ps1` now requires `c\index.html` and `r\index.html` in the export.

## Test ideas

1. Alerts: start from the app at ~250 m (max 300) → detail `startedFarAway`, list `hasAlerts`; two refused RemoteStarts
   (charger offline / Rejected) then a good one → `repeatedFailedStarts`; leave the car plugged after `Finishing` →
   `parkedAfterCharging`; simulator rejecting RemoteStop with a JOD limit → `limitStopNotAccepted`.
2. `?month=2026-10` vs `/summary?month=2026-10` — same count; `?month=2026-13` → 400.
3. Create an upgrade request → pending-count +1; reject → −1.
4. Scan with a DiscountPercent promo, run settlement → CSV last column and the line item promo fields.
5. Boost with `maxBonusPointsPerUser` 100, ×2 on a 40-point scan with a BonusPoints 50 promo → cap counts 40, not 90.
6. Buy a gift in English, admin cancels while the device is Arabic → history in either language shows both lines in it.
7. Bundle at 0 scans, preview with a DiscountPercent and a BonusPoints promo → basePoints 0; bonus promo shows its bonus only.
8. PUT emailReceipts true, finish a session → one email with `CHS-….pdf`; re-run the job → no second email; SMTP down →
   `OcppReceiptEmail.Status = Failed`, job succeeds.
9. Model compare for station 37 with and without `bundlePlanId` / `priceOptionId`; a provider with no transactions → `noData`.
10. After `build-landing.ps1`: `/c/CHG-7K2M9Q`, `/ar/c/CHG-7K2M9Q/`, `/r/CHS-2026-000072` serve the pages; `/c/` unchanged;
    `/api/receipts/verify/…` unchanged.
