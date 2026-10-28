# Sprint 5 — Partner billing: plans, subscriptions, upgrade requests, scan bundles

Backend for `PARTNER_BILLING.md` (2026-10-27) items **A** (plans and "My plan"), **2b** (admin-managed plans) and
**B** (scan bundles). Items C and D shipped earlier; this adds `partnerQuotaReached` when a bundle is used up.

- SQL: `Scripts/Sprint5_PartnerBilling.sql` (idempotent, non-destructive). **Apply it before deploying this build**:
  the API maps the new tables and `PartnerAgreement.BillingModel`, so partner, wallet and my-assets reads fail
  until the script has run.
- Code: `Application/Billing/**`, `WebApi/Routes/BillingRoutes.cs`, EF configs in
  `Infrastructrue/Persistence/Configurations/Billing/`, entities in `Domain/Enitites/Billing/BillingEntities.cs`.
- Defaults are the document's section 7 decisions. Every price, tier and feature is data the admin can edit.

---

## 1. How it fits on the existing engine

There is still **one** billing engine. `Subscription` + `Payment` (+ grace settings, switch-off, receipt PDF) keep
deciding whether premium / Cable Connect is on, exactly as before.

| New | What it is |
|---|---|
| `BillingPlan` + `BillingPlanPriceOption` | Plans as data. `legacyEntityTypes` ties a plan to the existing engine: `premium` → `StationPremium,ServiceProviderPremium`, `connect` → `OcppConnect`. |
| `PlanSubscription` | One record per purchase / renewal / trial / bundle, never overwritten, with snapshots of the plan name, price option and points rule. Points at `Payment` / `Subscription` when it was paid through the existing engine. |
| `BillingPlanChangeLog` | Field-level diff of every plan edit. |
| `PlanUpgradeRequest` | The partner's "upgrade / renew / buy a bundle" request. |
| `BillingEvent` | Every billing push and admin alert, unique key → never sent twice; admin rows are the billing alerts list. |
| `PartnerTransactionBundleScan` | Marks a partner transaction as bundle-billed and records the scan it consumed. Side table, so percentage transactions are untouched. |
| `PartnerAgreement.BillingModel` | `percentage` (default, every existing agreement) or `bundle`. |

Recording a payment with the existing `POST /api/subscriptions/payments` now also:
1. opens a `PlanSubscription` record (plan inferred from `entityType`, or `planId` / `priceOptionId` if sent);
2. fulfils the station's pending upgrade request for that plan (or `upgradeRequestId` if sent).

Both are best effort for that endpoint: a failure is logged and never un-records the payment (same rule as the
receipt). Voiding a payment cancels the record it opened. New optional body fields on `RecordPaymentCommand`:
`planId`, `priceOptionId`, `upgradeRequestId` — the admin web works unchanged without them.

Station owners can now also download **Cable Connect** receipts (`GET /api/subscriptions/payments/{id}/receipt`
previously allowed the owner only for `StationPremium` / `ServiceProviderPremium`).

---

## 2. Partner endpoints (owner only — workers get 403)

Owner of the station / provider, or an admin. Workers never see billing (decision 6).

### `GET /api/provider/subscription?providerType=ChargingPoint&providerId=37`

