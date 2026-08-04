# User Complaint Statuses — Expanded Workflow

## Overview
The complaint workflow now supports **seven** statuses (was three) so that admins
can model the full lifecycle of a complaint, from submission through investigation
to one of several terminal states.

`UserComplaint.Status` is stored as `INT NOT NULL` in `dbo.UserComplaints`. Values
come from the `Cable.Core.Emuns.ComplaintStatus` enum.

## Status reference

| Value | Name           | Meaning                                                                 |
|-------|----------------|-------------------------------------------------------------------------|
| 0     | `New`          | **Default** for a freshly submitted complaint. No admin action yet.     |
| 1     | `NotComplaint` | Admin reviewed it and determined it is not actually a complaint.        |
| 2     | `Solved`       | Complaint resolved.                                                     |
| 3     | `Opened`       | Admin started working on the complaint.                                 |
| 4     | `FollowUp`     | Waiting on the user or station for additional information.             |
| 5     | `Unsolved`     | Investigation closed, fix not possible.                                |
| 6     | `SystemIssue`  | Root cause is in the Cable platform itself, not the station.          |

### Why these specific numeric values
The numbers `0`, `1`, `2` are kept stable to preserve historical data without a
backfill migration:
- `0` used to be `Pending` and is now `New` (same role: default for fresh complaints).
- `1` used to be `Rejected` and is now `NotComplaint` (closest semantic fit; no rows existed at this value in production).
- `2` was `Solved` and remains `Solved`.
- The four new statuses are appended starting at `3`.

## Suggested workflow

```
                    ┌────────────────────────┐
   submission       │                        │  admin
   ───────────────▶ │          New (0)       │ ─────▶ NotComplaint (1)
                    │                        │
                    └────────────┬───────────┘
                                 │ admin starts working
                                 ▼
                    ┌────────────────────────┐
              ┌───▶ │        Opened (3)      │
              │     └─────┬──────┬─────┬─────┘
              │           │      │     │
              │  needs    │      │     │
              │  more     ▼      │     ▼
              │   info  FollowUp (4)   SystemIssue (6)
              └────────────┘      │
                                  ▼
                          ┌────────────┬────────────┐
                          │ Solved (2) │ Unsolved (5)│
                          └────────────┴────────────┘
```

Transition rules are **not** enforced server-side today — any admin call to
`PUT /api/user-complaints/{id}/status` can move the complaint to any value
allowed by the enum. If you later want strict transitions, add the rule set
inside `UpdateUserComplaintStatusCommandHandler`.

## Database changes

### Dev DB (already applied)
- A `CHECK` constraint was added so invalid values can never be inserted:
  ```sql
  ALTER TABLE dbo.UserComplaints
      ADD CONSTRAINT CK_UserComplaints_Status_Range
      CHECK (Status BETWEEN 0 AND 6);
  ```
- The pre-existing auto-named `DEFAULT 0` constraint on `Status` was kept as-is
  (its name is `DF__UserCompl__Statu__740F363E`).

### Production rollout (run when deploying)
```sql
SET XACT_ABORT ON;

-- 1) Ensure a DEFAULT 0 constraint exists (production already has it auto-named)
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.UserComplaints')
      AND parent_column_id = (
            SELECT column_id FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.UserComplaints') AND name = N'Status'))
BEGIN
    ALTER TABLE dbo.UserComplaints
        ADD CONSTRAINT DF_UserComplaints_Status DEFAULT 0 FOR Status;
END;

-- 2) Range CHECK
IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_UserComplaints_Status_Range'
      AND parent_object_id = OBJECT_ID(N'dbo.UserComplaints'))
BEGIN
    ALTER TABLE dbo.UserComplaints
        ADD CONSTRAINT CK_UserComplaints_Status_Range
        CHECK (Status BETWEEN 0 AND 6);
END;
```

**No data migration is required.** All 139 production rows are at `Status = 0`,
which is exactly the new `New` status.

## Code changes

| File | Change |
|---|---|
| `Cable.Core/Enums/ComplaintStatus.cs` | Replaced 3-value enum with 7-value enum (kept `0`, `1`, `2` numerics stable). |
| `Domain/Enitites/UserComplaint.cs`     | Added explicit `= (int)ComplaintStatus.New` initializer + XML docs. |
| `Infrastructrue/Persistence/Configurations/UserComplaintConfiguration.cs` | Made `Status` required + `HasDefaultValue(0)`. |
| `Application/UserComplaints/Command/UpdateUserComplaintStatus/UpdateUserComplaintStatusCommandValidator.cs` | **No change needed** — already validates with `IsInEnum()`, which auto-extends with the new enum values. |

## API impact

- `POST /api/user-complaints` — the request body does **not** include `Status`;
  new complaints are saved with `Status = New (0)` by default.
- `PUT /api/user-complaints/{id}/status` (admin) — body accepts any value from
  the new enum (`0..6`). Invalid values are rejected by FluentValidation.
- All list/get endpoints continue to expose `Status` as an `int`.

## Frontend checklist
- Update the admin complaint dropdown to display the seven new options.
- Map the integer to a display label using the names above (or English/Arabic
  translations from your localization layer).
- Replace any hardcoded reference to "Pending" / "Rejected" with `New` /
  `NotComplaint` (or, better, drive the labels from the enum).

## Rollback (if ever needed)
```sql
ALTER TABLE dbo.UserComplaints DROP CONSTRAINT CK_UserComplaints_Status_Range;
```
…and revert the enum to its prior three values. No data loss because numeric
values `0`, `1`, `2` retain a valid (if differently-labeled) meaning.
