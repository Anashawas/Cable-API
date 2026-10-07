# Cable Partner — Endpoint Alignment: Mobile vs Web

**Date:** 2026-08-16
**Audience:** Cable Partner mobile team
**Purpose:** Reconcile the mobile app's endpoint list with what the API actually exposes, and list the endpoints the new Partner Web portal uses that mobile does not, so both clients behave identically.

---

## 1. Summary

The Partner Web portal was built against the same API as the mobile app. Auditing the
endpoint list supplied by the mobile team against the API source produced three groups:

| Group | Count | Action for mobile |
|---|---|---|
| Incorrect entries in the mobile list | 6 | Fix — some of these are live bugs |
| Endpoints web uses that mobile does not | 3 | Adopt — one of them is a live bug on mobile |
| Endpoints mobile uses that web does not | 3 | None — intentional, see §5 |

---

## 2. Corrections to the mobile endpoint list

These six entries do not match the API. Verified against route definitions in
`WebApi/Routes/`.

| # | Listed as | Actual | Impact |
|---|---|---|---|
| 1 | `POST /api/provider/favorites/notifications/{id}/approve` | **`PUT`** | Approve fails if sent as POST |
| 2 | `POST /api/provider/favorites/notifications/{id}/reject` | **`PUT`** | Reject fails if sent as POST |
| 3 | `GET /api/provider/charging-points/update-requests/my-requests` | **`POST`** — filter travels in the body | Listing fails if sent as GET |
| 4 | `POST /api/provider/charging-points/update-requests/{id}/cancel` | **`DELETE`** | Cancel fails if sent as POST |
| 5 | `POST /api/offers/provider/ConfirmTransaction/{transactionId}` | **Does not exist** — see §2.1 | Dead call |
| 6 | `GET /api/offers/provider/LookupTransaction/{code}` | **Does not exist** — see §2.2 | Dead call |

Additionally: `GET /api/offers/provider/GetTransactionById/{transactionId}` does not exist
under `/api/offers`. The real path is:

```
GET /api/partners/provider/GetTransactionById/{id}
```

`/api/offers/*` (loyalty offers) and `/api/partners/*` (partner agreements) are two
separate systems. The mobile list merges them.

### 2.1 `ConfirmTransaction`

`Application/Offers/Commands/ConfirmOfferTransaction/ConfirmOfferTransactionCommand.cs`
exists and is imported by `WebApi/Routes/OfferRoutes.cs`, but **no route is mapped to it**.
It is unreachable code.

**Question for the backend team:** was this meant to be wired up, or should it be deleted?

### 2.2 The redemption flow that actually exists

There is no provider-side "confirm" step. The working flow is two steps:

1. `POST /api/offers/provider/CreateTransaction` — provider picks an offer, API returns a
   CBL code to display to the customer.
2. The **customer** scans that code in the consumer app:
   `POST /api/offers/ScanOfferCode` — deducts the customer's points and completes the
   transaction in one call.

The provider may void an unused code before it is scanned:

```
POST /api/offers/provider/CancelTransaction/{id}
```

Once scanned, the transaction is `Completed` and the cancel is rejected.

---

## 3. Endpoints web uses that mobile does not — please adopt

### 3.1 `GET .../auto-approve-worker-notifications` — fixes a live bug

```
GET /api/provider/favorites/{providerType}/{providerId}/auto-approve-worker-notifications
```

Response:

```json
{ "providerType": "ChargingPoint", "providerId": 37, "enabled": false }
```

**Why this matters.** The `PUT` setter has existed since F4 shipped, but nothing exposed
the current value. Any client with that toggle can change the setting but cannot read it,
so the switch shown to the user is a guess — wrong after a reinstall, and wrong on a
second device.

This endpoint was **added on 2026-08-16** specifically to close that gap. It ships with the
next WebApi publish.

Access is owner/admin only, matching the setter — a worker receives `403`. Treat `403` as
"hide the toggle", not as an error: a worker must not be able to unlock their own sends.

### 3.2 `GET /api/offers/GetWalletHistory` — wallet ledger

Returns every wallet movement, not just the balance:

```json
{
  "id": 1, "providerType": "ChargingPoint", "providerId": 37,
  "transactionType": 1, "amount": 50.0, "balanceAfter": 50.0,
  "referenceType": null, "referenceId": null, "note": null,
  "recordedByUserName": "Admin", "createdAt": "2026-08-01T10:00:00"
}
```

`transactionType` is `WalletTransactionType`:

| Value | Meaning | Direction |
|---|---|---|
| 1 | Deposit | credit |
| 2 | SettlementDeduction | debit |
| 3 | Refund | credit |
| 4 | Adjustment | debit |
| 5 | CommissionDeduction | debit |
| 6 | CommissionRefund | credit |
| 7 | OfferPaymentCredit | credit |
| 8 | OfferPaymentRefund | debit |

