# Sprint 5 — Live charging session, part A (backend)

Source: `LIVE_CHARGING_SESSION.md` §4.2, 4.4, 4.7, 4.8, 4.9, 11.c, 13.4 and N-5, with the default decisions of §8.

| # | What | Where |
|---|---|---|
| 4.2 / 11.c | Proximity rule on app start; position + distance stored on the session | `POST /api/users/me/ocpp-sessions/start` |
| 4.4 | A station's own time-of-use tariff, used for every price of its sessions | `/api/admin/ocpp/stations/{cp}/tariff`, `/api/provider/charging-points/{cp}/tariff`, `/api/charging-points/{id}/tariff` |
| 4.7 | Automatic points when a driver's session ends (off by default) | Hangfire job + `pointsAwarded` on the session DTOs and the finished push |
| 4.8 | Monthly summary | `GET /api/users/me/ocpp-sessions/summary?month=YYYY-MM` |
| 4.9 | ~~Link an RFID card to my account~~ | **Removed (Oct 28):** cards belong to stations and their staff, not drivers |
| 13.4 | The driver's car on the session; `reportsSoc` learning | `carId` on start, `car` on the session DTOs, `reportsSoc` on the user's cars |
| N-5 | "Your car has finished charging — could you unplug it?" to the driver | alert job (`check-ocpp-alerts`) |

**SQL:** `Scripts/Sprint5_LiveCharging_A.sql` (idempotent). **Deploy order:** API first, then Cable.Ocpp — Cable.Ocpp enqueues a new Hangfire job type (`IOcppSessionPointsService`) that only the new API can run.

All user-facing messages follow `Accept-Language` (Arabic by default). Pushes use the device token's language.

---

## 1. Start with the phone's position (4.2, 11.c) and the car (13.4)

```http
POST /api/users/me/ocpp-sessions/start
Authorization: Bearer …
Accept-Language: ar

{
  "chargingPointId": 37, "chargerId": 8, "connectorId": 2,
  "chargeLimitKind": "Jod", "chargeLimitValue": 7,
  "lat": 31.95391, "lng": 35.91062, "accuracyMeters": 12,
  "carId": 451
}
```

`200`:

```jsonc
{
  "commandId": 134, "accepted": true, "status": "Answered", "resultStatus": "Accepted",
  "idTag": "CBL-U13795", "message": "The charger accepted. …",
  "distanceMeters": 42,     // phone → station; null when either position is unknown
  "carId": 451              // the car recorded on the session (chosen, or your only car); null = none
}
```

**The rule:** reject when `distance − min(accuracyMeters, 100) > Ocpp.AppStart.MaxDistanceMeters` (300). A station without coordinates (0 / 0) is always allowed. The check runs on the server, after "does this station offer app charging" and before the plug checks.

| Status | `code` | When | Extra fields |
|---|---|---|---|
| 422 | `LOCATION_REQUIRED` | no `lat` / `lng` and `Ocpp.AppStart.RequireLocation` = true | `maxMeters` |
| 422 | `TOO_FAR_FROM_STATION` | farther than the radius | `distanceMeters`, `maxMeters` |
| 400 | — | `lat` without `lng` (or the reverse), out-of-range values, a `carId` that is not yours | |
| 409 | `sessionAlreadyOpen` | unchanged | |

```jsonc
// 422
{
  "type": "https://tools.ietf.org/html/rfc4918#section-11.2", "status": 422,
  "detail": "لازم تكون عند المحطة لتبدأ الشحن (أنت على بعد 1240 م، والحد 300 م).",
  "code": "TOO_FAR_FROM_STATION", "distanceMeters": 1240, "maxMeters": 300
}
```

**Car:** `carId` is a `UserCar` id — `GET /api/carmanagement/GetAllUserCars` now returns it as `carModels[].plugTypes[].userCarId`. No `carId` = the driver's only car when they have exactly one, else none (the model has no "default car" flag).

**How it reaches the session:** `RemoteStartTransaction` only opens the session later (Cable.Ocpp `StartTransaction`), so the position, distance and car are parked on the plug (`OcppConnector.PendingStart*`, `PendingCarId`), like the pending charge limit. That driver's App session on that plug within 10 min takes them into `OcppTransaction.StartLat / StartLng / StartAccuracyMeters / StartDistanceMeters / CarId`.

## 2. A station's real price (4.4)

A station can have its own time-of-use tariff — the same windows shape as the national one (`PUT /api/admin/pricing/tou`). When it has one, it prices **everything** for that station: the live cost so far (`/current`, session details, partner live view), the price stored at stop, the price-ocpp-sessions catch-up, the JOD charge limit in Cable.Ocpp, the finished push. No own tariff = the national tariff, as before. `OcppTransaction.TariffChargingPointId` records which one priced a session.

### Public (driver app)

```http
GET /api/charging-points/37/tariff
```

