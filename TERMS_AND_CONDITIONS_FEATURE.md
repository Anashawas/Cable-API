# Terms & Conditions — Versioned Policies with Acceptance Tracking

**Date:** 2026-07-19 · **Status:** ✅ Implemented, live-tested on dev. Prod needs `Scripts/Terms_Phase1.sql` before deploy.

## What it is

Versioned terms & conditions with a full acceptance audit trail:

- Policy **content lives in the DB** — Arabic + English (`contentAr` / `contentEn`, HTML allowed)
- Each version carries a **`systemVersion`** display string (e.g. `"2.2.2"`) tying it to an app release
- **`roleId` scoping**: `null` = all roles; `4` = providers only; `3` = users only — one active version per scope
- Publishing a new version **automatically forces re-acceptance** for everyone in its scope (no user data touched)
- Every acceptance is **recorded permanently**: who, which version, when — provable in disputes
- Enforcement is **FE-gated**: the API reports `hasAcceptedTerms`; the app blocks its own UI until accepted

## Endpoints (tag: Terms & Conditions)

| Method | Route | Auth | Notes |
|---|---|---|---|
| GET | `/api/terms/GetCurrentTerms` | — (anonymous OK) | The active policy for the caller: role-specific first, general fallback. Anonymous (registration screen) → general policy, `hasAccepted: false`. Body `null` when nothing published |
| POST | `/api/terms/AcceptTerms` | 🔒 user | Records acceptance of the caller's applicable version. **Idempotent.** Returns `{termsVersionId, systemVersion, acceptedAt}` |
| POST | `/api/terms/admin/PublishTermsVersion` | 🔒 admin | `{systemVersion, roleId?, contentEn, contentAr, effectiveFrom?}` — deactivates the current version in that scope, activates the new one |
| GET | `/api/terms/admin/GetAllTermsVersions` | 🔒 admin | All versions + acceptance counts (content excluded; optional `page`/`pageSize`) |
| GET | `/api/terms/admin/GetTermsVersionById/{id}` | 🔒 admin | One version with full AR + EN content |

**Profile flag:** `GET /api/users/GetUserById/{id}` now returns `hasAcceptedTerms` — `true` also when no terms are published at all (nothing to accept), so the FE can always gate on `false`.

### `GetCurrentTerms` response

```json
{
  "id": 3, "systemVersion": "2.3.0", "roleId": 4,
  "effectiveFrom": "2026-07-19T19:17:00",
  "contentEn": "<h1>Provider terms</h1>...",
  "contentAr": "<h1>شروط المزود</h1>...",
  "hasAccepted": false, "acceptedAt": null
}
```

## FE flow (mobile + portal)

1. On login/startup read `hasAcceptedTerms` from the profile (or `GetCurrentTerms`)
2. If `false` → show the policy screen (`contentAr`/`contentEn` per app language, rendered as HTML) with an Accept button
3. Accept button → `POST /api/terms/AcceptTerms` → proceed
4. Registration screen: call `GetCurrentTerms` **without a token** to display the general policy

## Data model

```
TermsVersion         Id, SystemVersion, RoleId (null=all), ContentEn, ContentAr,
                     EffectiveFrom, IsActive (one active per role scope), audit cols
UserTermsAcceptance  Id, UserId, TermsVersionId, AcceptedAt   ← immutable audit, never updated
UserAccount          + AcceptedTermsVersionId, + TermsAcceptedAt (denormalized fast check)
```

## Verified live (dev)

empty state → 200 null ✅ · non-admin publish → 403 ✅ · publish v1 → anonymous GET returns AR+EN content ✅ · accept → flag true in GetCurrentTerms **and** profile ✅ · re-accept idempotent (same timestamp, no duplicate row) ✅ · **publish v2 → user's flag flips to false automatically** ✅ · provider-scoped doc: role-4 user gets it, role-3 user keeps the general one ✅ · admin list shows acceptance counts ✅

## Deploy

1. Run `Scripts/Terms_Phase1.sql` on prod (idempotent) **before** the build
2. Deploy
3. Admin publishes the first real policy via `PublishTermsVersion` — until then every `hasAcceptedTerms` is `true` and nothing is gated (safe rollout)
