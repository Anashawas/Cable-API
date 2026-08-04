# Social Media Platforms & Per-Provider Links

## Overview
Two new domain concepts power social-media presence for service providers and
charging points:

1. **`SocialMediaPlatform`** — a global catalog of platforms (Facebook,
   Instagram, TikTok, …). Each platform has an optional icon image that lives in
   the `CableSocialMediaIcons` upload folder. Icons are uploaded **once** by an
   admin and shared by every provider that links to that platform.
2. **`SocialLink`** — a polymorphic link row that connects a
   `ServiceProvider` *or* `ChargingPoint` to a platform + a URL. Multiple links
   per platform are allowed (e.g. main + branch Facebook pages).

This mirrors the existing polymorphism used by `ProviderOffer` and
`PartnerTransaction` (`ProviderType` + `ProviderId`).

---

## Database

### `dbo.SocialMediaPlatform`

| Column | Type | Notes |
|---|---|---|
| `Id`              | `INT PK` | identity |
| `Name`            | `NVARCHAR(100) NOT NULL UNIQUE` | English name, e.g. `"Facebook"` |
| `NameAr`          | `NVARCHAR(100) NULL` | Arabic name, e.g. `"فيسبوك"` |
| `IconFileName`    | `NVARCHAR(255) NULL` | file in `CableSocialMediaIcons` folder |
| `IconExtension`   | `NVARCHAR(50)  NULL` | |
| `IconContentType` | `NVARCHAR(50)  NULL` | |
| `IconFileSize`    | `BIGINT NULL` | |
| `DisplayOrder`    | `INT NOT NULL DEFAULT 0` | |
| `IsActive`        | `BIT NOT NULL DEFAULT 1` | |
| `IsDeleted`       | `BIT NOT NULL DEFAULT 0` | soft delete |
| audit (`CreatedAt`, `CreatedBy`, `ModifiedAt`, `ModifiedBy`) | | |

Filtered index `IX_SocialMediaPlatform_IsActive` on `IsActive` (where
`IsDeleted = 0`) for fast catalog reads.

### `dbo.SocialLink`

| Column | Type | Notes |
|---|---|---|
| `Id`                    | `INT PK` | |
| `ProviderType`          | `NVARCHAR(50) NOT NULL` | `"ServiceProvider"` or `"ChargingPoint"` (CHECK enforced) |
| `ProviderId`            | `INT NOT NULL` | Id of the SP/CP this link belongs to (**not** a `UserAccount.Id`) |
| `SocialMediaPlatformId` | `INT NOT NULL` FK → `SocialMediaPlatform.Id` | |
| `Url`                   | `NVARCHAR(1000) NOT NULL` | |
| `DisplayOrder`          | `INT NOT NULL DEFAULT 0` | |
| `IsDeleted`             | `BIT NOT NULL DEFAULT 0` | soft delete |
| audit | | |

Filtered index `IX_SocialLink_Provider` on `(ProviderType, ProviderId)` where
`IsDeleted = 0`.

> **No schema change on `ServiceProvider` or `ChargingPoint`.** Links live in a
> separate child table and are surfaced as a collection in API responses.

### Seed data (9 platforms, no icons yet)
`Facebook`, `Instagram`, `TikTok`, `X`, `YouTube`, `WhatsApp`, `Telegram`,
`Snapchat`, `LinkedIn` — `DisplayOrder` 1 → 9, `IsActive = 1`.

### Migration script
`Scripts/SocialMedia_Phase1_CreateTables.sql` is idempotent and additive (no
existing data touched). Already applied on dev; ready to run on production.

---

## API reference

### 1) Catalog — `/api/socialMediaPlatforms`

#### `GET /api/socialMediaPlatforms?activeOnly=true`
Public. Returns the catalog ordered by `DisplayOrder` then `Name`.

```json
[
  {
    "id": 1,
    "name": "Facebook",
    "nameAr": "فيسبوك",
    "iconUrl": "https://cable-app.com/Public/CableSocialMediaIcons/facebook_2026...png",
    "displayOrder": 1,
    "isActive": true
  },
  {
    "id": 2,
    "name": "Instagram",
    "nameAr": "إنستغرام",
    "iconUrl": null,
    "displayOrder": 2,
    "isActive": true
  }
]
```

| Query param | Default | Description |
|---|---|---|
| `activeOnly` | `true` | When `true`, returns only `IsActive = 1`. Pass `false` for the admin screen that shows deactivated rows. |

`iconUrl` is `null` until an icon has been uploaded for that platform.

---

#### `POST /api/socialMediaPlatforms` *(admin)*
Multipart form-data. Creates a new platform; the icon is optional.

