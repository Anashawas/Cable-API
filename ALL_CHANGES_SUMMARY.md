# All Changes Summary — Pre-Production Review

Complete list of all changes made during this session, organized by category.

---

## 1. Settlement System Redesign

**Files:** See `SETTLEMENT_REDESIGN_CHANGES.md` for full details.

- Fixed business logic (Cable earns commission, never collects from users)
- Changed monthly → weekly settlements (Sunday-Saturday)
- Unified wallet (merged `LoyaltyCurrentBalance` + `WalletBalance` into one)
- Real-time per-transaction commission deduction
- `WalletCreditLimit` for controlling max debt
- `WalletCoveredAmount` tracking per partner transaction
- `WalletApplied` accumulated atomically in `SettlementService`
- `OutstandingAmount` computed field
- Removed Invoiced status, PaidAmount, InvoicedAt
- Settlement locks after Paid, blocked until week ends
- `GetProviderSettlements` endpoint with `HasDebt` filter
- `AddWalletDeposit` replaced `RecordProviderPayment`

---

## 2. Offer Transactions Affect WalletBalance

**Files:** See `LATEST_CHANGES.md` for full details.

- When user redeems offer → provider's `WalletBalance` increases immediately
- New `WalletCreditedAmount` field on `OfferTransaction`
- New enum values: `OfferPaymentCredit = 7`, `OfferPaymentRefund = 8`
- Settlement `walletCoveredAmount` correctly passes `0` for offers (not `WalletCreditedAmount`)

---

## 3. Minimum Transaction Amount

- New `MinimumTransactionAmount` field on `PartnerAgreement`
- Enforced at `InitiatePartnerTransaction`
- Included in all agreement CRUD and query endpoints

---

## 4. Offer Attachments

- New `OfferAttachment` entity, configuration, and DB table
- Add/Delete/Get attachment endpoints for offers
- New `CableOfferAttachments` enum value in `UploadFileFolders`

---

## 5. Change Owner Bug Fixes

**File:** See `CHANGE_OWNER_BUGFIXES.md` for full details.

- `ChangeChargingPointOwner`: Added ownership check, same-owner validation, role check (RoleId = 4)
- `ChangeServiceProviderOwner`: Added same-owner validation, role check
- Wallet not affected by ownership change (confirmed safe)

---

## 6. DateTime.Now → DateTime.UtcNow

All `DateTime.Now` replaced with `DateTime.UtcNow` across the entire project:

| File | Change |
|------|--------|
| `Infrastructrue/Persistence/Interceptors/AuditableEntitySaveChangesInterceptor.cs` | `DateTime.Now` → `DateTime.UtcNow` |
| `Cable.Security.Jwt/Services/TokenGenerationService.cs` | `DateTime.Now` → `DateTime.UtcNow` |
| `Application/Common/Models/Reports/UtilityInvoiceRequest.cs` | `DateTime.Now` → `DateTime.UtcNow` |
| `Infrastructrue/Persistence/Repositories/SharedLinkRepository.cs` | `DateTime.Now` → `DateTime.UtcNow` |
| `Domain/Enitites/SharedLinkUsage.cs` | `DateTime.Now` → `DateTime.UtcNow` |
| `Application/Sms/Commands/SendOtp/SendOtpCommand.cs` | `DateTime.Now` → `DateTime.UtcNow` |
| `Application/SharedLinks/Commands/ValidateSharedLink/ValidateSharedLinkCommand.cs` | `DateTime.Now` → `DateTime.UtcNow` |
| `Application/SharedLinks/Commands/CreateSharedLink/CreateSharedLinkCommandValidator.cs` | `DateTime.Now` → `DateTime.UtcNow` |
| `WebApi/Routes/ReportRoutes.cs` | `DateTime.Now` → `DateTime.UtcNow` |

---

## 7. Jordan Timezone Converter (Temporary)

**File:** `WebApi/Converters/JordanDateTimeJsonConverter.cs` (NEW)

- All API responses auto-convert UTC → Jordan time (UTC+3)
- Handles both `DateTime` and `DateTime?`
- Cross-platform: Windows (`Jordan Standard Time`) + Linux (`Asia/Amman`)
- Treats `DateTimeKind.Unspecified` as UTC (EF Core reads from DB as Unspecified)
- **Temporary** — remove when mobile handles UTC → local conversion
- Registered in `Program.cs` → `ConfigureJsonSerliaizer`

---

## 8. HasReadUpdateNotes Feature

**File:** See `UPDATE_NOTES_FEATURE.md` for full details.

- New `HasReadUpdateNotes` (bit) field on `UserAccount`
- Returned in login response and all user query endpoints
- `PATCH /api/users/mark-update-notes-read` endpoint
- Admin resets manually: `UPDATE UserAccount SET HasReadUpdateNotes = 0`

---

## 9. Concurrency Protection (Transaction + Row Locking)

### Infrastructure:
- Added `DatabaseFacade Database` property to `IApplicationDbContext`
- Added `FindWithLockAsync` helper in `Application/Common/Extensions/DbContextExtensions.cs`

### Commands protected with `BeginTransactionAsync` + `UPDLOCK, ROWLOCK`:

| Command | What's protected |
|---------|-----------------|
| `InitiatePartnerTransaction` | Credit limit check + balance deduction + audit |
| `CancelPartnerTransaction` | Balance refund + audit |
| `ConfirmPartnerTransaction` | Expired code refund path + completion + points award |
| `ConfirmOfferTransaction` | Points deduction + wallet credit + audit |
| `AddWalletDeposit` | Balance add + audit |
| `RedeemReward` | Balance check + deduct + transaction |
| `CancelRedemption` | Balance refund + counter decrement |
| `AdminAdjustPoints` | Balance adjust + transaction |
| `EndSeason` | Bulk bonus addition (entire loop) |

