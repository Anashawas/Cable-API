# Partner App Adoption — Backend Response

**Responding to:** `PARTNER_APP_ADOPTION_BE_SPEC_AR.md` (2026-09-23)
**Status:** implemented, built, verified end-to-end on dev. Payloads below are captured from that run.
**Deploy prerequisite (production):** run `Scripts/PartnerAdoption_Phase1.sql` (two nullable columns on `UserAccount`; additive).

---

## The one endpoint

### `GET /api/admin/partner-adoption?activeWindowDays=30` 🔒 admin only

Returns the funnel totals plus one row per station. It is **not** on `GetAllChargingPoints` as the spec suggested, because that endpoint is public (the consumer app's station list) and owners' login times must not be exposed there.

```json
{
  "funnel": {
    "allStations": 214, "noOwner": 0, "defaultOwner": 190,
    "withRealOwner": 24, "distinctRealOwners": 22,
    "ownerUsedPartnerApp": 14, "activeInWindow": 16
  },
  "activeWindowDays": 30,
  "stations": [{
    "chargingPointId": 179, "name": "Power Station", "cityName": "Irbid",
    "ownerId": 13955, "ownerName": "Power Station", "ownerPhone": "+962786363310",
    "isDefaultOwner": false,
    "ownerUsesPartnerApp": true, "ownerUsesPartnerWeb": false,
    "ownerLastLoginAt": "2026-09-23T14:47:23.943",
    "ownerLastSeenAt": "2026-09-23T14:47:32.203",
    "lastPartnerActivityAt": "2026-09-22T18:55:51.697",
    "stage": "Active"
  }]
}
```
*(funnel numbers above are production's as of 2026-09-23; the station row is from dev)*

## Field by field — and the answer to §6

| Spec field | Delivered as | Source |
|---|---|---|
| `isDefaultOwner` | `isDefaultOwner` | **The owner account holds the Admin role.** No id or email to exclude client-side — both Yahia accounts qualify today, and any admin added later will too. **This answers §6: we return the flag.** |
| `ownerLastLoginAt` | `ownerLastLoginAt` | New column `PartnerLastLoginAt`, written **only** by partner-app and partner-web logins. The consumer app does not move it — verified: a consumer login moved `LastLoginAt` and left this null. |
| `ownerAppInstalled` | `ownerUsesPartnerApp` + `ownerUsesPartnerWeb` | **Not derivable from FCM tokens** — all 21,007 production tokens are the consumer app type; the partner app registers under it. Instead: has the owner ever held a partner-mobile / partner-web session. Accurate, and already true for 14 of the 22 real owners. |
| `lastPartnerActivityAt` | `lastPartnerActivityAt` | Newest of: QR generated, offer confirmed, update request submitted, offer proposed — all existing tables. |
| — | `ownerLastSeenAt` | Bonus: last authenticated request from a partner client (new column, throttled like `LastSeenAt`). Better "active" signal than login, since tokens live 10 days. |
| — | `stage` | Computed server-side so every screen agrees. |

### `stage` values (worst → best)

| Stage | Meaning | Action list |
|---|---|---|
| `NoOwner` | `ownerId` is null | assign an owner |
| `DefaultOwner` | owned by an admin account | find the real partner |
| `NeverUsedApp` | real owner, never signed into a partner client and no partner activity | onboard |
| `Inactive` | used it, but nothing within the window | re-engage |
| `Active` | partner-side activity or request within the window | — |

The funnel in §4 of the spec maps directly: `allStations → withRealOwner → ownerUsedPartnerApp → activeInWindow`.

## Notes

- **The two new timestamps start empty** and fill as owners log in / act after deploy. Until then "has used the app" comes from the session stamps and activity, which already have history — so the funnel is meaningful on day one.
- Production today: **190 of 214 stations are default-owner.** That is the real adoption gap; the 22 genuine owners are the short list.
- `activeWindowDays` defaults to 30; pass any positive value.
