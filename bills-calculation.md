# Bill Calculation Mechanism

This document describes how monthly resident bills are produced in this app. Sources: `BillingService`, `AuthorityBillCalculator`, `WaterBillingConstants`, `MonthlyUsageRepository`, `BillRepository`, and the Admin pages (Billing, FinancialItems, AuthorityBills) and Worker Usage page. The water allocation follows the legacy PITS formula (`Docs/old-docs.md`). All money is in Rial.

## 1. Overview

A **Bill** is one house's invoice for one month (`Year`, `Month`). It has one **BillItem** per financial-item line, plus a `PreviousDebt` carried from the house. Bills are generated for **all active houses at once**, because equal division and the water allocation need the whole set of houses.

Lifecycle: `Draft` -> `Approved` -> `Paid`.

```
Usage entry (Worker) + Authority bill (Admin) + Financial items / tiers (Admin)
                         |
              BillingService.GenerateBillsAsync   -> Draft bills
                         |
              ApproveBillsAsync                   -> Approved (debt increases, SMS sent)
                         |
              RecordPaymentAsync / online payment -> Paid (debt decreases)
```

## 2. Inputs

### 2.1 Financial items (`FinancialItem`)
| Field | Meaning |
|---|---|
| `CalculationType` | `EqualDivision` or `ParentChildTiering` |
| `PeriodType` | `Permanent`, `Once` (deactivated after approval), `Installment` |
| `TotalAmount`, `NumberOfInstallments`, `InstallmentsBilled` | Installment items bill `TotalAmount / NumberOfInstallments` per month and are deactivated when all installments are billed |
| `ParentId`, `WaterKind` | Water model (2.2) |
| `Tiers` | Tier rows, only on the water **parent** item |

An item is *applicable* when it is active and (for installments) not yet fully billed.

### 2.2 Water model (ParentChildTiering)
One **parent** item (no `ParentId`) owns three **children**:
- `Hot`: hot-meter usage entry only.
- `Cold`: cold-meter usage entry only.
- `Shared`: carries the shared-consumption line and the rounding difference.

Children are never billed on their own; they are produced through the parent. The metered line is attached to the **parent**.

### 2.3 Tiers (`FinancialItemTier`, on the parent)
- `TierOrder` (ascending), `UpperLimit` (inclusive upper bound of the **monthly average** usage; `null` = open-ended top tier), `MonthlyPrice` = **flat price per 30-day month** for that tier.
- `RatePerUnit` is not used for ParentChildTiering.

### 2.4 Meter usage (`MonthlyUsage`)
Per house, per hot/cold child, per month: `PreviousReading`, `CurrentReading`, `UsageCount = max(0, Current - Previous)`.
- **Negative usage is blocked**: the Usage page rejects `Current < Previous`, and `MonthlyUsageRepository.AddAsync/UpdateAsync` throws if `Current < Previous` or `UsageCount < 0`.

### 2.5 Authority bill (`AuthorityBill`)
The bulk bill from the water company, per parent item per month: `TotalUse`, `TotalPrice`, `PeriodDays` (default 30), `Tax` (stored for reference only, not used in allocation).

### 2.6 Constants (`WaterBillingConstants`)
| Name | Value | Use |
|---|---|---|
| `DaysPerMonth` | 30 | Normalizes usage and price to a 30-day month |
| `TaxRate` | 0.04 | Tax on each house's tariff price |
| `HomeRoundingStep` | 1000 | Per-house final water amount rounding |
| `FallbackDivisor` / `FallbackFactor` / `FallbackOffset` | 1000 / 56,625 / 2,311,016 | Fallback monthly price outside the tiers |

## 3. Generation flow (`GenerateBillsAsync`)

1. Load active houses (none -> error).
2. Pick applicable active items that have an amount in `finalAmounts` (chosen in the Billing dialog), excluding water children.
3. For every water parent: resolve Hot/Cold/Shared children (a missing child is an error) and compute the water allocation for all houses (section 5).
4. For each house:
   - An existing `Approved`/`Paid` bill for that month is skipped; an existing `Draft` is deleted and regenerated.
   - Create a Draft bill with `PreviousDebt = max(0, house.CurrentDebt)`.
   - Add one BillItem per applicable item (sections 4 and 5).