```jsonc
{
  "providerType": "ChargingPoint",
  "providerId": 37,
  "tier": "connect",                 // key of the highest plan that is on; "free" otherwise
  "tierPlanId": 3,
  "tierName": "كيبل كونكت",
  "state": "expiringSoon",           // active | expiringSoon | inGrace | expired | switchedOff | none
  "expiresAt": "2026-11-17T00:00:00",
  "daysLeft": 5,
  "graceEndsAt": null,               // set when inGrace under the AfterDays rule
  "isTrial": false,
  "features": ["verifiedBadge","editInfo","appearFirst","images","complaints","analytics","workers","biometrics","connect"],
  "subscriptions": [
    { "entityType": "OcppConnect", "status": "ExpiringSoon", "isOn": true, "expiresAt": "…", "planKey": "connect", "subscriptionId": 12 }
  ],
  "loyalty": {
    "enrolled": true,
    "billingModel": "bundle",        // percentage | bundle
    "commissionPercentage": null,    // the owner's own % on the percentage model; null on bundle
    "minimumTransactionAmount": 2.0,
    "bundle": { "planSubscriptionId": 41, "planId": 4, "planKey": "loyalty_bundle_400", "planName": "حزمة 400 مسحة",
                "scansTotal": 400, "scansUsed": 132, "scansRemaining": 268, "percentRemaining": 67,
                "isUnlimited": false, "isTrial": false, "startsAt": "…", "endsAt": null, "state": "active" }   // active | low | exhausted | expired
  },
  "pendingUpgradeRequest": null,     // or { "id": 4, "tier": "premium", "planId": 2, "kind": "upgrade", "months": 3, "priceOptionId": null, "createdAt": "…" }
  "trialAvailable": { "premium": false, "connect": false, "loyalty_bundle_400": false, "loyalty_unlimited": true }
}
```

- `features[]` = free plan features ∪ features of every plan that is on (+ the plans they include). The apps switch
  on these keys instead of guessing (`Capabilities.isPremium`, `station_plan.dart`, `plan.ts`).
- On the free plan, `state` tells whether a paid plan lapsed (`expired` / `switchedOff`, with `expiresAt`) so the home
  banner can show; `none` = never had one.

### `GET /api/provider/subscription/payments?providerType=&providerId=`
```jsonc
[ { "id": 6, "referenceNo": "RCP-2026-000006", "amount": 40.0, "currency": "JOD", "method": "كليك",
    "paidDate": "…", "periodStart": "…", "periodEnd": "…", "planMonths": 1, "entityType": "OcppConnect",
    "receiptDownloadPath": "/api/subscriptions/payments/6/receipt" } ]
```
Non-void payments only. The PDF is the existing receipt endpoint.

### `GET /api/provider/subscriptions/history?providerType=&providerId=`
Every `PlanSubscription` record (see §4 shape), newest first.

### `POST /api/provider/upgrade-requests`
```json
{ "providerType": "ChargingPoint", "providerId": 42, "tier": "premium", "months": 3, "note": "…" }
```
`tier` is a plan key (`premium`, `connect`, `loyalty_bundle_400`, …) — or send `planId`; `priceOptionId` optional.
→ `200 { "id": 4, "status": "Pending" }`. `kind` is derived: `bundle` (loyalty plan), `renew` (plan already on), `upgrade`.
`409 UPGRADE_REQUEST_PENDING` while one is open. Admins get a push + inbox (`billing_upgrade_request`) and an alert row.

### `POST /api/provider/trials`
```json
{ "providerType": "ChargingPoint", "providerId": 42, "planId": 2 }
```
→ the trial record. Once per station / provider per plan. A subscription-plan trial needs no other subscription plan
on; a loyalty-plan trial started by the partner needs the agreement already on `bundle` (a partner can never move
themselves off percentage). Errors: `409 TRIAL_ALREADY_USED`, `409 ACTIVE_SUBSCRIPTION_EXISTS`,
`422 TRIAL_NOT_AVAILABLE`, `422 PLAN_ARCHIVED`, `422 PLAN_NOT_APPLICABLE`. Apps hide the button on iOS.

### `GET /api/plans?providerType=&category=`
Any signed-in user. Active + visible plans for the comparison table: names (`name` in the request language plus
`nameAr` / `nameEn`), features with names, price options (`isUnlimited`), points rule, trial days / scans. No margin data.

### Scan counter on existing responses
- `GET /api/offers/GetWalletBalance` → new `loyaltyBundle` (same shape as `loyalty.bundle`; null when never on a bundle).
- `GET /api/provider/my-assets` → new `loyaltyBundles: [{ providerType, providerId, billingModel, bundle }]` for each
  asset with an agreement or a bundle.

---

## 3. Admin endpoints

