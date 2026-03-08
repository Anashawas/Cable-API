# Station → Provider Refactoring

## Overview
Renamed the "Station" role (Id=4) to "Provider" — a single unified role for users who own **ChargingPoints** and/or **ServiceProviders**. Consolidated all provider-owner endpoints into a new `/api/provider` route group with a unified dashboard.

## SQL Migration (Run BEFORE deploying)
```sql
UPDATE dbo.Role SET Name = 'Provider' WHERE Id = 4 AND Name = 'Station';
```
> Active `station-auth:*` cache entries will become orphaned (10-min TTL, self-resolves).

---

## Part A: Rename Station → Provider (All Layers)

### Application Models
| Action | File | Description |
|--------|------|-------------|
| Created | `Application/Common/Models/ProviderAuthSession.cs` | Cache model for 2FA session (UserId, PhoneNumber, expiry) |
| Created | `Application/Common/Models/ProviderAuthSessionResult.cs` | Result DTO (Success, SessionToken, PhoneMasked, ExpiresAt) |
| Deleted | `Application/Common/Models/StationAuthSession.cs` | Replaced by ProviderAuthSession |
| Deleted | `Application/Common/Models/StationAuthSessionResult.cs` | Replaced by ProviderAuthSessionResult |

### Interface
| Action | File | Changes |
|--------|------|---------|
| Modified | `Application/Common/Interfaces/IAuthenticationService.cs` | `LoginStation` → `LoginProvider`, `SendStationOtpAsync` → `SendProviderOtpAsync`, `VerifyStationOtpAsync` → `VerifyProviderOtpAsync` |

### Implementation
| Action | File | Changes |
|--------|------|---------|
| Modified | `Infrastructrue/Identity/AuthenticationService.cs` | Full rename in `#region Provider 2FA Authentication`: methods, role check (`"Provider"`), cache key (`"provider-auth:"`), session class, error messages |

### WebApi Request DTOs
| Action | File | Description |
|--------|------|-------------|
| Created | `WebApi/Requests/Providers/ProviderLoginRequest.cs` | `record ProviderLoginRequest(string Email, string Password)` |
| Created | `WebApi/Requests/Providers/ProviderLoginRequestValidator.cs` | FluentValidation: Email required + valid, Password required |
| Created | `WebApi/Requests/Providers/ProviderLoginRequestValidationFilter.cs` | IEndpointFilter |
| Created | `WebApi/Requests/Providers/ProviderSendOtpRequest.cs` | `record ProviderSendOtpRequest(string SessionToken)` |
| Created | `WebApi/Requests/Providers/ProviderSendOtpRequestValidator.cs` | FluentValidation: SessionToken required |
| Created | `WebApi/Requests/Providers/ProviderSendOtpRequestValidationFilter.cs` | IEndpointFilter |
| Created | `WebApi/Requests/Providers/ProviderVerifyOtpRequest.cs` | `record ProviderVerifyOtpRequest(string SessionToken, string OtpCode)` |
| Created | `WebApi/Requests/Providers/ProviderVerifyOtpRequestValidator.cs` | FluentValidation: SessionToken required, OtpCode 6 digits numeric |
| Created | `WebApi/Requests/Providers/ProviderVerifyOtpRequestValidationFilter.cs` | IEndpointFilter |
| Deleted | `WebApi/Requests/Stations/` | Entire folder (9 old Station request files) |

---

## Part B: New Queries

### GetMyServiceProviders
| Action | File | Description |
|--------|------|-------------|
| Created | `Application/ServiceProviders/Queries/GetMyServiceProviders/GetMyServiceProvidersRequest.cs` | Returns `List<ServiceProviderDto>` filtered by `OwnerId == currentUserService.UserId`. Optional `CategoryId` filter. Reuses existing `ServiceProviderDto`. |

### GetMyProviderAssets (Unified Dashboard)
| Action | File | Description |
|--------|------|-------------|
| Created | `Application/Providers/Queries/GetMyProviderAssets/GetMyProviderAssetsRequest.cs` | Returns `ProviderAssetsDto(List<GetAllChargingPointsDto> ChargingPoints, List<ServiceProviderDto> ServiceProviders)`. Calls `IChargingPointRepository.GetChargingPointsByOwner()` + inline ServiceProviders query. |

---

## Part C: Owner Validation on ServiceProvider Commands

Added `ICurrentUserService` injection + ownership check to 5 command handlers. Pattern:
```csharp
var userId = currentUserService.UserId
             ?? throw new NotAuthorizedAccessException("User not authenticated");
// ... fetch entity ...
if (serviceProvider.OwnerId != userId)
    throw new ForbiddenAccessException("You are not the owner of this service provider");
```