### Commands using `ExecuteUpdateAsync` (atomic SQL):
| Command | What's protected |
|---------|-----------------|
| `InitiateOfferTransaction` | `CurrentTotalUses` counter increment |
| `RedeemReward` | `CurrentRedemptions` counter increment |
| `CancelRedemption` | `CurrentRedemptions` counter decrement |
| `UpdateChargingPointVisitorsCount` | `VisitorsCount` counter increment |

### Loyalty points moved inside transactions:
- `ConfirmOfferTransaction`: `DeductPointsFromOfferAsync` now inside transaction (rolls back if wallet credit fails)
- `ConfirmPartnerTransaction`: `AwardPointsFromOfferAsync` now inside transaction (rolls back if fails)

### Row locks added to `LoyaltyPointService`:
- `AwardPointsAsync` — UPDLOCK on UserLoyaltyAccount
- `AwardPointsFromOfferAsync` — UPDLOCK on UserLoyaltyAccount
- `DeductPointsFromOfferAsync` — UPDLOCK on UserLoyaltyAccount

---

## 10. Race Condition Fixes (Counters)

| Command | Before | After |
|---------|--------|-------|
| `InitiateOfferTransaction` | `offer.CurrentTotalUses++` (read-then-write) | `ExecuteUpdateAsync` atomic SQL |
| `RedeemReward` | `reward.CurrentRedemptions++` (read-then-write) | `ExecuteUpdateAsync` atomic SQL |
| `CancelRedemption` | `reward.CurrentRedemptions--` (read-then-write) | `ExecuteUpdateAsync` with guard `> 0` |

---

## 11. Offer Usage Counter Decrement (Missing Logic)

- `CancelOfferTransaction`: Added `ExecuteUpdateAsync` to decrement `CurrentTotalUses` when cancelled
- `BackgroundJobService.ExpireOfferTransactionCodesAsync`: Added grouped `ExecuteUpdateAsync` to decrement `CurrentTotalUses` when expired

---

## 12. Bug Fixes

### Critical:
| Bug | File | Fix |
|-----|------|-----|
| Hardcoded localhost URL | `CreateSharedLinkCommand.cs` | Uses `IConfiguration["File:ServerUrl"]` fallback |
| Settlement WalletApplied wrong for offers | `SettlementService.cs` | Changed `walletCoveredAmount` from `WalletCreditedAmount` to `0` for offer transactions |

### High:
| Bug | File | Fix |
|-----|------|-----|
| N+1 queries (3 files) | `GetActivePartners`, `GetAllPartnerAgreements`, `GetMyPartnerTransactions` | Batch load provider names with `ToDictionaryAsync` |
| Negative amount on Deposit/Refund | `AddWalletDeposit` | Validates `Amount > 0` for Deposit and Refund types |

### Medium:
| Bug | File | Fix |
|-----|------|-----|
| Null reference `x.User.Name` | `GetProviderPartnerTransactions` | `x.User != null ? x.User.Name : null` |
| Null reference `t.RecordedByUser.Name` | `GetProviderBalance` | `t.RecordedByUser != null ? t.RecordedByUser.Name : null` |
| `AsEnumerable` before DB filter (4 files) | All attachment Get queries | Replaced with `ToListAsync` + in-memory URL mapping |
| In-memory Sum | `GetWalletBalance` | Changed to `SumAsync` (calculated in DB) |
| Include after Take | `GetUserNotifications` | Moved `Include` before `OrderByDescending` |
| Wrong delete folder | `DeleteChargingPointAttachment` | Changed `CableBanners` → `CableAttachments` |
| Missing CreatedAt/CreatedBy | `AdminAdjustPoints` | Added `CreatedAt = now`, `CreatedBy = adminId` |

---

## 13. New Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/users/GetAllUsers?deletedOnly=true` | Filter to show only deleted accounts |
| `GET` | `/api/users/GetAllUsers?includeDeleted=true` | Show all accounts including deleted |
| `PATCH` | `/api/users/restore` | Restore deleted users (accepts array of IDs) |
| `DELETE` | `/api/files/attachment/{folderName}/{id}` | Delete single attachment by ID from any folder |
| `PATCH` | `/api/users/mark-update-notes-read` | Mark update notes as read for current user |
| `GET` | `/api/offers/GetProviderSettlements` | Get settlements for specific provider with filters |

---

## 14. Staging File Upload Fix

- Changed `appsettings.Staging.json` `FileUploadPath` from `www\files\staging` to `www\dev\files`
- Dev site (`subsite2`) runs in separate app pool with write access only inside its own root

---

## 15. Known Issues (Not Fixed)

| Issue | Severity | Reason |
|-------|----------|--------|
| `ChangeMyPassword` doesn't verify current password | HIGH | Accepts `CurrentPassword` param but never checks it |
| `ChangePassword` (admin) allows empty password | MEDIUM | Sets password to `null` — user can't login |
| No SecurityStamp update after password change | MEDIUM | Old tokens still work after password change |
| Background job `ExpirePartnerTransactionCodes` no row locks | LOW | Runs on schedule, low concurrency risk |

---

## Database Migrations

See `PRODUCTION_MIGRATION_SCRIPT.sql` for the full production migration script.

**Additional migration not in the script:**
```sql
-- Missing from main script — add Status column to UserComplaints
ALTER TABLE UserComplaints ADD Status int NOT NULL DEFAULT 0;

-- Rename Role
UPDATE Role SET Name = 'Provider' WHERE Id = 4;
```