Supports `page` / `pageSize`. **Note:** returns a bare array when empty, and a paged
envelope otherwise — handle both.

### 3.3 `PUT /api/offers/DeactivateOffer/{id}` — switch off a live offer

Web allows the partner to deactivate an approved, active offer. Mobile appears to be
read-only on offers. Only meaningful when `isActive === true` and
`approvalStatus === 2` (Approved).

---

## 4. Behavioural alignment — station update requests

This is not an endpoint difference but it affects your admin review queue.

**Send only the fields the user actually changed.**

`POST /api/provider/charging-points/submit-update-request/{chargingPointId}`

The handler snapshots the station at submit time and diffs the payload against it. Any
field present in the payload is recorded as a proposed change — **including a field
re-sent with its current, unchanged value**.

If the whole form is posted every time, an admin reviewing the request sees a wall of
no-op edits with the real change buried among them, and every request trips more
`riskFlags` than it should.

Web builds the payload by comparing current form values against the values loaded into the
form, and omits anything that did not change. Whitespace-only edits are treated as no
change; plug type IDs are compared as a set, not an ordered list.

### Risk flags the API can return

`owner_contact_changed`, `contact_phone_changed`, `name_changed`, `location_moved`.

Map all four. An unmapped flag should fall back to showing the raw string rather than
being hidden — a warning the partner cannot see is worse than an ugly label.

---

## 5. Endpoints mobile uses that web does not — no action needed

| Endpoint | Why web omits it |
|---|---|
| `POST /api/offers/provider/CreateTransaction` | QR generation is a point-of-sale action. Staff serving a customer use the phone; the web portal is for management. Web keeps the read side (`GetProviderTransactions`) and the void action. |
| `GET /api/offers/provider/GetTransactionById/{id}` | Same reason. Also note the wrong path — see §2. |
| `POST`/`DELETE /api/charging-points/{id}/view-image` | Premium home-card promo image. Administered by the Cable team, not partners. |

### Redundant calls mobile can drop

```
GET /api/provider/charging-points/my
GET /api/provider/service-providers/my
```

`GET /api/provider/my-assets` returns both lists in a single call. Web uses only
`my-assets`. Dropping these saves mobile two requests on every asset refresh.

---

## 6. Session behaviour — read this before testing

The API keeps **three independent session slots per user account**, selected by the
`X-Client-App` request header:

| Header value | Slot | Used by |
|---|---|---|
| *(absent)* or `consumer` | `SecurityStamp` | Cable consumer app |
| `provider` | `ProviderSecurityStamp` | Partner **mobile** app |
| `provider-web` | `ProviderWebSecurityStamp` | Partner **web** portal |

**Send `X-Client-App: provider` on every request from the partner mobile app.**

Tokens issued before this shipped carry no `app` claim and are validated against the
consumer slot, so existing sessions keep working across the deploy.

Consequence: signing into the web portal no longer signs the partner out of the mobile
app, and vice versa. Two logins **into the same slot** still evict each other — that is
the intended single-device rule, now applied per app rather than globally.

---

## 7. Phone number format

Phone fields are returned in dial-ready **E.164** (`+9627...`) so `tel:` links work
without a client release. **Storage is unchanged** — the database still holds `9627...`.

Applies to: charging points, service providers, emergency services, and — from the next
publish — **the worker endpoint** (`GET /api/workers`).

Do not compare a returned phone against a locally stored value with string equality, and
do not re-post a returned phone as-is into a field the API expects in storage format.

WhatsApp numbers use a different format: bare digits with country code, no `+` and no
leading zero, as `wa.me` requires.

---

## 8. Checklist for mobile

- [ ] Fix the four wrong HTTP verbs (§2, rows 1–4)
- [ ] Remove `ConfirmTransaction` and `LookupTransaction` calls (§2.1, §2.2)
- [ ] Correct `GetTransactionById` to the `/api/partners/` path
- [ ] Adopt `GET .../auto-approve-worker-notifications` and stop guessing the toggle state
- [ ] Add wallet ledger via `GetWalletHistory`
- [ ] Add offer deactivation via `DeactivateOffer/{id}`
- [ ] Submit only changed fields on station update requests (§4)
- [ ] Map all four risk flags, with raw-string fallback (§4)
- [ ] Send `X-Client-App: provider` on every request (§6)
- [ ] Verify no string comparison against returned E.164 phone values (§7)
- [ ] Drop `charging-points/my` and `service-providers/my` in favour of `my-assets` (§5)

## 9. Questions back to the backend team

1. Should `ConfirmOfferTransactionCommand` be routed, or deleted?
2. Is premium `view-image` management ever intended to be partner-facing?