5. Equal-division rounding fix-up (section 4).
6. `TotalAmount = RoundUpTo10,000( sum(BillItem.Amount) + PreviousDebt )`. Item amounts stay exact; only the payable total is rounded up (e.g. 132,080,001 -> 132,090,000).
7. Save and write audit logs.

## 4. EqualDivision items

- Amount per house = `finalAmount / activeHouseCount` (`CalculateEqualDivisionAmount`; 0 when there are no houses).
- Installment items use `finalAmount = TotalAmount / NumberOfInstallments`.
- Rounding fix-up: `diff = finalAmount - sum(house amounts)` is added to the **last** house's item, so the line sums exactly to the final amount.

## 5. Water allocation (ParentChildTiering)

Implemented in `AuthorityBillCalculator.Calculate(authorityBill, parentTiers, houseUsages)`.

### 5.1 Preconditions (errors otherwise)
- At least one house.
- `AuthorityBill.TotalUse > 0` and `PeriodDays > 0`.
- If tiers exist, at least one has `MonthlyPrice > 0`.
- The authority bill must exist for the parent/month, and every active house must have hot and cold usage.
- The sum of all tariffs must be > 0.

### 5.2 Per house
```
Use        = Hot + Cold                     (each clamped to >= 0)
Avg        = Use / PeriodDays * 30          (monthly average)
Tier       = first tier with UpperLimit == null or Avg <= UpperLimit
MonthPrice = Tier.MonthlyPrice
             or, if no tier matches / none defined:
             (Avg / 1000) * 56,625 - 2,311,016
Price      = round(MonthPrice * PeriodDays / 30)
Tax        = round(Price * 0.04)
Tariff     = Price + Tax
```
Zero usage gives zero for everything. Rounding is `MidpointRounding.AwayFromZero`.

### 5.3 Reconciling with the authority bill
```
SumUse        = sum(Use of all houses)
SumTariff     = sum(Tariff of all houses)
SharedUse     = Bill.TotalUse - SumUse
SharedPrice   = Bill.TotalPrice * SharedUse / Bill.TotalUse
Increaser     = (Bill.TotalPrice - SharedPrice) / SumTariff
SharedPerHome = SharedPrice / HomeCount
```
`SharedUse` is common-area consumption. If negative, it becomes an **equal credit per home**.

### 5.4 Per-house result
```
RealPrice  = Tariff * Increaser
Metered    = round(RealPrice)                 -> BillItem on the PARENT
TotalReal  = RealPrice + SharedPerHome
Final      = round(TotalReal / 1000) * 1000   (per-house water total)
Shared     = Final - Metered                  -> BillItem on the SHARED child
RoundPrice = Final - TotalReal
```
`Metered + Shared = Final` always; the rounding difference is carried by the shared line.

### 5.5 Worked example
Tiers: T1 up to 100 -> 1,000,000; T2 up to 200 -> 3,000,000. `PeriodDays = 30`.
- House 1: use 50 -> tier 1, Price 1,000,000, Tax 40,000, Tariff 1,040,000.
- House 2: use 150 -> tier 2, Price 3,000,000, Tax 120,000, Tariff 3,120,000.
- Authority bill: TotalUse 200, TotalPrice 5,200,000 -> SharedUse 0, Increaser = 5,200,000 / 4,160,000 = **1.25**.
- Finals: house 1 = 1,300,000; house 2 = 3,900,000.

The fallback formula expects liter-scale usage; small numbers produce a negative price.

## 6. Approval and payment

- **Approve** (`Draft` -> `Approved`): `house.CurrentDebt += max(0, TotalAmount - PreviousDebt)`; an SMS is sent via the template if configured. `Once` items are deactivated; `Installment` items increment `InstallmentsBilled` and are deactivated when complete.
- **Payment** (`Approved` -> `Paid`): a `Payment` of `TotalAmount` is recorded and `house.CurrentDebt -= TotalAmount`.

## 7. Admin workflow

1. Define the water parent item (ParentChildTiering, no parent) and enter its tiers (`MonthlyPrice`).
2. Create Hot, Cold and Shared child items.
3. Workers enter hot/cold meter readings per house.
4. Admin records the authority bill (total use, total price, period days).
5. Admin generates Draft bills in Billing, reviews, then approves.

## 8. Migration note

`MonthlyPrice` was added by migration `20250101000011_AddTierMonthlyPrice` (default 0). Old per-unit rates cannot be converted; re-enter tiers on the parent item. With no tiers, the fallback formula is used.
