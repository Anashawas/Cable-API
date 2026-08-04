# Update Notes Read Feature

## Overview

Simple feature to show "What's New" dialog to users after each app update. Content is static in the mobile app — the backend only tracks whether the user has seen it.

---

## How It Works

1. Admin pushes new app update → manually resets all users: `UPDATE UserAccount SET HasReadUpdateNotes = 0`
2. User logs in → mobile checks `hasReadUpdateNotes` in login response
3. If `false` → mobile shows the "What's New" dialog
4. User clicks confirm → mobile calls `PATCH /api/users/mark-update-notes-read`
5. Next login → `hasReadUpdateNotes = true` → dialog not shown

---

## New Field

| Table | Field | Type | Default | Description |
|-------|-------|------|---------|-------------|
| `UserAccount` | `HasReadUpdateNotes` | `bit` | `0` | `false` = user hasn't seen update notes, `true` = user has confirmed |

---

## New Endpoint

### `PATCH /api/users/mark-update-notes-read`

Sets `HasReadUpdateNotes = true` for the authenticated user.

- **Auth:** Required (any authenticated user)
- **Request body:** None
- **Response:** `200 OK`

---

## Field Included In

| Endpoint / DTO | Field |
|----------------|-------|
| Login response (`UserDetailsResult`) | `hasReadUpdateNotes` |
| `GET /api/users/GetAllUsers` (`GetAllUsersDto`) | `hasReadUpdateNotes` |
| `GET /api/users/GetUserById/{id}` (`GetUserByIdDto`) | `hasReadUpdateNotes` |

---

## Reset (Manual)

When a new app version is released, run this SQL to reset all users:

```sql
UPDATE UserAccount SET HasReadUpdateNotes = 0
```

All users will see the "What's New" dialog on their next login.

---

## Files Modified

| File | Change |
|------|--------|
| `Domain/Enitites/UserAccount.cs` | Added `HasReadUpdateNotes` property |
| `Infrastructrue/Persistence/Configurations/UserAccountConfiguration.cs` | Added column config with default `false` |
| `Application/Common/Models/UserLoginDetails.cs` | Added `HasReadUpdateNotes` to `UserDetailsResult` |
| `Application/Common/Extensions/UserAccountExtensions.cs` | Included field in `ToUserDetails()` mapping |
| `Application/Users/Queries/GetAllUsers/GetAllUsersDto.cs` | Added field to DTO |
| `Application/Users/Queries/GetAllUsers/GetAllUsersRequest.cs` | Added field to query projection |
| `Application/Users/Queries/GetUserById/GetUserByIdDto.cs` | Added field to DTO |
| `Application/Users/Queries/GetUserById/GetUserByIdRequest.cs` | Added field to query projection |
| `Application/Users/Commands/MarkUpdateNotesRead/MarkUpdateNotesReadCommand.cs` | **NEW** — command handler |
| `WebApi/Routes/UserRoutes.cs` | Added `PATCH /mark-update-notes-read` endpoint |

## Database Migration

```sql
ALTER TABLE UserAccount ADD HasReadUpdateNotes bit NOT NULL DEFAULT 0;
```
