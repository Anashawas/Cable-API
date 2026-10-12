# Banner action 6 "phone call" — backend reply (Oct 12, 2026)

> Answer to `BANNER_CALL_ACTION_BE_SPEC_AR.md`. Done, tested against the dev database, pushed
> (Cable-API + Cable-Admin). Live on dev / production after the next API publish.

## What was built

| Spec item | Done |
|---|---|
| 3.1 Accept `actionType = 6` on add and update | ✅ — and **only** 1–6 (or none). `7` → 400 «نوع إجراء البانر غير معروف…». Before this, **any** number was accepted. |
| 3.2 Normalise numbers on save | ✅ for `phone` (all banners) and `actionUrl` of types **2 and 6**: Arabic digits → Latin, spaces / dashes / brackets removed |
| Any dialable number | ✅ mobile `0791234567` → `962791234567` · landline `06 555 1234` → `96265551234` · short `911` → `911` · international `+44 7700 900123` / `0044…` → `447700900123` · also `+962 079…` (stray 0) → `962791234567` |
| Reject the undialable | ✅ 400 «رقم الهاتف غير صحيح»: letters, `*` / `#`, < 3 or > 15 digits. Type 2/6 with **both** `actionUrl` and `phone` empty → 400 too (the app would have nothing to call). |
| Return `+962…` from `GetAllBanners` / `GetNearbyBanners` | ✅ for `phone` and for `actionUrl` of type **6**. Short codes come back as-is (`911`). |
| 3.3 One-off clean-up of existing data | ✅ as an admin endpoint using the **same rules** (see below) |
| Admin option «اتصال هاتفي» | ✅ added to the add and edit forms (it was **not** in the repo yet), with a phone placeholder `0791234567` and a hint for types 2 and 6 |

## Two deliberate differences from the spec

1. **WhatsApp (type 2) `actionUrl` is returned as bare digits** (`962777317444`), not `+962…`. wa.me links must not carry a `+`,
   and app versions released before this change may build the link straight from this value. `phone` is still `+962…` for every type.
   Production has one live type-2 banner (id 56) — it keeps working unchanged.
2. **Landlines get their own rules.** The existing phone helper is Jordan-mobile-only and would rewrite the landline
   `96265551234` into the wrong mobile `962765551234` on the next save. Banners now use a separate normaliser
   (`DialablePhone`); SMS / OTP keep the old one.

## One-off clean-up — run once per environment after the publish

`POST /api/banners/admin/normalize-phones?dryRun=true` (admin login) → report only:
`{ checked, changed, unchanged, invalid, rows: [{ bannerId, bannerName, field, before, after, result }] }`.
Then `?dryRun=false` to apply. Values that cannot be dialled are listed as `Invalid` and **left untouched** for manual repair.
Safe to run again.

Expected today:
- **Dev**: banner 19 `0775338750` → `962775338750`; banner 40 `٠٧٩٧٩٤٠١٤٤` → `962797940144`; **invalid**: banner 50 `00010101001`, banner 51 `sdffsdsdf` (fix by hand in the admin).
- **Production**: all seven phones are already `962…` — nothing to change.

## Tests run (local API, dev database)

- `actionType 7` → 400 · type 6 + `actionUrl "abc"` → 400 · `phone "sdffsdsdf"` → 400 · type 6 with no number at all → 400.
- Type 6 + `actionUrl "٠٧٩١٢٣٤٥٦٧"` → 200, stored `962791234567`, returned `+962791234567`.
- Type 6, phone `06 555 1234`, no actionUrl → stored `96265551234`, returned `+96265551234`.
- Type 2, actionUrl `+962 77 731 7444` → stored and returned `962777317444`.
- Admin edit round trip (sending back `+962…`) → 200, stored value unchanged. A landline on update (rejected before) → 200.
- 23 normaliser cases (spec table + every phone in dev and production banners) → all pass.
- Types 1, 3, 4, 5 behave as before.

## Release order (as in the spec, §4)

1. Publish the API (+ admin) → run the clean-up dry run, then apply.
2. App update with `case '6'` to the stores.
3. Wait for most users to update.
4. Create the first type-6 banner. Until then, a call banner = type **5** with the number in `phone`.