| File | Notes |
|------|-------|
| `Application/ServiceProviders/Commands/UpdateServiceProvider/UpdateServiceProviderCommand.cs` | Added userId check + ownership validation |
| `Application/ServiceProviders/Commands/DeleteServiceProvider/DeleteServiceProviderCommand.cs` | Added `ICurrentUserService`, userId check + ownership validation |
| `Application/ServiceProviders/Commands/UploadServiceProviderIcon/UploadServiceProviderIconCommand.cs` | Added `ICurrentUserService`, userId check + ownership validation |
| `Application/ServiceProviders/Commands/AddServiceProviderAttachment/AddServiceProviderAttachmentCommand.cs` | Added `ICurrentUserService`, userId check + ownership validation |
| `Application/ServiceProviders/Commands/DeleteServiceProviderAttachment/DeleteServiceProviderAttachmentCommand.cs` | Added `ICurrentUserService`, fetches ServiceProvider entity first (original only queried attachments) |

---

## Part D: New ProviderRoutes + Route Cleanup

### Created: `WebApi/Routes/ProviderRoutes.cs`
Route group: `/api/provider` with tag `"Provider"` — **22 endpoints** organized into 4 sub-groups:

#### Authentication (3 endpoints — no auth required)
| Method | Path | Handler |
|--------|------|---------|
| POST | `/authenticate` | `IAuthenticationService.LoginProvider()` |
| POST | `/send-otp` | `IAuthenticationService.SendProviderOtpAsync()` |
| POST | `/verify-otp` | `IAuthenticationService.VerifyProviderOtpAsync()` |

#### Dashboard (1 endpoint — auth required)
| Method | Path | Handler |
|--------|------|---------|
| GET | `/my-assets` | `GetMyProviderAssetsRequest` via MediatR |

#### Charging Point Management (11 endpoints — auth required)
| Method | Path | Handler |
|--------|------|---------|
| GET | `/charging-points/my` | `GetMyChargingPointsRequest` |
| POST | `/charging-points/submit-update-request/{chargingPointId}` | `SubmitChargingPointUpdateRequestCommand` |
| POST | `/charging-points/upload-update-request-icon/{updateRequestId}` | `UploadUpdateRequestIconCommand` |
| POST | `/charging-points/add-update-request-attachments/{updateRequestId}` | `AddUpdateRequestAttachmentsCommand` |
| PATCH | `/charging-points/change-owner/{chargingPointId}` | `ChangeChargingPointOwnerCommand` |
| POST | `/charging-points/update-requests/my-requests` | `GetMyUpdateRequestsRequest` |
| GET | `/charging-points/update-requests/{id}` | `GetUpdateRequestByIdRequest` |
| DELETE | `/charging-points/update-requests/{id}/cancel` | `CancelUpdateRequestCommand` |
| POST | `/charging-points/update-requests/pending` | `GetPendingUpdateRequestsRequest` |
| POST | `/charging-points/update-requests/{id}/approve` | `ApproveUpdateRequestCommand` |
| POST | `/charging-points/update-requests/{id}/reject` | `RejectUpdateRequestCommand` |

#### Service Provider Management (7 endpoints — auth required)
| Method | Path | Handler |
|--------|------|---------|
| GET | `/service-providers/my` | `GetMyServiceProvidersRequest` |
| POST | `/service-providers/create` | `CreateServiceProviderCommand` |
| PUT | `/service-providers/{id}` | `UpdateServiceProviderCommand` (+ owner check) |
| DELETE | `/service-providers/{id}` | `DeleteServiceProviderCommand` (+ owner check) |
| POST | `/service-providers/{id}/upload-icon` | `UploadServiceProviderIconCommand` (+ owner check) |
| POST | `/service-providers/{id}/add-attachments` | `AddServiceProviderAttachmentCommand` (+ owner check) |
| DELETE | `/service-providers/{id}/delete-attachments` | `DeleteServiceProviderAttachmentCommand` (+ owner check) |

### Removed from existing routes
| File | Removed |
|------|---------|
| `WebApi/Routes/UserRoutes.cs` | `MapStationAuthenticationRoutes()` method + chain call + `using Cable.Requests.Stations` |
| `WebApi/Routes/ChargingPointsRoutes.cs` | `MapStationRoutes()` method + chain call + unused usings |

### Registered in Program.cs
Added `.MapProviderRoutes()` to route registration chain (line 235).

---

## What Stayed Unchanged
- `/api/service-providers/*` public endpoints (GetAll, GetById, GetNearby, GetByCategory, Favorites, Ratings, GetAttachments) — user-facing, not owner operations
- `/api/charging-points/*` public endpoints — unchanged
- `StationType` entity, `FirebaseAppType.StationApp`, `StationTypeId` fields — not in scope
- ServiceProvider approval workflow — deferred to later phase

## Build Result
**0 errors**, 77 warnings (all pre-existing)