```jsonc
{
  "chargingPointId": 37,
  "source": "station",            // station | national
  "hasOwnTariff": true,
  "tariff": { "version": 2, "effectiveFrom": "…", "timezone": "Asia/Amman", "currency": "JOD", "unit": "fils/kWh",
              "windows": [ { "key": "offPeak", "startMin": 300, "endMin": 1020, "tier": "offPeak", "priceFils": 175, "nameEn": "Off-peak", "nameAr": "خارج الذروة" }, … ] },
  "now": {
    "key": "offPeak", "tier": "offPeak", "nameEn": "Off-peak", "nameAr": "خارج الذروة", "priceFils": 175,
    "endsAt": "2026-10-28T17:00:00",                     // Jordan time (JSON converter)
    "next": { "key": "peak", "priceFils": 213, "startMin": 1020, … },
    "textAr": "الآن: خارج الذروة · 175 فلس/kWh",
    "textEn": "Now: Off-peak · 175 fils/kWh"
  }
}
```

### Owner (read-only) — `GET /api/provider/charging-points/{cp}/tariff` (ConnectView). Same body.

### Admin

| Method | Path | Body |
|---|---|---|
| GET | `/api/admin/ocpp/stations/{cp}/tariff` | — |
| PUT | `/api/admin/ocpp/stations/{cp}/tariff` | `{ "windows": [ …same as the national tariff… ], "note": "Station price list Oct 2026" }` |
| DELETE | `/api/admin/ocpp/stations/{cp}/tariff` | — (back to the national tariff) |

PUT validation = the national one: exactly 1440 minutes, unique keys, tier `offPeak | partial | peak`, 1–9999 fils. Each PUT is a new version for that station; the previous one is kept inactive. All three return the body above.

### Driver session DTOs

