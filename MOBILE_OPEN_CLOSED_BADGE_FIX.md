# Mobile fix — provider shows "مغلق" in the list but "مفتوح 24/7" in the details

**For:** Mobile team
**Scope:** Cable consumer app (Flutter) — offers list card + service details screen
**Backend change required:** **None.** The API is behaving correctly; see §3.

---

## 1. The symptom

On the **العروض** list, *Khalefa Service* renders the red **مغلق** badge.
Opening the same provider shows **ساعات العمل: مفتوح 24/7**.

Same provider, same API response, two opposite answers in the same session.

## 2. Root cause

The provider's stored hours are:

```
FromTime = "00:00:00"
ToTime   = "00:00:00"
```

`FromTime` and `ToTime` are **equal**, which by convention means *open 24 hours*.

- The **details screen** implements that convention → renders "مفتوح 24/7". ✅
- The **list card** does not. It evaluates a range like `now >= from && now < to`. With
  `from == to` that window is zero-length, so it is false at every instant of the day →
  renders "مغلق". ❌

Both screens read the same two fields. They disagree because the "equal means 24/7"
rule exists in one of them and not the other.

## 3. Why this is not a backend issue

The API does **not** compute or return any open/closed value. `ServiceProviderDto`
exposes only:

```csharp
string? FromTime,
string? ToTime,
```

There is no `isOpen`, no `isClosed`, no server-side schedule evaluation anywhere in the
codebase — verified by search across the whole solution. The API returns the same two
strings to both screens, and the response is identical whichever screen asks.

Therefore the badge is derived **entirely in the app**, and two different derivations in
two widgets is the whole of the defect. Nothing changes on the server to fix it.

## 4. What the parser must accept

This matters more than it looks: the fix is not just "handle `from == to`". These are the
**actual value pairs in production today**, and the shared function has to survive all of
them.

| `FromTime` → `ToTime` | Records | Meaning |
|---|---|---|
| `0:00` → `0:00` | **150 charging points** | 24 hours |
| `00:00:00` → `00:00:00` | service providers | 24 hours (note: **with seconds**) |
| `00:00` → `0:00` | 4 | 24 hours (mixed padding) |
| `06:00` → `24:00` | 2 | `24:00` is not a wall-clock time — treat as end of day (1440) |
| `5:30` → `2:00`, `5:00` → `2:00` | 2 | **overnight span** — closes after midnight |
| `06:00` → `22:00`, `9:00` → `21:00`, … | several | ordinary same-day ranges |
| `0` → `0` | 1 | 24 hours (bare, no colon) |
| `0:0]` → `0:00` | 1 | **unparseable** |
| `NULL` | **36 charging points** | hours unknown |

Two consequences worth stating plainly:

- **151 of 209 charging points (72%) have `FromTime == ToTime`.** If the list card treats
  equal times as closed, the large majority of stations currently display as closed in
  list views. This is not a one-provider bug.
- **Unknown hours must not render as "مغلق".** 36 stations have `NULL` hours and one is
  malformed. Showing those as closed hides working stations from users.

## 5. The fix

Extract **one** function and call it from both the list card and the details screen.
Neither widget should evaluate hours itself.

```dart
enum OpenState { open, closed, unknown }

/// Minutes since midnight, or null if unparseable.
/// Accepts "0", "0:00", "00:00", "00:00:00", "24:00".
int? _parseMinutes(String? raw) {
  if (raw == null) return null;
  final s = raw.trim();
  if (s.isEmpty) return null;

  final parts = s.split(':');
  final h = int.tryParse(parts[0]);
  if (h == null) return null;                      // "0:0]" and friends
  final m = parts.length > 1 ? int.tryParse(parts[1]) : 0;
  if (m == null) return null;

  if (h == 24 && m == 0) return 1440;              // "24:00" = end of day
  if (h < 0 || h > 23 || m < 0 || m > 59) return null;
  return h * 60 + m;
}

OpenState openState(String? fromTime, String? toTime, {DateTime? now}) {
  final f = _parseMinutes(fromTime);
  final t = _parseMinutes(toTime);

  // Unknown or malformed hours: show NO badge. Never "closed".
  if (f == null || t == null) return OpenState.unknown;

  // Equal endpoints mean open 24 hours — this is the bug being fixed.
  if (f == t) return OpenState.open;

  final n = now ?? DateTime.now();
  final cur = n.hour * 60 + n.minute;

  // to <= from means the range crosses midnight (e.g. 5:30 -> 2:00).
  if (t < f) return (cur >= f || cur < t) ? OpenState.open : OpenState.closed;

  return (cur >= f && cur < t) ? OpenState.open : OpenState.closed;
}
```

Rendering rule:

| `openState` | Badge |
|---|---|
| `open` | مفتوح (green) |
| `closed` | مغلق (red) |
| `unknown` | **no badge at all** |

The details screen keeps showing "مفتوح 24/7" when `f == t`; that wording is correct and
should stay — it just needs to come from the same function so it can never diverge again.

## 6. Test cases

| From | To | Now | Expected |
|---|---|---|---|
| `00:00:00` | `00:00:00` | any | open ← *the reported bug* |
| `0:00` | `0:00` | any | open |
| `0` | `0` | any | open |
| `06:00` | `22:00` | 10:00 | open |
| `06:00` | `22:00` | 23:00 | closed |
| `06:00` | `24:00` | 23:30 | open |
| `5:30` | `2:00` | 23:00 | open (overnight) |
| `5:30` | `2:00` | 01:00 | open (overnight) |
| `5:30` | `2:00` | 04:00 | closed |
| `null` | `null` | any | unknown → no badge |
| `0:0]` | `0:00` | any | unknown → no badge |

## 7. Planned follow-up (backend, optional, not required for this fix)

To remove the ambiguity permanently, the API can expose `isAlwaysOpen` and `isOpenNow`
(nullable — `null` = unknown) computed server-side in Jordan time, so the app renders a
field instead of parsing strings. Both would be **additive** and safe to ignore until a
release is ready to consume them.

That is a future improvement, **not a prerequisite**. The fix in §5 is complete on its own
and can ship independently.
