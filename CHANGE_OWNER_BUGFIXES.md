# Change Owner Endpoint Bug Fixes

## Overview

Fixed security and validation bugs in the ChargingPoint and ServiceProvider ownership transfer endpoints.

---

## Bugs Found & Fixed

### Bug 1: Missing Ownership Check (ChargingPoint)

**Endpoint:** `PUT /api/chargingPoints/ChangeOwner`

**Problem:** Any authenticated user could change the owner of any charging point — no check that the current user is the actual owner.

**Fix:** Added `ICurrentUserService` injection and ownership validation:
```
if (chargingPoint.OwnerId != userId)
    throw ForbiddenAccessException("You are not the owner of this charging point");
```

> Note: ServiceProvider already had this check.

---

### Bug 2: Same Owner Allowed (Both)

**Endpoints:**
- `PUT /api/chargingPoints/ChangeOwner`
- `PUT /api/serviceProviders/ChangeOwner`

**Problem:** A provider could "transfer" ownership to themselves — no error, just a pointless database update.

**Fix:** Added same-owner validation on both:
```
if (entity.OwnerId == request.NewOwnerId)
    throw DataValidationException("New owner is already the current owner");
```

---

### Bug 3: No Role Check on New Owner (Both)

**Endpoints:**
- `PUT /api/chargingPoints/ChangeOwner`
- `PUT /api/serviceProviders/ChangeOwner`

**Problem:** Ownership could be transferred to any user regardless of their role (e.g., a regular User with RoleId = 3). Only users with **Provider role (RoleId = 4)** should be allowed to own a ChargingPoint or ServiceProvider.

**Fix:** Added role validation on both:
```
if (newOwner.RoleId != 4)
    throw DataValidationException("New owner must have Provider role");
```

---

### Bug 4: Wallet Not Affected (No Fix Needed)

**Concern:** Does changing the owner affect the provider's WalletBalance or WalletCreditLimit?

**Answer:** No. The wallet (`WalletBalance`, `WalletCreditLimit`) belongs to the **ChargingPoint/ServiceProvider entity**, not to the owner user. Changing ownership does not reset or transfer the wallet — it stays with the entity.

---

## Files Modified

| File | Change |
|------|--------|
| `Application/ChargingPoints/Commands/ChangeChargingPointOwner/ChangeChargingPointOwnerCommand.cs` | Added ownership check, same-owner validation, role check |
| `Application/ServiceProviders/Commands/ChangeServiceProviderOwner/ChangeServiceProviderOwnerCommand.cs` | Added same-owner validation, role check |

---

## Validation Summary

| Check | ChargingPoint | ServiceProvider |
|-------|:---:|:---:|
| User must be authenticated | ✅ | ✅ |
| User must be current owner | ✅ (new) | ✅ (existed) |
| New owner cannot be same as current | ✅ (new) | ✅ (new) |
| New owner must exist and not deleted | ✅ | ✅ |
| New owner must have Provider role (4) | ✅ (new) | ✅ (new) |