### Plans — `/api/admin/plans`
| Method | Path | |
|---|---|---|
| GET | `?category=Subscription\|Loyalty&includeArchived=` | with `subscriberCount` |
| GET | `/{id}` | |
| POST | `` | create → `PlanSaveResult` |
| PUT | `/{id}` | edit (same body + `note`) → `{ plan, changes[], affectedSubscribers, pricingChangedForNewOnly }` |
| PATCH | `/{id}/active` | `{ isActive, note }` — archive / reactivate. No delete. Free cannot be archived |
| GET | `/{id}/history` | change log |
| GET | `/{id}/subscribers` | who is on it now |
| GET | `/features` | fixed catalog `[{ key, name, nameAr, nameEn }]` |
| PUT | `/feature-matrix` | `{ plans: [{ planId, features: [] }], note }` — the "plan features" settings matrix |
| POST | `/margin-check` | §5 |

Plan body:
```jsonc
{
  "key": "loyalty_unlimited", "category": "Loyalty",
  "nameAr": "ولاء بلا حدود", "nameEn": "Unlimited Loyalty", "descriptionAr": "…", "descriptionEn": "…",
  "appliesTo": ["ChargingPoint", "ServiceProvider"],
  "features": [], "includesPlanIds": [], "legacyEntityTypes": [],
  "priceOptions": [ { "id": 11, "months": 1, "priceJod": 60, "scans": null }, { "months": null, "priceJod": 40, "scans": 400 } ],
  "pointsRule": { "mode": "Tiered", "fixedPoints": null,
                  "tiers": [ { "fromJod": 1, "toJod": 3, "points": 10 }, { "fromJod": 3, "toJod": null, "points": 20 } ] },
  "trialDays": 14, "trialScans": 50, "isActive": true, "isVisible": true, "sortOrder": 11,
  "badgeColor": "#7E57C2", "dailySoftCap": 150, "targetMarginPercent": 20,
  "note": "رفع السعر من 1 كانون الأول"      // PUT only, kept in the change log
}
```
Rules: key `[a-z0-9_]{2,50}`, fixed after creation; category fixed. Subscription options need `months`
(1–24) and no `scans`; loyalty options need `months` and/or `scans` (`scans: null` + `months` = unlimited).
PUT: options with `id` are updated, without `id` added, left-out ones retired (`isActive: false`). Price / rule edits
apply to new subscriptions and renewals only (records keep snapshots); feature edits apply at once.

Change log entry:
```json
{ "id": 77, "planId": 5, "changedByUserId": 1, "changedByName": "…", "changedAt": "…",
  "changes": [ { "field": "priceOptions[11].priceJod", "from": 50, "to": 60 },
               { "field": "features", "added": ["analytics"], "removed": [] } ],
  "note": "…" }
```

### Subscription records — `/api/admin/subscriptions`
| Method | Path | Body |
|---|---|---|
| GET | `?providerType=&providerId=&planId=&status=&from=&to=&page=&pageSize=` | |
| POST | `` | `{ providerType, providerId, planId, priceOptionId, startsAt?, payment?, isTrial, upgradeRequestId?, note? }` |
| POST | `/{id}/cancel` | `{ reason }` |
| POST | `/{id}/upgrade` | `{ planId, priceOptionId, payment?, note? }` |
| GET | `/{id}/break-even` | → `{ planSubscriptionId, period: total\|month, scansUsed, breakEvenScans, percent, state: ok\|warning\|over, avgPointsPerScan, costPerScanJod, priceJod, avgSource }` |

`payment` = `{ amount?, method: "CliQ"|"Cash", paidDate (Jordan local), payer: { payerId | userAccountId | name/phone… }, currency?, note?, receiptImageFileName? }`
— recorded through the existing `PaymentRecorder` (same receipt PDF, same "renewal extends from expiry" rule).
`amount` defaults to the option price (minus the upgrade credit). Without `payment` the period is complimentary.
Loyalty plans never take `payment`: their price is debited from the provider wallet (`bundlePurchase`).

- New subscription: `409 ACTIVE_SUBSCRIPTION_EXISTS` when another subscription plan is on and neither includes the
  other (renewing the same plan, or premium under connect, is allowed).
