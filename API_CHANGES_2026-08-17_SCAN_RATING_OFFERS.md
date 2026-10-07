# API Changes — 2026-08-17

Scan confirmation · driver rating · provider offers · provider favorites · best-match offer

All five items below are implemented, built, and **verified end-to-end against the dev
database**. Every example in this document is a real request/response captured during
that run — not illustrative.

**Base URL (dev):** as configured per environment
**Auth:** `Authorization: Bearer <accessToken>` unless stated otherwise

---

## Summary

| # | Requirement | Endpoint | Status |
|---|---|---|---|
| 1 | اضافة تاكيد لل Scan | `GET /api/partners/PreviewPartnerCode` | New |
| 2 | اضافة تقييم للمستخدم بعد ما يشحن | `POST /api/rate/RateUser` + 2 reads | New |
| 3 | لازم بالبروفايدر يرجع العروض | `offers[]` on every service-provider response | Changed |
| 4 | اضافة ك مفضلة للبروفايدر | `GET /api/service-providers/CheckIsFavorite/{id}` | New |
| 5 | المدينة + النقاط → أقرب عرض | `GET /api/offers/GetBestMatchOffer` | New |

### Action required before deploying

1. **Run `Scripts/UserRating_Phase1.sql`** — creates `dbo.UserRate`. Item 2 returns 500 without it.
   Already applied to dev. Idempotent and additive; safe to re-run.
