# Delete Attachment By ID Endpoint

## Overview

A single generic endpoint to delete any attachment by its ID and folder type. Removes the record from the database and deletes the physical file from disk.

---

## Endpoint

### `DELETE /api/files/attachment/{folder}/{id}`

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `folder` | `UploadFileFolders` enum | Yes | Which attachment table/folder to delete from |
| `id` | `int` | Yes | The attachment ID |

### Valid Folder Values

| Enum Value | Table | Physical Folder |
|------------|-------|----------------|
| `CableAttachments` | `ChargingPointAttachment` | `/CableAttachments/` |
| `CableBanners` | `BannerAttachment` | `/CableBanners/` |
| `CableEmergencyService` | `EmergencyServiceAttachment` | `/CableEmergencyService/` |
| `CableServiceProvider` | `ServiceProviderAttachment` | `/CableServiceProvider/` |
| `CableOfferAttachments` | `OfferAttachment` | `/CableOfferAttachments/` |
| `CableChargingPoint` | ❌ Blocked | Icons — use upload icon endpoint instead |

### Example

```
DELETE /api/files/attachment/CableAttachments/15
DELETE /api/files/attachment/CableServiceProvider/8
```

### Response

- `200 OK` — attachment deleted from DB and disk
- `404 Not Found` — attachment with that ID not found or already deleted
- `400 Bad Request` — invalid folder or CableChargingPoint folder used
- `401 Unauthorized` — not authenticated

---

## What It Does

1. Validates the folder enum
2. Finds the attachment by ID in the correct table (checks `!IsDeleted`)
3. Deletes the physical file from disk
4. Removes the record from the database (hard delete)
5. Requires authentication

---

## Files

| File | Description |
|------|-------------|
| `Application/Common/Commands/DeleteAttachment/DeleteAttachmentCommand.cs` | Command + handler |
| `WebApi/Routes/FileRoutes.cs` | Route registration |