- Cancel: the record → `Cancelled`; what it switched on in the existing engine is switched off unless a later paid
  period covers it.
- Upgrade (decision 16): old record → `Upgraded`; credit = paid value × unused share (time, or scans for bundles,
  whichever is smaller), applied to the new price (`creditAppliedJod`). The old plan's engine row is switched off,
  unless the new plan includes it (connect ⊇ premium) — then it is extended to the new end.

Record shape (admin and partner history):
```jsonc
{ "id": 301, "providerType": "ChargingPoint", "providerId": 42, "providerName": "…",
  "planId": 5, "planKey": "loyalty_unlimited", "planCategory": "Loyalty", "planName": "…", "planNameAr": "…", "planNameEn": "…",
  "priceOptionId": 11, "priceOptionSnapshot": { "months": 1, "priceJod": 60, "scans": null },
  "pointsRuleSnapshot": { … }, "isTrial": false,
  "startsAt": "…", "endsAt": "…", "scansIncluded": null, "scansUsed": 1240, "scansRemaining": null, "scansCarriedOver": 0,
  "status": "Active",   // Active | ExpiringSoon | InGrace | Expired | SwitchedOff | Scheduled | Exhausted | Cancelled | Upgraded | Renewed
  "isOn": true, "graceEndsAt": null, "pricePaidJod": 60, "creditAppliedJod": 0,
  "paymentId": 88, "paymentReferenceNo": "RCP-2026-000088", "receiptDownloadPath": "/api/subscriptions/payments/88/receipt",
  "subscriptionId": 12, "walletTransactionId": null, "upgradedFromId": null,
  "source": "payment",  // payment | admin | trial | bundle | upgrade | migration
  "createdByUserId": 1, "createdAt": "…", "cancelledAt": null, "cancelReason": null, "note": null }
```
`Scheduled` = a renewal that starts when the current period ends; `Renewed` = a bundle whose leftover scans were
carried into the next one; `Exhausted` = a bundle with 0 scans.

### Upgrade requests — `/api/admin/upgrade-requests`
- `GET ?status=Pending|Fulfilled|Rejected&providerType=&providerId=&page=&pageSize=` — pending first; includes the
  requester's name and phone (E.164) for the follow-up call.
- `POST /{id}/reject` `{ reason }`.
- `POST /{id}/fulfil` `{ paymentId?, planSubscriptionId? }` — manual; normally automatic: recording a payment
  (existing payments screen, `RecordPaymentDialog` pre-filled with `entityType` / `entityId` / `planMonths`, or
  `upgradeRequestId`), creating the subscription, or selling the bundle fulfils it.

### Billing alerts — `/api/admin/billing-alerts`
`GET ?unacknowledgedOnly=&page=&pageSize=`, `POST /{id}/acknowledge`. Kinds: `adminSoftCap`, `adminBreakEven`,
`upgradeRequest`.

### Scan bundles on a partner agreement — `/api/partners/{agreementId}`
| Method | Path | Body |
|---|---|---|
| POST | `/bundles` | `{ planId, priceOptionId, upgradeRequestId?, note? }` — admin |
| GET | `/bundles` | — admin or the owner: `{ agreementId, billingModel, minimumTransactionAmount, current, history[] }` |
| PATCH | `/billing-model` | `{ billingModel: "percentage"\|"bundle", minimumTransactionAmount? }` — admin → `{ …, openPercentageCodes, bundle }` |

---

## 4. Scan bundle rules (§3.2, decisions 11–14, 20)

