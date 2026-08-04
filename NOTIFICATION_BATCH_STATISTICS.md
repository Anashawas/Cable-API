# Notification Batch Statistics — Admin Endpoints

## Overview
When an admin sends a notification (broadcast, targeted list, or filter-based),
every `NotificationInbox` row created by that single send is now stamped with the
**same** `BatchId` (a `UNIQUEIDENTIFIER`). This lets the admin dashboard answer:

- How many users was a given notification sent to?
- How many of them opened / read it?
- What is the read rate (%)?
- Who exactly received it and who hasn't read it yet?

Two read-only admin endpoints expose these statistics.

---

## Database change

`dbo.NotificationInbox` now has one additional column:

| Column  | Type               | Nullable | Notes                                                    |
|---------|--------------------|----------|----------------------------------------------------------|
| BatchId | `UNIQUEIDENTIFIER` | Yes      | One GUID per logical send. Historical rows were backfilled. |

Supporting index:

```sql
CREATE INDEX IX_NotificationInbox_BatchId
    ON dbo.NotificationInbox(BatchId)
    INCLUDE (IsRead, IsDeleted);
```

Existing rows (pre-feature) were backfilled by grouping on
`(NotificationTypeId, Title, Body, CreatedAt-to-second)`, so the statistics
endpoints also surface historical sends.

---

## How `BatchId` is produced

- `SendNotificationCommandHandler` and `SendNotificationByFilterCommandHandler`
  generate **one** `Guid.NewGuid()` at the start of the request and pass it to
  `NotificationInboxHelper.CreateNotificationInboxRecordsAsync(..., batchId)`.
- The helper stamps every inbox row created for that send with the same GUID.
- If a caller invokes the helper directly without a `batchId`, the helper
  generates one automatically so no send is ever untraceable.

---

## Endpoints

Base URL: `/api/notifications`

All endpoints require a valid JWT (`RequireAuthorization()`) and are intended
for admin callers.

### 1) GET `/api/notifications/batches`

Paginated list of notification sends, one row per send, with aggregated
recipient / read statistics.

#### Query parameters

| Name                | Type       | Default | Description                                     |
|---------------------|------------|---------|-------------------------------------------------|
| `pageNumber`        | int        | 1       | 1-based page index                              |
| `pageSize`          | int        | 20      | Max 100                                         |
| `notificationTypeId`| int?       | null    | Filter by `NotificationType.Id`                 |
| `fromDate`          | DateTime?  | null    | `SentAt >= fromDate`                            |
| `toDate`            | DateTime?  | null    | `SentAt <= toDate`                              |

#### 200 Response — `GetNotificationBatchesDto`

```json
{
  "batches": [
    {
      "batchId": "a0c2ef4d-7b68-4e62-9b4f-3bce88c0a101",
      "notificationTypeId": 1,
      "notificationTypeName": "system_announcement",
      "title": "🎁 المكافآت وصلت",
      "body": "...",
      "deepLink": null,
      "sentAt": "2026-04-18T06:58:04.347",
      "totalRecipients": 428,
      "readCount": 112,
      "unreadCount": 316,
      "readRate": 26.17
    }
  ],
  "totalCount": 5,
  "pageNumber": 1,
  "pageSize": 20,
  "totalPages": 1,
  "hasPreviousPage": false,
  "hasNextPage": false
}
```

#### Field semantics
- **totalRecipients** — number of `NotificationInbox` rows with this `BatchId`
  (i.e. how many users the send actually reached the inbox for).
- **readCount** — rows where `IsRead = 1`.
- **unreadCount** — `totalRecipients - readCount`.
- **readRate** — `readCount * 100 / totalRecipients`, rounded to 2 decimals.
- Soft-deleted rows (`IsDeleted = 1`) are excluded.
- Only rows with `BatchId IS NOT NULL` are considered.

