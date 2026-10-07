# Payment Tracking — Backend Response (Phase 1)

**Responding to:** `PAYMENT_TRACKING_BE_SPEC_AR.md` (2026-09-23)
**Scope delivered:** **Phase 1** — record offline payments, reusable payers, auto-computed expiry, proof upload, **generated branded PDF receipt**, renewals dashboard, manual + timed switch-off. Phases 2–3 (sending reminders/receipts by email, SMS, WhatsApp) are not in this drop.
**Status:** implemented, built, verified end-to-end on dev. Payloads are captured.
**Deploy prerequisite (production):** run `Scripts/Subscriptions_Phase1.sql` — renames `StationPremiumSubscription` → `Subscription`, creates `Payer` and `Payment`, migrates the existing premium row into a payment, links `BannerDuration`. Idempotent.

---

## Answers to the spec's open questions (§9)

| # | Question | Answer |
|---|---|---|
| 1 | SMS / WhatsApp provider? | **SMS: yes** (smsapril, already used for OTP). **Email: yes** (SMTP, already configured). **WhatsApp: none** — Business API + Meta template approval is the one true external dependency. |
| 2 | Early renewal — from expiry or payment date? | **From the current expiry** while the period is still running; from the payment date otherwise. The customer never loses paid days. |
| 3 | Grace period? | **Manual by default**: nothing switches off until an admin does it. Optionally `AfterDays` + N: a daily job switches it off N days after expiry. Global setting, overridable per subscription. |
| 4 | Always JOD? | Default JOD; the column exists for later. |
| 5 | Unify with premium-history now? | **Unified now.** Same table, renamed; the one existing premium row became a payment. `GET /premium-history` still works. |
| 6 | Generate the receipt or forward the proof? | **Generated, branded, bilingual PDF** (Arabic + English), stored per payment. The uploaded CliQ screenshot is kept separately as proof. |
| 7 | Email provider? | Exists (MailKit SMTP). Sending is Phase 2. |

## Model — what changed from the spec, and why

Built to reuse, not duplicate:

| Spec | Delivered |
|---|---|
| `Subscription.status` stored | **Computed** on every read from expiry + grace + the admin switch. A stored status goes stale by the next morning. |
| `Subscription.currentPaymentId` | Derived — the latest non-void payment. |
| `Payment.recordedByAdminId`, `createdAt` | Existing `createdBy` / `createdAt` audit columns. |
| `Payer.name/phone/email` copied | Optional link to the **user account**; for the station owner, name/phone/email are read from the user. Only non-user payers store their own. |
| `Payment.receiptImageUrl` | Existing upload folder + service. |
| Reminder tables, templates | Phase 2 — not created. |

`Payment` additionally stores **`periodStart` / `periodEnd`** — the days that specific payment bought — so voiding the latest payment rolls the expiry back exactly, with no guesswork.

---

## Endpoints — `/api/subscriptions` 🔒 all admin-only (receipt download also allows the owner)

### Record a payment
`POST /api/subscriptions/payments`
```json
{
  "entityType": "StationPremium",        // StationPremium | Banner | ServiceProviderPremium
  "entityId": 179,
  "planMonths": 3,                       // 1..24 — expiry is computed, never typed
  "amount": 30, "currency": "JOD",       // currency optional, defaults JOD
  "method": 1,                           // 1 = CliQ, 2 = Cash
  "paidDate": "2026-09-23T10:00:00",     // Jordan local time
  "payer": { "userAccountId": 13955, "hasWhatsApp": true },
  "note": "…", "receiptImageFileName": null
}
```
`payer` accepts **one of**: `payerId` (reuse), `userAccountId` (link the owner — name/phone come from the user), or `name`/`phone`/`email` (someone else; a matching phone reuses the existing payer).

Captured response:
```json
{ "subscriptionId": 1, "paymentId": 4, "referenceNo": "RCP-2026-000004",
  "periodStart": "2026-09-23T10:00:00", "periodEnd": "2026-12-23T10:00:00",
  "expiresAt": "2026-12-23T10:00:00",
  "receiptDownloadPath": "/api/subscriptions/payments/4/receipt", "receiptError": null }
```
`receiptError` is non-null only if the PDF failed to render — the payment is still recorded.

