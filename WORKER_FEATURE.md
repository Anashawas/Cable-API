# Worker (Provider Manager) Feature

## Overview
A **worker** is a second account — besides the owner — allowed to manage a single
provider (a `ServiceProvider` or a `ChargingPoint`).

- **One worker per provider.** A provider has at most one active worker.
- **All workers are equal.** There are no access levels / tiers.
- **Owner is in full control.** The owner creates the worker account, assigns it,
  can deactivate it, or delete it.
- The **owner stays in `CP.OwnerId` / `SP.OwnerId`** (one per provider) and is
  never a `ProviderManager` row.

## How a worker logs in
A worker account is a normal `UserAccount` with `RoleId = Worker`. The provider
app login was already **email + password + phone OTP (2FA)**; the only change is
the role gate now accepts **Provider OR Worker**:

```
Owner → POST /api/workers  (name + email + password + worker's phone)
      → backend creates UserAccount {RoleId: Worker} + ProviderManager row
Worker → provider-app login (email + password) → OTP to their phone → logged in
       → GetMyChargingPoints / GetMyServiceProviders returns the assigned provider
```

## Database

### Role
A new role row `Worker` is added to `dbo.Role`.

### `dbo.ProviderManager`
| Column | Type | Notes |
|---|---|---|
| `Id`           | `INT PK` | |
| `ProviderType` | `NVARCHAR(50) NOT NULL` | `"ServiceProvider"` or `"ChargingPoint"` (CHECK) |
| `ProviderId`   | `INT NOT NULL` | the SP/CP Id |
| `UserId`       | `INT NOT NULL` FK → `UserAccount` | the Worker account |
| `IsActive`     | `BIT NOT NULL DEFAULT 1` | owner can pause/resume |
| `IsDeleted`    | `BIT NOT NULL DEFAULT 0` | soft delete |
| audit | | |

Indexes:
- `UX_ProviderManager_OneWorker` — UNIQUE on `(ProviderType, ProviderId)` where
  `IsDeleted = 0` → **one worker per provider**.
- `IX_ProviderManager_User` — on `UserId` for "providers this user works for".

Migration: `Scripts/Worker_Phase1_CreateProviderManager.sql` (idempotent).

## API — `/api/workers` (owner)

### `GET /api/workers?providerType={…}&providerId={…}`
Returns the single worker for a provider, or `null`.
```json
{
  "providerManagerId": 12,
  "userId": 20990,
  "name": "Sami",
  "phone": "962790000000",
  "email": "sami.worker@example.com",
  "isActive": true,
  "assignedAt": "2026-06-22T10:00:00"
}
```

### `POST /api/workers`  *(owner only)*
Creates the worker account and assigns it.
```json
{
  "providerType": "ServiceProvider",
  "providerId": 17,
  "name": "Sami",
  "email": "sami.worker@example.com",
  "phone": "0790000000",
  "password": "Secret123"
}
```
Response: `{ "workerUserId": 20990, "providerManagerId": 12 }`

Validation / errors:
- `400` — bad provider type, invalid email, password < 6 chars, **email or phone already in use**, or **the provider already has a worker**.
- `403` — caller is not the provider owner.
- `404` — provider not found.

### `PATCH /api/workers/{providerManagerId}/active?isActive={true|false}`  *(owner only)*
Activates / deactivates the worker. Deactivating also sets the worker account's
`IsActive = false`, so they can no longer log in.

### `DELETE /api/workers/{providerManagerId}`  *(owner only)*
Soft-deletes the assignment **and** the worker account (`IsActive = false`,
`IsDeleted = true`). This frees the provider to receive a new worker.

## What a worker can do
Once logged into the provider app, the worker sees their assigned provider via
`GetMyChargingPoints` / `GetMyServiceProviders` (both now include providers where
the user is the active worker, in addition to ones they own) and can manage it
through the existing provider endpoints.

> Owner-only actions (delete the provider, transfer ownership, manage the worker)
> remain the owner's responsibility — enforce these when the authorization layer
> is wired in.

## Implementation map
| Layer | Files |
|---|---|
| Domain | `Domain/Enitites/ProviderManager.cs` |
| Persistence | `Infrastructrue/Persistence/Configurations/ProviderManagerConfiguration.cs`; `DbSet<ProviderManager>` in `IApplicationDbContext` + `ApplicationDbContext` |
| Auth gate | `Infrastructrue/Identity/AuthenticationService.cs` — `LoginProvider` allows Provider **or** Worker |
| CQRS | `Application/Workers/Commands/CreateWorker/*`, `SetWorkerActive/*`, `DeleteWorker/*`, `Queries/GetWorkerByProvider/*`, plus `WorkerOwnershipHelper.cs` |
| Visibility | `GetMyServiceProviders` (LINQ) and `ChargingPointRepository.GetChargingPointsByOwner` (raw SQL) include worker-assigned providers |
| Routes | `WebApi/Routes/WorkerRoutes.cs` + `Program.cs` wiring |
| Migration | `Scripts/Worker_Phase1_CreateProviderManager.sql` |

## Rollout
1. Dev: run the migration script (done), build (0 errors), test.
2. Production: run the same idempotent script, deploy.

## Removed from the earlier (richer) design
Per the simplified requirement, these were **not** built: `AccessLevel` table,
`AccessLevelPrivilege` table, per-level privileges, multi-manager support, and
the role auto-promote/demote sync (the worker account is created as `Worker`
directly).