2. **One breaking change** — see [§6](#6-breaking-changes).

---

## 1. Scan confirmation

The scan used to be one call that completed the transaction and awarded points
immediately. It is now **preview → user confirms → scan**.

`ScanPartnerCode` is **unchanged** and still completes and awards — it is now simply
the "confirm" step. Existing clients keep working; nothing breaks if you do not adopt
the preview.

### `GET /api/partners/PreviewPartnerCode?code={code}` 🔒

Read-only. Resolves a scanned code to the details the user confirms against. Awards
nothing, completes nothing. Abandoning the sheet leaves the code scannable until it
expires.

**Response**

```json
{
  "transactionId": 8,
  "transactionCode": "PTR-F544G5",
  "providerName": "Power Station",
  "providerType": "ChargingPoint",
  "providerId": 179,
  "transactionAmount": 10.500,
  "currencyCode": "JOD",
  "commissionAmount": 1.050,
  "pointsToBeAwarded": 26,
  "codeExpiresAt": "2026-08-17T23:53:23.940",
  "expiresInSeconds": 58,
  "canConfirm": true,
  "blockReason": null
}
```

| Field | Use |
|---|---|
| `pointsToBeAwarded` | Exactly what the user will receive — safe to show as a promise |
| `expiresInSeconds` | Drives the countdown on the confirmation sheet |
| `canConfirm` / `blockReason` | `false` + a message when the user's loyalty account is blocked. Surface it and disable Confirm — otherwise they tap Confirm and the whole scan rolls back |

**Errors:** `404` unknown code or no longer awaiting a scan · `400` expired · `401` no token

**Verified:** repeated previews returned `expiresInSeconds` 58 → 57 with the code still
`Initiated`, then `ScanPartnerCode` completed it and awarded 26 points.

---

## 2. Driver rating (provider rates the user)

The reverse of the existing station rating. A rating is **anchored to one completed
`PartnerTransaction`** — the only record proving a driver was actually served. This is
what stops a provider rating someone they never served, and caps it at one rating per
visit (enforced by a unique index, not application logic).

### `POST /api/rate/RateUser` 🔒 — owner / worker / admin

```json
{ "partnerTransactionId": 9, "rating": 5, "comment": "Polite driver, left the bay clean" }
```

Returns the new rating id. The driver, provider and station are all read from the
transaction — the client cannot specify who is being rated.

| Error | Cause |
|---|---|
| `403` | Caller is not the owner, an active worker, or an admin of that provider |
| `403` | Self-rating (owner is also the scanning driver) |
| `400` | Transaction not `Completed` / already rated / `rating` outside 1–5 |
| `404` | Transaction not found |

### `GET /api/rate/GetMyUserRating` 🔒 — the driver's own standing

### `GET /api/rate/GetUserRating/{userId}` 🔒 — provider or admin view

Both return the same shape:

```json
{
  "userId": 14122,
  "averageRating": 5,
  "ratingsCount": 1,
  "ratings": [{
    "id": 1, "rating": 5,
    "comment": "Polite driver, left the bay clean",
    "providerType": "ChargingPoint", "providerId": 179,
    "providerName": "Power Station",
    "partnerTransactionId": 9,
    "createdAt": "2026-08-17T23:52:51.130"
  }]
}
```

A driver nobody has rated gets `averageRating: null`, `ratingsCount: 0`, `ratings: []` —
**not** a 404.

> `GetUserRating/{userId}` is scoped: a provider may only read a driver it has a
> completed transaction with, so it cannot be used to enumerate every user's rating.
> Admins are unrestricted; reading your own id always succeeds.

### Knowing what is still rateable

`ProviderPartnerTransactionDto` gained **`isRated`** (`GetProviderTransactions`,
`GetTransactionById`). Show "Rate customer" only where `status == 2 && !isRated` —
otherwise the one-per-visit rule surfaces as an error after the user taps.

```json
{ "id": 9, "status": 2, "userId": 14122, "isRated": true }
```

---

## 3. Offers on service providers

`ServiceProviderDto` gained **`offers[]`**, populated on **all six** endpoints that
return it:

`GetAllServiceProviders` · `GetServiceProviderById` · `GetByCategory` · `GetNearby` ·
`GetMyFavorites` · `GetMyServiceProviders`

```json
{
  "id": 2, "name": "test wash",
  "offers": [{
    "id": 3, "title": "english ", "titleAr": "وصف ",
    "description": "english ", "descriptionAr": "الوصف",
    "pointsCost": 200, "pointsPriceValue": 4.0,
    "monetaryValue": 3.0, "currencyCode": "JOD",
    "imageUrl": null,
    "validFrom": "2026-07-01T17:25:00.000",
    "validTo": "2028-01-01T02:59:59.000"
  }]
}
```

Only **currently redeemable** offers appear: approved, active, inside the validity
window, and not at their global use cap. A provider with none returns `"offers": []`.
Sorted cheapest first. Loaded in one batched query per request — no N+1.

> `offers[]` is backed by the real `ProviderOffer` table. The older `hasOffer` /
> `offerDescription` fields are free text on the provider record and are **not**
> related — prefer `offers[]`.

---

## 4. Favorites parity for service providers

Stations already had a check endpoint; service providers did not, so a detail screen
had to pull the whole favorites list to render a heart.

### `GET /api/service-providers/CheckIsFavorite/{serviceProviderId}` 🔒

Returns the **same shape** as the station check, so one model and one widget serve both:

```json
{ "isFavorite": true, "favoriteId": 1 }
```

Favorites are now fully symmetric:

| | Station | Service Provider |
|---|---|---|
| Add | `POST /api/favorites/{id}` | `POST /api/service-providers/AddToFavorites/{id}` |
| Remove | `DELETE /api/favorites/{id}` | `DELETE /api/service-providers/RemoveFromFavorites/{id}` |
| List | `GET /api/favorites` | `GET /api/service-providers/GetMyFavorites` |
| **Check** | `GET /api/favorites/check/{id}` | `GET /api/service-providers/CheckIsFavorite/{id}` |

**Verified:** add → check `{true, 1}` → remove → check `{false, null}`.

---

## 5. Best-match offer for a city + points balance

### `GET /api/offers/GetBestMatchOffer?city={city}&points={points}&alternativesLimit={n}` 🔒

| Param | Required | Notes |
|---|---|---|
| `city` | yes | Arabic or English — `عمّان`, `Amman`, `al zarqa` all resolve |
| `points` | yes | The user's balance. `0` is valid; negative returns `400` |
| `alternativesLimit` | no | Default 5 |

**Best match = the most expensive offer the balance already covers** — the user gets the
most value, not the cheapest item. If nothing is affordable, the **nearest offer above**
the balance is returned with `isAffordable: false` and the shortfall, so the app has a
target to show instead of an empty screen.

**Affordable** — `city=Maan&points=500`:

```json
{
  "requestedCity": "Maan", "resolvedCity": "Maan", "points": 500,
  "bestMatch": {
    "offer": { "id": 1, "title": "Congs for charging", "titleAr": "مبروك عليك الشحنة",
               "pointsCost": 485, "monetaryValue": 8.000, "currencyCode": "JOD" },
    "providerType": "ChargingPoint", "providerId": 219,
    "providerName": "EV Station - Test", "cityName": "Maan",
    "isAffordable": true, "pointsDifference": 15
  },
  "alternatives": []
}
```

**Not yet affordable** — `city=Maan&points=100`: same offer, `"isAffordable": false`,
`"pointsDifference": -385` (short by 385).

**Arabic** — `city=معان` → `"resolvedCity": "Maan"`, identical match.

**Unrecognised city** — `city=Atlantis` → `200` with `"resolvedCity": null` and an empty
result. Not an error: the question was reasonable, the answer is "nothing here".

Covers **both** charging points and service providers. Recognised cities are the 12
canonical Jordanian ones: Amman, Zarqa, Irbid, Salt, Mafraq, Madaba, Jerash, Ajloun,
Karak, Tafilah, Maan, Aqaba.

---

## 6. Breaking changes

### `GET /api/favorites/check/{chargingPointId}` now requires authentication

It was anonymous and returned `{"isFavorite": false}` when no user was attached. An
expired token therefore produced a cheerful "not favorited" instead of a `401` — the
heart rendered empty on a station the user *had* favorited, with nothing signalling the
dead session.

It now returns **`401`** without a valid token.

**Client action:** ensure this is called with a token and that `401` flows into your
normal refresh/re-login path. The anonymous response carried no information (always
`false`), so nothing real is lost.

### Additive (safe to ignore)

- `ServiceProviderDto.offers[]`
- `ProviderPartnerTransactionDto.isRated`

Both are new JSON fields on existing responses. Existing clients ignore them.

---

## 7. Test evidence

Run against dev on 2026-08-17. All passed.

| Check | Result |
|---|---|
| Preview returns details, countdown ticks, code stays `Initiated` | ✅ |
| Preview unknown code | ✅ 404 |
| `ScanPartnerCode` after preview completes + awards 26 pts | ✅ |
| Rate driver as provider owner | ✅ 200 |
| Duplicate rating on same transaction | ✅ 400 "already been rated" |
| Rating by an unrelated user | ✅ 403 |
| Rating outside 1–5 | ✅ 400 |
| Driver reads own rating · provider reads it | ✅ both |
| Unrelated user reads a driver they never served | ✅ 403 |
| `isRated` true/false on the right transactions | ✅ |
| Service-provider list + detail carry `offers[]` | ✅ |
| Favorites add → check → remove → check | ✅ |
| Best match affordable (+15) and shortfall (−385) | ✅ |
| Arabic city resolution · unknown city · negative points · no token | ✅ |
| Station favorites check without token | ✅ 401 |

### Dev environment notes

- Offer ids 1–3 were **activated** (`IsActive = 1`, `ValidTo = 2027-12-31`) so items 3
  and 5 are testable — they were previously inactive and expired, which is why both
  endpoints correctly returned empty. Revert with:
  ```sql
  UPDATE dbo.ProviderOffer SET IsActive = 0 WHERE Id IN (1,2,3);
  ```
- Dev has test OTP enabled: any phone number, code `000000`. Rate-limited to one
  request per minute per number.
- Test data left on dev: partner transactions 8–9, one `UserRate` row, user 14122.