| Field | Type | Required | Notes |
|---|---|---|---|
| `name`         | text     | yes | unique, ≤ 100 chars (English) |
| `nameAr`       | text     | no  | ≤ 100 chars (Arabic) |
| `displayOrder` | text/int | yes | `>= 0` |
| `icon`         | file     | no  | image; uses the existing upload pipeline |

Response: `200 OK` with the new platform `Id` (int).

Errors: `400` validation, `401` unauthenticated, `500` server.

---

#### `PUT /api/socialMediaPlatforms/{id}` *(admin)*
Multipart form-data. Updates fields; if `icon` is sent, the old icon file is
deleted and replaced.

| Field | Type | Required | Notes |
|---|---|---|---|
| `name`         | text     | yes | English name |
| `nameAr`       | text     | no  | Arabic name |
| `displayOrder` | text/int | yes | |
| `isActive`     | text/bool | yes | |
| `icon`         | file     | no  | provide only when replacing the icon |

Response: `200 OK`. Errors: `400`, `401`, `404`, `500`.

---

#### `DELETE /api/socialMediaPlatforms/{id}` *(admin)*
Soft delete: sets `IsDeleted = 1` and `IsActive = 0`. Existing `SocialLink` rows
that reference this platform stay valid in the DB but the platform disappears
from `GET /api/socialMediaPlatforms?activeOnly=true`.

Response: `200 OK`. Errors: `401`, `404`, `500`.

---

### 2) Per-provider links — `/api/socialLinks`

#### `GET /api/socialLinks?providerType={ServiceProvider|ChargingPoint}&providerId={int}`
Public. Returns the provider's links ordered by `DisplayOrder`, each enriched
with the platform name and resolved icon URL.

```json
[
  {
    "id": 105,
    "providerType": "ServiceProvider",
    "providerId": 17,
    "socialMediaPlatformId": 1,
    "socialMediaPlatformName": "Facebook",
    "socialMediaPlatformNameAr": "فيسبوك",
    "socialMediaPlatformIconUrl": "https://cable-app.com/Public/CableSocialMediaIcons/facebook_2026...png",
    "url": "https://facebook.com/cable.cafe.amman",
    "displayOrder": 1
  },
  {
    "id": 106,
    "providerType": "ServiceProvider",
    "providerId": 17,
    "socialMediaPlatformId": 1,
    "socialMediaPlatformName": "Facebook",
    "socialMediaPlatformNameAr": "فيسبوك",
    "socialMediaPlatformIconUrl": "https://cable-app.com/Public/CableSocialMediaIcons/facebook_2026...png",
    "url": "https://facebook.com/cable.cafe.amman.events",
    "displayOrder": 2
  }
]
```

> Notice both rows reference platform `1` — multiple URLs per platform are
> supported.

| Query param | Required | Description |
|---|---|---|
| `providerType` | yes | `"ServiceProvider"` or `"ChargingPoint"` |
| `providerId`   | yes | Id of the SP or CP |

---

#### `POST /api/socialLinks` *(admin)*
JSON body. **Replaces** the full link list for a provider atomically:
- existing rows for `(ProviderType, ProviderId)` are soft-deleted
- the new list is inserted

```http
POST /api/socialLinks
Content-Type: application/json
Authorization: Bearer <jwt>

{
  "providerType": "ServiceProvider",
  "providerId": 17,
  "links": [
    { "socialMediaPlatformId": 1, "url": "https://facebook.com/cable.cafe.amman",        "displayOrder": 1 },
    { "socialMediaPlatformId": 1, "url": "https://facebook.com/cable.cafe.amman.events", "displayOrder": 2 },
    { "socialMediaPlatformId": 2, "url": "https://instagram.com/cable.cafe.amman",       "displayOrder": 3 }
  ]
}
```

Response: `200 OK` with the new `SocialLink` Ids, in order:
```json
[105, 106, 107]
```

| Field | Required | Validation |
|---|---|---|
| `providerType` | yes | must be `"ServiceProvider"` or `"ChargingPoint"` |
| `providerId`   | yes | `> 0`, must reference an existing non-deleted parent |
| `links`        | yes | array (empty is allowed → removes all links) |
| `links[].socialMediaPlatformId` | yes | `> 0`, must be an existing **active** platform |
| `links[].url`                   | yes | non-empty, ≤ 1000 chars |
| `links[].displayOrder`          | no  | `>= 0`, default `0` |

Errors:
- `400` validation (bad `providerType`, missing `url`, …)
- `404` parent SP/CP not found, **or** any referenced platform is missing/inactive
- `401` unauthenticated
- `500` server

**Replacing semantics**: to clear all links, send `"links": []`.

---

#### `DELETE /api/socialLinks/{id}` *(admin)*
Soft-delete a single link. Response: `200 OK`. Errors: `401`, `404`, `500`.

---

## Common usage flows