| Rule | Implementation |
|---|---|
| One model per agreement | `PartnerAgreement.BillingModel`; admin switch only; never automatic. |
| Switch percentage → bundle (decision 13) | Codes created before the switch keep their reserved commission and complete on percentage, so the open weekly settlement closes on percentage; codes created after are bundle-billed. `openPercentageCodes` tells the admin how many are still in flight. |
| Buy | Wallet debit `WalletTransactionType.BundlePurchase = 20` (`referenceType: PlanSubscription`), so it flows through the existing settlement. Reserved for the system in `AddWalletDeposit`. |
| Points | From the bundle's snapshotted points rule (Fixed or Tiered by amount). Below the first tier → 0 points; `PreviewPartnerCode` says `belowMinimumAmount` with `minimumAmountForPoints`. Boosts / welcome bonus still apply on top (unchanged pipeline). Points are fixed when the code is created. |
| Minimum amount (decision 12) | Mandatory on bundle: the agreement's `minimumTransactionAmount`, else `Billing.BundleMinimumTransactionAmount` (2 JOD). The switch to bundle fills it in when empty. |
| Consume | Exactly one scan when the transaction **completes**, with one conditional `UPDATE … SET ScansUsed = ScansUsed + 1 WHERE ScansUsed < ScansIncluded` inside the completion transaction — two codes cannot take the last scan. Created / expired / cancelled codes consume nothing. |
| At 0 scans | New code → `409 bundleExhausted` ("خلصت حزمة المسحات — جدّد لتكمل تعطي نقاط لزباينك"). Codes created earlier still confirm, with 0 points; `PreviewPartnerCode` returns `pointsAvailable: false, pointsUnavailableReason: "partnerQuotaReached"`. No automatic switch back to percentage. |
| Renewal adds | New bundle = option scans + leftover scans of the previous one (even if it expired, decision 11); a still-running period of the same plan is extended. Previous record → `Renewed`. |
| Reversal | `POST /api/loyalty/admin/ReverseTransaction` with `activityType: "Partner"` on a completed bundle-billed transaction gives the scan back. |
| Pushes | Owner at 20%, 10% and 0% left (`billing_bundle`), each once; a skipped threshold is never sent late. |
| Unlimited (decisions 14, 20) | `scans: null` with months. Never stops scanning. Admin alert when a station exceeds the plan's `dailySoftCap` in a Jordan day, and when it passes the monthly break-even. |

---

## 5. Margin calculator and break-even (§2b.4b)

`POST /api/admin/plans/margin-check`
```json
{ "priceJod": 40, "scans": 100, "months": null,
  "pointsRule": { "mode": "Tiered", "tiers": [ {"fromJod":1,"toJod":3,"points":100}, {"fromJod":3,"toJod":5,"points":300},
                                               {"fromJod":5,"toJod":10,"points":500}, {"fromJod":10,"toJod":null,"points":800} ] },
  "amountMix": [ {"fromJod":1,"percent":30}, {"fromJod":3,"percent":40}, {"fromJod":5,"percent":20}, {"fromJod":10,"percent":10} ],
  "targetMarginPercent": 20 }
```
→
```json
{ "avgPointsPerScan": 330, "pointValueJod": 0.001, "costPerScanJod": 0.33, "pricePerScanJod": 0.4,
  "breakEvenScans": 121, "recommendedScans": 96, "scansAboveBreakEven": -21,
  "breakEvenScansPerMonth": null, "suggestedDailySoftCap": null, "ok": true, "targetMarginPercent": 20,
  "mixSource": "manual", "amountMix": [ … ], "warning": null }
```
- Instead of `amountMix`, send `providerId` (+ `providerType`, default ChargingPoint) to use that provider's completed
  transactions of the last 90 days (`mixSource: "provider"`); no mix → equal weight per tier (`"equal"`).
- `scans > breakEvenScans` → `ok: false`, `warning: "Every scan after 121 is a loss"`.
- Unlimited (`scans: null`): `breakEvenScansPerMonth` (= default soft cap, decision 20), `suggestedDailySoftCap`;
  `expectedMonthlyScans` above it → `ok: false`.
- Points cost only (no operating cost, payment fees, campaign or promo points). Recommended scans are floored
  (96.97 → 96). Break-even is never returned to partners.

---

## 6. Owner pushes (§2.6, §2b.6) — partner app (`StationApp`), device language, inbox