**Renewal rule, verified:** a 3-month payment ending `2026-12-23`, then a 1-month renewal paid on `2026-09-24` → new period **`2026-12-23 → 2027-01-23`** (from expiry, not from the payment date).

### Everything else

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/{entityType}/{entityId}` | The subscription + payment history (`404` if never paid) |
| `PUT` | `/payments/{id}` | Edit amount / method / note / payer / proof — receipt regenerated. **Dates are not editable** (void + re-record) |
| `POST` | `/payments/{id}/void` `{reason}` | Void (kept for audit); expiry rolls back to the latest remaining period; voiding the only payment switches the item off |
| `POST` | `/payments/{id}/proof` (multipart `file`) | Attach the CliQ screenshot; returns its URL |
| `GET` | `/payments/{id}/receipt` | Streams the PDF. Admin or the owner of the station / provider |
| `PATCH` | `/{subscriptionId}/switch` `{on}` | Manual off (ends premium / the banner run now) or back on |
| `PATCH` | `/{subscriptionId}/grace` `{graceMode, graceDays}` | Per-subscription override; `graceMode: null` inherits the global |
| `GET` | `/payers?q=&page=&pageSize=` | Previous payers by name/phone (matches linked users too) |
| `POST` / `PUT` | `/payers`, `/payers/{id}` | Create / edit a payer |
| `GET` | `/renewals?withinDays=30` | Dashboard: `expiringSoon`, `lapsedStillOn`, `expired`, 12-month `monthly` totals by method and plan |
| `GET` / `PUT` | `/settings/grace` | Global rule: `{graceMode: "Manual" \| "AfterDays", graceDays}` |

### Subscription shape (captured)
```json
{ "id": 1, "entityType": "StationPremium", "entityId": 179, "entityName": "Power Station",
  "planMonths": 3, "startDate": "…", "expiresAt": "2026-12-23T10:00:00",
  "status": "Active", "isOn": true, "isSwitchedOff": false,
  "graceMode": "Manual", "graceDays": 0, "graceIsOverride": false, "autoOffAt": null,
  "daysUntilExpiry": 91, "payments": [ { "id": 4, "referenceNo": "RCP-2026-000004",
     "amount": 30, "currency": "JOD", "method": 1, "methodName": "CliQ",
     "payerName": "Power Station", "payerPhone": "+962786363310",
     "receiptDownloadPath": "/api/subscriptions/payments/4/receipt", "isVoid": false, "…": "…" } ] }
```

**`status`** (computed): `Active` · `ExpiringSoon` (≤ 7 days) · `InGrace` (past expiry, still on) · `Expired` · `SwitchedOff`. **`isOn`** is what the consumer app effectively sees. With `AfterDays` grace, `autoOffAt` tells you when the job will act.

---

## What the UI should build (Phase 1)

1. **Record payment** form on the station / banner page: plan (1/3/6), amount, method, paid date, payer picker (`GET /payers?q=` — prefill from the last payment's payer, or the owner via `userAccountId`), optional proof upload after save.
2. **Payment history** per item from `GET /{entityType}/{entityId}`, with download-receipt and void actions.
3. **Renewals dashboard** from `GET /renewals` — three lists + monthly totals.
4. **Switch off / on** and the grace controls (global under settings, per-subscription on the item).

## Also changed

- **`PATCH /api/charging-points/{id}/premium`** (the current admin form) still works — it now records through the same path (payer = station owner, method Cash, explicit expiry) and **is now admin-only**; it previously had no authorization at all.
- **Station premium sync**: recording a payment sets the station's premium dates and type; switching off clears them, so `GetNearestPremium` etc. keep working untouched.
- **Banner**: a payment creates/extends a `BannerDuration` linked to the subscription; switching off truncates it.
- **ServiceProviderPremium**: accepted and tracked, but the provider entity has no premium fields yet, so nothing is toggled on it.

## Not in this drop (Phase 2 / 3)
Reminder templates and config, the reminder job, sending receipts by email/SMS, WhatsApp. Email and SMS providers already exist, so Phase 2 is mostly templates; WhatsApp needs the Meta setup first.