### A. Admin onboarding a new platform
1. `POST /api/socialMediaPlatforms` with `name="Threads"`, `displayOrder=10`, no icon.
2. `PUT /api/socialMediaPlatforms/10` with the icon file once the asset is ready.
3. `GET /api/socialMediaPlatforms` confirms `iconUrl` is now non-null.

### B. Admin setting links for a service provider
```http
POST /api/socialLinks
{
  "providerType": "ServiceProvider",
  "providerId": 17,
  "links": [
    { "socialMediaPlatformId": 1, "url": "https://facebook.com/cable.cafe.amman" },
    { "socialMediaPlatformId": 2, "url": "https://instagram.com/cable.cafe.amman" }
  ]
}
```
Re-sending the same `POST` with a different list **replaces** the previous one.

### C. Frontend rendering on a provider profile screen
1. Render the provider profile from existing `GET /api/serviceProviders/{id}`.
2. Call `GET /api/socialLinks?providerType=ServiceProvider&providerId={id}`.
3. For each entry, render `socialMediaPlatformIconUrl` as the button image and
   open `url` on tap.

---

## Authorization

| Endpoint | Required |
|---|---|
| `GET /api/socialMediaPlatforms` | none |
| `POST/PUT/DELETE /api/socialMediaPlatforms` | JWT (admin) |
| `GET /api/socialLinks` | none |
| `POST/DELETE /api/socialLinks` | JWT (admin) |

> Authorization is `[Authorize]` at the route level. Role/permission checks
> follow the project's existing `Privilege` / `Role` system; the social-media
> endpoints don't introduce a new privilege.

---

## Implementation map

| Layer | Files |
|---|---|
| **Domain**       | `Domain/Enitites/SocialMediaPlatform.cs`, `Domain/Enitites/SocialLink.cs` |
| **Persistence**  | `Infrastructrue/Persistence/Configurations/SocialMediaPlatformConfiguration.cs`, `SocialLinkConfiguration.cs` |
| **DbContext**    | `Application/Common/Interfaces/IApplicationDbContext.cs` (+ `Infrastructrue/Persistence/ApplicationDbContext.cs`) — added `DbSet<SocialMediaPlatform> SocialMediaPlatforms` and `DbSet<SocialLink> SocialLinks` |
| **Upload**       | `Cable.Core/Enums/UploadFileFolders.cs` — added `CableSocialMediaIcons` |
| **Catalog CQRS** | `Application/SocialMediaPlatforms/Queries/GetAllSocialMediaPlatforms/*`, `Commands/AddSocialMediaPlatform/*`, `Commands/UpdateSocialMediaPlatform/*`, `Commands/DeleteSocialMediaPlatform/*` |
| **Link CQRS**    | `Application/SocialLinks/Queries/GetSocialLinksByProvider/*`, `Commands/SetSocialLinks/*`, `Commands/DeleteSocialLink/*` |
| **Routes**       | `WebApi/Routes/SocialMediaPlatformRoutes.cs`, `WebApi/Routes/SocialLinkRoutes.cs` + `WebApi/Program.cs` wiring |
| **Migration**    | `Scripts/SocialMedia_Phase1_CreateTables.sql` (idempotent, applied on dev) |

---

## Conventions reused (so it feels native to the codebase)

| Existing convention | This feature |
|---|---|
| Attachment table stores `FileName`, `Extension`, `ContentType`, `Size` + audit | `SocialMediaPlatform` stores the same for its icon |
| Upload via `IUploadFileService.SaveFileAsync(file, UploadFileFolders.*, ct)` | Uses the new `CableSocialMediaIcons` folder |
| Polymorphic owner via `ProviderType` + `ProviderId` (`ProviderOffer`, `PartnerTransaction`) | Same pattern for `SocialLink` |
| Soft delete via `IsDeleted` + filtered index `WHERE IsDeleted = 0` | Same on both new tables |
| CQRS per folder + minimal-API route group | Same |

---

## Rollback

Tables are new and additive; existing rows are unaffected. To revert:

```sql
DROP TABLE IF EXISTS dbo.SocialLink;
DROP TABLE IF EXISTS dbo.SocialMediaPlatform;
```

And revert the code (or simply ignore the unused endpoints — they're harmless if
the tables exist).

---

## Production rollout checklist

1. Run `Scripts/SocialMedia_Phase1_CreateTables.sql` on `MSSQL_MCP_Cable_Production`.
2. Deploy the build (includes the new endpoints).
3. Admin: hit `GET /api/socialMediaPlatforms` to verify catalog is present.
4. Admin: upload the 9 platform icons one by one via
   `PUT /api/socialMediaPlatforms/{id}` (multipart with `icon`).
5. Send a single `POST /api/socialLinks` to a known SP/CP and re-fetch via
   `GET` to confirm.