#### Example
```
GET /api/notifications/batches?pageNumber=1&pageSize=20&notificationTypeId=1
Authorization: Bearer <jwt>
```

---

### 2) GET `/api/notifications/batches/{batchId}`

Full statistics **plus** the recipient breakdown for a single send.

#### Path parameter
| Name      | Type | Description                                   |
|-----------|------|-----------------------------------------------|
| `batchId` | Guid | GUID returned by the list endpoint            |

#### 200 Response — `NotificationBatchDetailDto`

```json
{
  "batchId": "a0c2ef4d-7b68-4e62-9b4f-3bce88c0a101",
  "notificationTypeId": 1,
  "notificationTypeName": "system_announcement",
  "title": "🎁 المكافآت وصلت",
  "body": "...",
  "deepLink": null,
  "data": null,
  "sentAt": "2026-04-18T06:58:04.347",
  "totalRecipients": 428,
  "readCount": 112,
  "unreadCount": 316,
  "readRate": 26.17,
  "recipients": [
    {
      "userId": 101,
      "userName": "Ahmad Ali",
      "phone": "+962790000000",
      "email": "ahmad@example.com",
      "isRead": true,
      "receivedAt": "2026-04-18T06:58:04.347"
    }
  ]
}
```

- Recipients are ordered **read first**, then by `UserId`, so the admin can
  quickly scan who opened it vs. who hasn't.
- `receivedAt` is the inbox row's `CreatedAt`.

#### 404 Response
Returned as the project's standard `NotFoundException` when no rows exist with
the given `BatchId` (or all such rows are soft-deleted).

---

## Status & error codes

| Status | Meaning                                                                 |
|--------|-------------------------------------------------------------------------|
| 200    | OK                                                                      |
| 401    | Unauthenticated (`RequireAuthorization()`)                              |
| 404    | `batchId` not found (detail endpoint only)                              |
| 500    | Unhandled server error                                                  |

---

## Implementation map

| Layer           | File                                                                                                |
|-----------------|-----------------------------------------------------------------------------------------------------|
| Domain          | `Domain/Enitites/NotificationInbox.cs` — `Guid? BatchId`                                            |
| Persistence     | `Infrastructrue/Persistence/Configurations/NotificationInboxConfiguration.cs` — mapping + index     |
| Helper          | `Application/NotificationInbox/Helpers/NotificationInboxHelper.cs` — accepts optional `batchId`     |
| Send handlers   | `Application/NotificationInbox/Commands/SendNotification/SendNotificationCommand.cs`                |
|                 | `Application/NotificationInbox/Commands/SendNotificationByCategory/SendNotificationByCategoryCommand.cs` |
| List query      | `Application/NotificationInbox/Queries/GetNotificationBatches/*`                                    |
| Detail query    | `Application/NotificationInbox/Queries/GetNotificationBatchById/*`                                  |
| Routes          | `WebApi/Routes/NotificationInboxRoutes.cs` — `/batches` and `/batches/{batchId:guid}`               |

---

## Backward compatibility

- `BatchId` is nullable; old rows that were never backfilled would simply be
  hidden from the statistics endpoints (the list query filters
  `WHERE BatchId IS NOT NULL`).
- The existing send endpoints are **unchanged** in their request / response
  contract. Clients continue to call `POST /api/notifications` and
  `POST /api/notifications/send-by-filter` exactly as before.
- `IsRead` semantics (and the `PUT /{id}/mark-as-read` endpoint) are unchanged;
  the statistics endpoints simply aggregate the same column.

---

## Operational notes

- The list query scales with the number of distinct `BatchId`s, not the number
  of inbox rows, thanks to `IX_NotificationInbox_BatchId`.
- For very large historical volumes, future enhancement options include:
  materialising the per-batch aggregates into a dedicated
  `NotificationCampaign` table, tracking per-row delivery status
  (`Pending / Delivered / Failed / TokenInvalid`), and exposing a dedicated
  `ReadAt` timestamp.
