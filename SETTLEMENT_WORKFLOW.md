# Settlement System Workflow

## What is a Settlement?

A **ProviderSettlement** is a monthly financial summary for each provider. It aggregates all completed transactions for a given month and calculates how much Cable owes each provider.

---

## Two Transaction Types Feed Into Settlements

### 1. Offer Transactions (Cable pays provider)
- User spends **loyalty points** to redeem an offer at a provider
- Cable pays the provider a fixed **MonetaryValue** for each redemption
- Flow: Provider initiates → code generated → user scans → points deducted → Completed

### 2. Partner Transactions (User pays provider, Cable takes commission)
- User pays the provider for a service (e.g., charging)
- Cable retains a **commission percentage**
- User earns **loyalty points** from the transaction
- Flow: User initiates payment → provider confirms → commission deducted → Completed

---

## Settlement Generation (Admin triggers monthly)

**Endpoint:** `POST /api/offers/GenerateSettlement` with `{ Year, Month }`

### Step-by-step:

1. **Collect Offer Transactions** for the month (only `Completed` status)
   - Group by (ProviderType, ProviderId)
   - Sum: MonetaryValue, PointsDeducted, transaction count

2. **Collect Partner Transactions** for the month (only `Completed` status)
   - Group by (ProviderType, ProviderId)
   - Sum: TransactionAmount, CommissionAmount, PointsAwarded, transaction count

3. **Upsert Settlement** for each provider:
   - If settlement exists for that provider+month → **accumulate** values
   - If not → **create new** with status = `Pending(1)`

4. **Calculate Net Amount:**
   ```
   NetAmountDueToProvider = (PartnerTransactionAmount - PartnerCommissionAmount) + OfferPaymentAmount
   ```

### Example:

| Item | Amount |
|------|--------|
| Partner transactions total | 1,000 JOD |
| Commission (Cable's cut, 20%) | -200 JOD |
| Provider net from partners | 800 JOD |
| Offer payments (Cable pays) | +150 JOD |
| **Net due to provider** | **950 JOD** |

---

## Settlement Status Lifecycle

```
Pending(1) ──→ Invoiced(2) ──→ Paid(3)
                    │
                    ↓
               Disputed(4) ──→ Paid(3)
```

| Status | Meaning | Fields Set |
|--------|---------|------------|
| Pending (1) | Just generated, awaiting review | — |
| Invoiced (2) | Admin sent invoice | `InvoicedAt` |
| Paid (3) | Payment completed | `PaidAt`, `PaidAmount` |
| Disputed (4) | Under dispute | `AdminNote` |

**Endpoint:** `PUT /api/offers/UpdateSettlementStatus/{id}` with `{ Status, PaidAmount?, Note? }`

---

## All Settlement Endpoints

| Endpoint | Who | Purpose |
|----------|-----|---------|
| `POST /api/offers/GenerateSettlement` | Admin | Generate monthly settlements |
| `GET /api/offers/GetSettlements` | Admin | List all (filter by status/month/year) |
| `GET /api/offers/GetSettlementSummary` | Admin | Dashboard totals across all providers |
| `GET /api/offers/GetProviderSettlement` | Provider | View own settlement for a month |
| `PUT /api/offers/UpdateSettlementStatus/{id}` | Admin | Update status (Invoiced/Paid/Disputed) |

---

## Key Business Rules

1. **One settlement per provider per month** (unique DB constraint)
2. **Only Completed transactions** are included — Expired/Cancelled are excluded
3. **Idempotent generation** — running it twice for the same month accumulates, doesn't duplicate
4. **PaidAmount can differ** from NetAmountDueToProvider (for partial payments or disputes)
5. **Decimal precision:** 3 decimal places for all monetary amounts
