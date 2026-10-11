# Price alerts — mobile spec (Cable app)

> **Feature:** the driver asks to be reminded before electricity-price windows change (e.g. "remind me 30 minutes
> before peak starts"). The server holds the tariff and sends the reminders; the app shows the tariff, lets the
> driver choose, and receives the push. Requirement: `PRICE_ALERTS_BE_SPEC_AR.md`.
>
> **Status:** all endpoints are **live on dev** (`http://dev.cable-app.com`). No backend work is needed — app
> screens only. Production: `https://cable-app.com` after the next production release.
>
> Errors use RFC 7807 problem details — show `detail` to the user.

---

## 1. Endpoints

| # | Call | Login | Use |
|---|---|---|---|
| 1 | `GET /api/pricing/tou` | no | The tariff: price windows of the day |
| 2 | `GET /api/users/me/price-alerts` | yes | The driver's reminder settings |
| 3 | `PUT /api/users/me/price-alerts` | yes | Save the reminder settings |
| 4 | `PUT /api/notification-token` | yes | Push token **with the app language** (needed for the reminder to arrive in the right language) |

### 1.1 The tariff — `GET /api/pricing/tou`

Anonymous. Cache it on the device and refresh it **on every app open**. When `version` changes, re-render
the tariff screen (the windows or prices changed).

Real response from dev today:

```json
{
  "version": 1,
  "effectiveFrom": "2026-04-01T00:00:00.000",
  "timezone": "Asia/Amman",
  "currency": "JOD",
  "unit": "fils/kWh",
  "windows": [
    { "key": "offPeak", "startMin": 300,  "endMin": 840,  "tier": "offPeak", "priceFils": 183, "nameEn": "Off-peak",     "nameAr": "خارج الذروة" },
    { "key": "midday",  "startMin": 840,  "endMin": 1020, "tier": "partial", "priceFils": 193, "nameEn": "Partial peak", "nameAr": "ذروة جزئية" },
    { "key": "peak",    "startMin": 1020, "endMin": 1380, "tier": "peak",    "priceFils": 213, "nameEn": "Peak",         "nameAr": "الذروة" },
    { "key": "night",   "startMin": 1380, "endMin": 1740, "tier": "partial", "priceFils": 193, "nameEn": "Partial peak", "nameAr": "ذروة جزئية" }
  ]
}
```

- `startMin` / `endMin` = minutes from **midnight Jordan time** (Asia/Amman). `300` = 05:00, `1020` = 17:00.
- `endMin > 1440` means the window **crosses midnight**: `night` 1380 → 1740 = 23:00 → 05:00 next day.
- `tier` drives the colour: `offPeak` (green / cheap) · `partial` (amber) · `peak` (red / expensive).
- `priceFils` per kWh; show as fils or `priceFils / 1000` JOD.
- Use `nameAr` / `nameEn` by UI language. Two windows may share a name (`midday` and `night` are both "Partial
  peak") — always identify a window by `key`, never by name.
- **Never hard-code the windows or prices.** The Cable team can change them from the admin at any time (new `version`).

Which window is "now": convert the device time to Asia/Amman, take minutes since midnight `m`, and pick the window
with `startMin ≤ m < endMin`, or `startMin ≤ m + 1440 < endMin` for the one crossing midnight.

### 1.2 Read settings — `GET /api/users/me/price-alerts`

```json
{ "enabled": false, "leadMinutes": 15, "windows": [] }
```

A driver who never saved anything gets exactly this default (off, 15 min, nothing selected).

### 1.3 Save settings — `PUT /api/users/me/price-alerts`

```json
{ "enabled": true, "leadMinutes": 30, "windows": ["offPeak", "peak"] }
```

Returns the saved settings (same shape as 1.2).

| Field | Rule |
|---|---|
| `enabled` | `false` pauses all reminders but **keeps** the selection, so turning it back on restores it |
| `leadMinutes` | how long before the window starts: **15, 30, 45 or 60** — anything else → 400 "leadMinutes must be 15, 30, 45 or 60" |
| `windows` | `key` values from the tariff; an unknown key → 400 "Unknown window key(s): … Valid: …"; empty list allowed |

### 1.4 Push token — `PUT /api/notification-token`

Send after login, on every FCM token refresh, and **again whenever the user switches the app language**:

```json
{ "token": "<FCM token>", "osName": "android", "osVersion": "14", "appVersion": "3.4.0", "appType": 1, "language": "ar" }
```

- `appType: 1` = Cable app.
- `language`: `"ar"` or `"en"` — the app's UI language. Reminders are sent in this language. Without it they arrive in Arabic.

---

## 2. Screens

### 2.1 Tariff / reminders screen

Suggested entry: station page or settings → "Charging prices" / "أسعار الشحن".

1. **Today's timeline**: a 24-hour bar (00:00 → 24:00) coloured by `tier`, with the **current window highlighted**
   and "now" marker. Under it, one row per window: name, `HH:mm – HH:mm`, `priceFils` fils/kWh.
2. **"Remind me" switch** → `enabled`.
3. **When**: segmented control 15 / 30 / 45 / 60 min → `leadMinutes`.
4. **Which windows**: one checkbox / chip per tariff window (by `key`, labelled with name + time range so the two
   "Partial peak" windows are distinguishable).
5. **Save** → `PUT` (or save on every change — either is fine; the endpoint is idempotent).
6. Small note: "No reminders between 23:30 and 06:30" / "لا تنبيهات بين 23:30 و 06:30".

Disable steps 3–4 while the switch is off (but keep the values).

### 2.2 Push handling

When a reminder arrives:

| Item | Value |
|---|---|
| Title (ar) | `باقي 30 دقيقة على الذروة` (`ساعة` when the lead is 60) |
| Body (ar) | `السعر رح يرتفع لـ 213 فلس ⚡` — or `ينزل … 🔋` when the next window is cheaper |
| Title (en) | `Peak in 30 min` (`1 hour` for 60) |
| Body (en) | `Price rises to 213 fils ⚡` — or `drops … 🔋` |
| `data.type` | `"price_alert"` |
| `data.windowKey` | e.g. `"peak"` |
| `data.startsAt` | Jordan local time the window starts, `"2026-10-11T17:00:00"` |

On tap with `data.type = "price_alert"` → open the tariff screen with that window highlighted.

---

## 3. How the server sends (so the app doesn't duplicate it)

- A server job runs every 5 minutes and sends each reminder at window start − `leadMinutes` (Jordan time).
- **At most one reminder per user, per window, per day** — the server de-duplicates; the app must **not** schedule
  its own local notifications for this feature.
- **Quiet hours 23:30 → 06:30** (Jordan): a reminder that falls inside is **cancelled**, not delayed. So with the
  current tariff, a reminder for `offPeak` (05:00) with lead 30 (04:30) is never sent. Show the note in 2.1 so this
  does not look like a bug. The Cable team can change the quiet hours from the admin.
- Only drivers with `enabled = true`, the window selected, and a registered push token receive it.
- Reminders are push only (they do not appear in the in-app notification inbox).

---

## 4. Test on dev

1. Open the app logged out → the tariff screen loads (anonymous `GET /api/pricing/tou`).
2. Log in → `GET /api/users/me/price-alerts` returns the default `{ enabled: false, leadMinutes: 15, windows: [] }`.
3. Turn on, choose 15 min and the next window (e.g. `peak` at 17:00) → save → `GET` again returns the same values.
4. Send `leadMinutes: 20` → 400 with the message; send a made-up window key → 400 listing the valid keys.
5. Make sure the push token was sent with `language`; at 16:45 Jordan time the "Peak in 15 min" push arrives.
   To test without waiting, ask the backend team for the admin **dry run** (Price alerts page → "Dry run" for any
   Jordan time), which shows exactly who would be reminded and the text — it sends nothing.
6. Switch the app language → token re-sent with the new `language` → the next reminder arrives in that language.
7. Turn the switch off → `GET` still returns the selected windows with `enabled: false`; no more pushes.