`GET /api/users/me/ocpp-sessions/current` now also returns, while open: `costFils` / `costJod` / `price` **so far** (at the station's tariff), `currentRateFils`, `currentWindowNameEn/Ar`, `tariffSource`. History and details carry `tariffSource` once priced.

## 3. Automatic points when a session ends (4.7)

Behind **`Ocpp.AutoPoints.Enabled`** (default **false** — nothing changes until an admin switches it on). Only sessions that **end after the switch was turned on** count (the setting row's `ModifiedAt`), and never older than 2 days.

**Which sessions:** `StartedByUserId` set and `StartSource` App or Card (a card linked to a user). Never Operator / staff starts, rejected starts, orphan stops, or zero-cost sessions.

**How:** exactly like a partner QR scan for the session's cost (`costJod`), using the station's active `PartnerAgreement`:

- reward % × points rate (the agreement's, else the default rate) → base points, floored;
- commission % off the station wallet (`ProviderWalletTransaction` CommissionDeduction), refused (skipped) past the credit limit;
- welcome bonus / campaign boosts via `ILoyaltyBoostService` (the same resolution as a scan);
- settlement via `ISettlementService`;
- points via `ILoyaltyPointService.AwardPointsFromOfferAsync`;
- recorded as a **completed `PartnerTransaction`, code `OCPP-{transactionId}`**, no staff (`ConfirmedByUserId` null) — so wallet, settlement and reports include it.

**No points** (stored in `OcppTransaction.PointsSkipReason`, `pointsAwarded = 0`): `noAgreement`, `userBlocked` (loyalty-blocked user: no points and no commission), `stationBlocked`, `belowMinimumAmount`, `noPoints` (0 points at this rate), `creditLimit`, `zeroCost`, `notDriverSession`.

**Idempotent:** the session row is locked (`UPDLOCK`), `PointsProcessedAt` marks "decided", and the unique transaction code is the backstop.

**When it runs (API Hangfire):**
1. Cable.Ocpp, on the driver's `StopTransaction` (after pricing): enqueues `IOcppSessionPointsService.AwardAfterStopAsync`, then chains the "charging finished" push after it, so the push can say "+330 نقطة / +330 points" and carries `data.pointsAwarded`.
2. Catch-up: the `price-ocpp-sessions` job (every minute) awards priced driver sessions still undecided.

`pointsAwarded` is on `MySessionDto` (current / history) and `SessionLiveDto` (details): `null` = not decided (or the feature is off), `0` = decided, none, `> 0` = awarded.

## 4. Monthly summary (4.8)

```http
GET /api/users/me/ocpp-sessions/summary?month=2026-10
```

```json
{ "month": "2026-10", "sessions": 7, "energyKwh": 142.315, "costJod": 27.612, "pointsAwarded": 330 }
```

Jordan calendar month; `month` omitted = the current month; bad format → 400. Same sessions as the history list (no rejected / orphan rows), by start time; a session still running counts in `sessions`, its kWh / JOD once closed.

The history list takes the same filter (leftovers): `GET /api/users/me/ocpp-sessions?month=2026-10&page=1&pageSize=20` returns only the sessions started in that Jordan month (exactly the set the summary counts), still paged and newest first. Without `month` it is unchanged; a malformed month → 400.

## 5. RFID cards on my account (4.9) — removed

Not built: RFID cards belong to the station (owner / workers, managed as the station's accepted cards), not to drivers. The driver and admin card-link endpoints were removed.

## 6. The driver's car (13.4)

- Session DTOs (`/current`, history, details) return `car: { id, name, reportsSoc }` — `name` = car type + model, e.g. "Hyundai Ioniq 5".
- **Learning:** when a session with a car closes and it reported a battery % (`SocStartPercent` / `SocEndPercent`), Cable.Ocpp marks the car's model `CarModel.ReportsSoc = true`.
- `GET /api/carmanagement/GetAllUserCars` → `carModels[].reportsSoc` (`true` = seen reporting; `null` = not seen yet) and `plugTypes[].userCarId`. The app hides the battery-% limit on AC chargers and when `reportsSoc` is not `true`.

## 7. "Your car has finished — could you unplug it?" (N-5)

The alert job (`check-ocpp-alerts`, every 5 min) already finds plugs in Finishing / SuspendedEV past the parked threshold. When the latest session on the plug is a **driver's session** (same rule as the driver session pushes: app start or linked card, never staff), the driver gets — once per session (`OcppTransaction.ParkedNotifiedAt`) — in their device language:

- ar: **سيارتك خلصت شحن — ممكن تفصلها؟** · "خلّي المقبس فاضي للسائق التالي · {station} · {charger} · المقبس 2"
- en: **Your car has finished charging — could you unplug it?** · "Free the plug for the next driver · …"
- `data`: `{ "type": "ocpp_session", "status": "parked", "sessionId": "…", "chargingPointId": "…" }`, plus an inbox row (type `charging_session_parked`).

The owner's side is unchanged: stage 2 escalates to the owner / managers after `OcppLimits.ParkedEscalateAfter`; with no known driver the owner is told at once.

## 8. Settings

| Key | Default | Meaning |
|---|---|---|
| `Ocpp.AppStart.MaxDistanceMeters` | `300` | proximity radius (10 – 100000) |
| `Ocpp.AppStart.RequireLocation` | `true` | `false` lets old app builds start without a position |
| `Ocpp.AutoPoints.Enabled` | `false` | automatic points; only sessions ending after the switch count |

Admin: `GET / PUT /api/admin/ocpp/live-charging-settings` — `{ "maxDistanceMeters": 300, "requireLocation": true, "autoPointsEnabled": false, "autoPointsSince": null }` (PUT fields are optional; null = unchanged). If you switch automatic points on with SQL, also set `ModifiedAt = GETUTCDATE()`.

## 9. Dev test steps (station 37, simulator A)

1. Apply `Scripts/Sprint5_LiveCharging_A.sql` to dev; every line prints `Added …` / `SKIP …`. Run it again — all `SKIP`.
2. **Proximity:** start with no `lat/lng` → 422 `LOCATION_REQUIRED`. With a position ~2 km away → 422 `TOO_FAR_FROM_STATION` with the distance. With the station's own coordinates → 200 and `distanceMeters` ≈ 0; when the session opens, `OcppTransaction.StartLat/StartLng/StartDistanceMeters` are filled. `PUT live-charging-settings { "requireLocation": false }` → a start without a position passes.
3. **Car:** start with someone else's `carId` → 400. With your car → `/current` shows `car.name`. A simulator that sends SoC → after stop, `CarModel.ReportsSoc = 1` for that model; `GetAllUserCars` shows `reportsSoc: true`.
4. **Station tariff:** `GET /api/charging-points/37/tariff` → `source: national`. `PUT /api/admin/ocpp/stations/37/tariff` with the national windows at different prices → `source: station`. Start a session: `/current` `currentRateFils` = the station price; stop: `costFils` and `price[].priceFils` use it, `TariffChargingPointId = 37`. A 1 JOD limit stops at the station price. `DELETE` → back to national.
5. **Monthly summary:** `GET …/summary` (this month) and `?month=2026-09`; compare with the history list. `?month=2026-13` → 400.
7. **Automatic points:** give station 37 an active PartnerAgreement (e.g. 10 % reward, 5 % commission). With the switch off, finish a session → `PointsProcessedAt` stays NULL, no PartnerTransaction. `PUT live-charging-settings { "autoPointsEnabled": true }`, finish another → the push says "+N نقطة", `PartnerTransaction` `OCPP-{id}` Completed, the user's points balance and the station wallet / settlement change; re-run the job from the Hangfire dashboard → nothing changes. A staff (partner app) start → no points. Block the user from loyalty → `PointsSkipReason = userBlocked`, no partner transaction.
8. **N-5:** leave the simulator in Finishing past the parked threshold after a driver session → the driver gets the unplug push once (data `status: parked`); the owner gets the stage-2 push later.