| Job | When | Text (ar) | Key (sent once) |
|---|---|---|---|
| `billing-subscription-reminders` daily 07:00 UTC | 7 days before | باقتك {الاسم} بتنتهي بعد 7 أيام | `sub:{id}:exp7:{expiry}` |
| | 1 day before | باقتك {الاسم} بتنتهي بكرة — جدّد لتحافظ على المميزات | `sub:{id}:exp1:{expiry}` |
| | expiry day | انتهت باقتك — عندك فترة سماح لحد … (AfterDays) / جدّد… (manual grace) | `sub:{id}:expired:{expiry}` |
| | grace end (switched off after expiry) | توقّفت مميزات الباقة {الاسم} — جدّد لترجعها | `sub:{id}:graceEnd:{expiry}` |
| | trial: 2 days before / end | تجربتك المجانية … بتخلص بعد يومين — اشترك لتكمل / انتهت تجربة … | `trial:{id}:ending` / `:ended` |
| `billing-watch` every 10 min | activation (payment, admin, upgrade, bundle — not migration) | تفعّلت باقتك {الاسم} لحد … | `activated:{recordId}` |
| | bundle 20% / 10% / 0% | قرّبت حزمتك تخلص… / خلصت حزمة المسحات | `bundle:{id}:{20\|10\|0}` |
| | trial end | switches the engine row off (no grace, decision 17) unless a payment extended it | — |
| | admin: soft cap / break-even | تجاوز الحد اليومي للمسح / المحطة تجاوزت نقطة التعادل | `cap:{id}:{yyyyMMdd}` / `be:{id}:{yyyyMM}` |

Every event is only sent within 48 hours of happening, so deploying never pushes old news. English texts are sent to
devices whose token language is `en`.

---

## 7. Errors

| HTTP | `code` | When |
|---|---|---|
| 409 | `PLAN_KEY_TAKEN` | create plan with a used key |
| 422 | `INVALID_TIERS` | gap / overlap / last tier not open / middle tier open / fixed points ≤ 0 |
| 422 | `UNKNOWN_FEATURE` | feature key not in the catalog (`details.unknown`) |
| 422 | `PLAN_ARCHIVED` | selling / trialling an archived plan |
| 422 | `PLAN_NOT_APPLICABLE` | plan not offered for that provider type |
| 422 | `INVALID_PRICE_OPTION` | option missing / not on the plan / retired |
| 409 | `TRIAL_ALREADY_USED` | second trial of the same plan |
| 422 | `TRIAL_NOT_AVAILABLE` | plan has no trial (or partner loyalty trial on a percentage agreement) |
| 409 | `ACTIVE_SUBSCRIPTION_EXISTS` | another plan of the category is on — use upgrade |
| 422 | `SUBSCRIPTION_NOT_ACTIVE` | upgrade / cancel an ended record; handle a handled request |
| 409 | `UPGRADE_REQUEST_PENDING` | partner already has a pending request |
| 409 | `bundleExhausted` | new partner code with 0 scans left |
| 400 | validation | field errors (`errors`) as usual |
| 403 | — | worker or another owner on billing endpoints |

All messages are Arabic or English by `Accept-Language`.

---

## 8. Settings (AppSetting) and seed data

| Key | Default | |
|---|---|---|
| `Billing.PointsPerJod` | 1000 (seeded) | decision 10 — **confirm**: all margin maths use it. Without the row: default conversion rate, then 1000 |
| `Billing.TargetMarginPercent` | 20 | decision 19 |
| `Billing.BundleMinimumTransactionAmount` | 2 | decision 12 |
| `Subscription.GraceMode` / `GraceDays` | unchanged | decision 4 |

Seeded plans (only if the key does not exist):

| key | category | price options | features / rule | trial |
|---|---|---|---|---|
| `free` | Subscription | — | verifiedBadge, editInfo | — |
| `premium` | Subscription (`StationPremium,ServiceProviderPremium`) | 1/3/6/12 months = 20/60/120/240 | + appearFirst, images, complaints, analytics, workers, biometrics | 14 days |
| `connect` | Subscription (`OcppConnect`, ChargingPoint only, includes premium) | 40/120/240/480 | + connect | none |
| `loyalty_bundle_400` | Loyalty | 400 scans / 40 JOD, no time limit | Tiered: <5 → 20, 5–20 → 50, 20+ → 100 (decision 9) | none |
| `loyalty_unlimited` | Loyalty | 1 month 60, 3 months 160, unlimited | Tiered: 1–3 → 10, 3–5 → 20, 5–10 → 40, 10+ → 70 (decision 15); soft cap 150/day | 14 days + 50 scans |

Notification types: `billing_subscription`, `billing_bundle`, `billing_upgrade_request`, `billing_admin_alert`.

---

## 9. Migration notes

- Non-destructive: `Subscription` / `Payment` rows are untouched. Each non-void payment of `StationPremium`,
  `ServiceProviderPremium`, `OcppConnect` is copied into a `PlanSubscription` (`source: migration`, plan premium /
  connect, period = the payment's period, snapshot `{months, priceJod}`); a Subscription row with no payment gets one
  record for its period. Re-running copies nothing twice. Banners are not partner plans and are not copied.
- Every existing agreement gets `BillingModel = 'percentage'` → creating, confirming, expiring and cancelling codes,
  commission reservation and settlements behave exactly as before.
- Nothing switches anyone off: the existing grace rules decide; trials are the only thing the new job ends (no grace,
  decision 17), and only periods that were a trial and were not paid.
- Legacy `POST /api/subscriptions/payments` for `OcppConnect` does **not** extend `StationPremium` (unchanged
  behaviour). The new admin `POST /api/admin/subscriptions` with plan `connect` (and upgrades / trials) does extend
  the included premium row to the same end, so map placement (StationTypeId) follows.

---

## 10. Dev test steps (§8 of the document)

0. Apply `Scripts/Sprint5_PartnerBilling.sql` on dev; check the PRINT lines (5 plans, notification types, migrated records).
1. **My plan**: `GET /api/provider/subscription?providerType=ChargingPoint&providerId=37` as the owner → `tier: connect`,
   correct `expiresAt`; `GET …/subscription/payments` lists `RCP-2026-000006`; its `receiptDownloadPath` downloads as the owner.
   As a worker of station 37 → 403.
2. **features**: `PUT /api/admin/plans/feature-matrix` removing `analytics` from connect → `features[]` no longer has it
   (no app release). Put it back; `GET /api/admin/plans/{id}/history` shows both edits.
3. **Upgrade request**: `POST /api/provider/upgrade-requests { tier: "premium", months: 3 }` → `Pending`; a second → 409;
   it appears in `GET /api/admin/upgrade-requests`; record the payment (`POST /api/subscriptions/payments`,
   `entityType: StationPremium`, `planMonths: 3`) → request `Fulfilled`, subscription extended, activation push within 10 min.
4. **Bundle**:
   - `POST /api/admin/plans` a Loyalty plan `test_bundle_3` with option `{ months: null, priceJod: 3, scans: 3 }` and a Fixed rule (10 points).
   - `PATCH /api/partners/{agreementId}/billing-model { billingModel: "bundle" }`, `POST /api/partners/{agreementId}/bundles`
     → wallet shows a `bundlePurchase` of −3.
   - Create 4 codes (`CreateTransaction`), confirm 3 → 10 points each; the 4th previews `partnerQuotaReached` and confirms
     with 0 points; a 5th code → 409 `bundleExhausted`. Pushes at 20/10/0 % arrive once.
   - A code left to expire consumes nothing (`scansUsed` unchanged).
   - Buy again → `scansIncluded = 3 + leftover`, previous record `Renewed`.
   - Admin reverse one completed transaction → `scansUsed` −1.
5. **No-points reasons**: below the first tier (`belowMinimumAmount` + minimum), bundle at 0 (`partnerQuotaReached`),
   0% partner (`partnerInactive`).
6. **iOS**: client-side — prices and upgrade / trial buttons hidden; the API returns the same data.
7. **Margin**: `POST /api/admin/plans/margin-check` with §5 body → 121 / 96; 200 scans → warning.
8. **Trial**: `POST /api/provider/trials { planId: premium }` on a station with no premium → premium on for 14 days;
   again → 409 `TRIAL_ALREADY_USED`; `trialAvailable.premium` false.
