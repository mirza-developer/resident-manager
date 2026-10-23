# PITS – Business Logic and Bill Calculation (Full Details)

Source of truth: `script.sql` (SQL Server schema, UTF‑16 encoded), the business layer (`c*.cs` in the repo root) and the WinForms presentation layer (`presentation/`, especially `frmUseData.cs`, `frmPeriodUseData.cs`, `frmFinalBillData.cs`, `cCalculate.cs`, `cConvert.cs`).

> **Verification status of this document.** The tables, constraints, both calculation views (`VIEW_tblUse_CalculateUse`, `VIEW_tblFinalBill_CalculateUse`), the `FUN_*` functions and the Bill / FinalBill / Home procedures were checked line by line against `script.sql`. Statements about the C# code (`cCalculate`, `frmUseData`, UI rounding, date conversion), about the Pattern / PeriodUse / Use stored procedures (soft delete, `SP_tblUse_LastUse`) and about the UTF‑16 encoding come from the original code analysis and were **not** re‑verified in the latest review. They are marked *(code analysis)* where relevant.

## 1. Purpose of the application

PITS (Town) is a desktop application (VB‑derived C#/WinForms + SQL Server) for a residential complex/town that receives **one bulk water bill** from the water authority and must **split it among the homes (units)** based on each unit's own cold‑ and hot‑water meters. Dates are Persian (Shamsi, `yyyy/MM/dd`, stored as `varchar(10)`); money is in Rial (displayed with thousands separators via `cFormat.Currency`).

Layers:

| Layer        | Files                                                                                               | Role                                                                                        |
|--------------|-----------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------|
| Presentation | `presentation/frm*.cs`, `cCalculate.cs`, `cConvert.cs`, `cFormat.cs`, `rptPrint.rpt`                | Forms, live (client-side) preview calculations, printing                                    |
| Business     | `cBill`, `cFinalBill`, `cHome`, `cPattern`, `cPeriodUse`, `cUse`, `cPassword`, `cBackup`, `cEnter`… | Thin wrappers; call stored procedures/functions through `cConnection`                       |
| Database     | `script.sql`                                                                                        | Tables, views (the real calculation engine), table-valued functions, CRUD stored procedures |

## 2. Entities (tables)

| Table            | Meaning                                                                                    | Key columns / rules                                                                                                                                                                                                  |
|------------------|--------------------------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `tblHome`        | A unit/home                                                                                | `fldNumber` (SMALLINT, unique business no.), `fldBlockNumber` (TINYINT), `fldTel` (VARCHAR(8), unique), `fldName` (owner), `fldMobile`, `fldRent` (BIT, nullable) + tenant name/mobile (`fldName1`, `fldMobile1`, nullable) |
| `tblPeriodUse`   | A meter‑reading period ("دوره کنترخوانی")                                                  | `fldPrivateId` (SMALLINT, unique period no.), `fldStartDate`, `fldEndDate` (each unique), `fldPeriod` = length in **days** (TINYINT, max 255), `fldReader`                                                           |
| `tblUse`         | One home's meter readings in one reading period                                            | `fldLastDigitCold/Hot`, `fldNewDigitCold/Hot` (BIGINT), `fldPatternId` (nullable FK). Unique (`fldPeriodUseId`,`fldHomeId`)                                                                                          |
| `tblPattern`     | Tiered tariff ("الگو")                                                                     | `fldPrivateId` (TINYINT, tier no.), `fldToDigit` (INT, upper bound of monthly consumption), `fldPrice` (MONEY, price **per 30‑day month** for that tier), `fldDel` (TINYINT; 0 = active; >0 = archived version). Unique (`fldPrivateId`,`fldDel`) |
| `tblBill`        | The water authority's bill received for the whole complex                                  | `fldPrivateId` (VARCHAR(5), unique), start/end date, `fldPeriod` (TINYINT, days), `fldUse` (BIGINT, total consumption billed), `fldPrice` (MONEY, total amount), `fldTax` (MONEY)                                      |
| `tblFinalBill`   | The "final bill" run: allocation of one `tblBill` to one `tblPeriodUse`. Stores aggregates | `fldTotalHomeUse/Price/Count` (Count is SMALLINT), `fldTotalSharedUse/Price`, `fldIncreaser` (FLOAT), `fldDescription`. Unique (`fldPeriodUseId`,`fldBillId`)                                                        |
| `tblPITSSetting` | Company name / version (single row, `fldPITSId` BIT = 1)                                   | `fldComponyName` and `fldVersion` are each unique                                                                                                                                                                    |
| `tblUsers`       | Single password row (`fldPassword`)                                                        | No key                                                                                                                                                                                                               |

Foreign keys: `tblUse`→`tblHome`, `tblPeriodUse`, `tblPattern`; `tblFinalBill`→`tblPeriodUse`, `tblBill`.

Type limits worth knowing: `fldPeriod` (reading period and bill) is `TINYINT`, so a period cannot exceed 255 days; tier numbers (`tblPattern.fldPrivateId`) and archive counters (`fldDel`) are `TINYINT`, so at most 255 tiers and 255 archived versions per tier; `tblFinalBill.fldTotalHomeCount` is `SMALLINT`.

## 3. Workflow

1. Maintain **Homes** (`SP_tblHome_*`).
2. Maintain **Tariff patterns** (`SP_tblPattern_*`). Edit/delete are *soft*: the old row gets `fldDel = (count of archived rows for that tier)+1` and, on update, a new row with `fldDel = 0` is inserted, so historic `tblUse.fldPatternId` references still point to the tariff that was valid then. *(code analysis)*
3. Create a **Reading period** (`tblPeriodUse`): start date, end date; the form computes the length (`cCalculate.DeffDate`, below).
4. Enter **Readings** per home for that period (`tblUse`). `SP_tblUse_LastUse` returns the home's latest `NewDigit*` values (ordered by period number desc) to pre‑fill the "last digit" fields. *(code analysis)* The form computes consumption, monthly average, matches a tariff and shows the price preview (section 4).
5. Enter the authority's **Bill** (`tblBill`): total use, total price, tax, period.
6. Issue the **Final bill** (`SP_tblFinalBill_INSERT`) combining one reading period + one authority bill. Each home's final payable amount is produced by view `VIEW_tblFinalBill_CalculateUse` (section 6).
7. Print (`FUN_tblFinalBill_LoadReport`, `rptPrint.rpt`).

## 4. Per‑home consumption and price (step 4)

### 4.1 Period length (`cCalculate.DeffDate`) *(code analysis)*

`Period (days) = DateDiff("d", Miladi(StartDate), Miladi(EndDate))` after converting Shamsi dates with `PersianCalendar` (`cConvert.ShamsiToMiladi`). The end date is *not* inclusive (end − start). The stored value is `tblPeriodUse.fldPeriod`; the views always use the stored value, never recompute it from the dates.

### 4.2 Consumption

```
TotalUseCold = NewDigitCold − LastDigitCold
TotalUseHot  = NewDigitHot  − LastDigitHot
TotalUse     = TotalUseCold + TotalUseHot
```

There is **no validation** that new ≥ last in SQL, and meter roll‑over (the meter wrapping back to 0) is not handled: it would produce a negative consumption that flows straight into the totals. Units are those of the meter, i.e. litres in the tariff scale below (inferred from the `/ 1000` in the fallback formula).

### 4.3 Monthly‑normalised average (`cCalculate.AverageForMonth`, `CalculateAverage`, view column `fldAverageUse`)

```
AverageUse = TotalUse / Period × 30
```

UI rounds to an integer (`Math.Round`); the view keeps it as FLOAT. A `Period` of 0 (start date = end date) causes a divide‑by‑zero in the view.

### 4.4 Tariff selection (`FindPattern`, `FUN_tblPattern_Select_Pattern`)

```
SELECT TOP 1 ... FROM tblPattern WHERE fldToDigit >= @AverageUse AND fldDel = 0
```

i.e. the first active tier whose upper limit is ≥ the monthly average. **Note:** the query has no `ORDER BY`; correctness relies on the engine returning the lowest tier first (typically clustered‑index/insert order). Adding `ORDER BY fldToDigit` would make it deterministic. The selected `fldPatternId` is stored in `tblUse.fldPatternId`.

**Flat (non‑progressive) tariff.** The tier model is a *bracket* price, not a progressive one: the single matched tier's `fldPrice` is charged for the **whole** month, not just for the part of the consumption that falls inside that tier. A home that moves from one tier to the next therefore jumps to the next tier's full price.

**The price is frozen at entry time.** The calculation view does not re‑match the tier from the readings. It joins `tblPattern` through the stored `tblUse.fldPatternId` and uses that row's price. If a reading is edited, the stored `fldPatternId` must be recalculated and re‑saved by the UI; the database will not do it. Because patterns are soft‑deleted, the price of an old reading keeps following the tariff that was valid when it was entered.

### 4.5 Monthly price

```
MonthPrice =
   Pattern.fldPrice                                   if a tier matched
   (AverageUse / 1000) × 56 625 − 2 311 016           if no tier matched (pattern shown as "!")
```

Hard‑coded constants (56 625 and 2 311 016) are a fallback linear formula for consumption above the last defined tier. In the view the fallback is applied when the left‑joined `tblPattern.fldPrice IS NULL`, which happens exactly when `tblUse.fldPatternId` is NULL; in that case the view recomputes the price from the stored readings (the only case where it does). The fallback is not guaranteed to be continuous with the last tier's price. If no tiers are defined at all, every home uses the fallback. Both the UI (`frmUseData.FindPattern`) and the view (`VIEW_tblUse_CalculateUse`) implement it.

### 4.6 Price for the actual period, tax and total

```
Price      = MonthPrice × Period / 30
Tax        = Price × 0.04        (4 % VAT / levy)
TotalPrice = Price + Tax
```

- UI (`SetPriceUse`): each of Price and Tax is rounded to whole Rial (`Math.Round`) and `TotalPrice = Price + Tax` of the rounded values. *(code analysis)*
- View: unrounded FLOAT values (so tiny differences vs. the form preview are possible).

Worked example: readings give TotalUse = 45 000, Period = 60 days → Average = 22 500. Suppose the matching tier price is 1 000 000 → Price = 1 000 000 × 60/30 = 2 000 000; Tax = 80 000; TotalPrice = 2 080 000.

Implemented in: `VIEW_tblUse_CalculateUse` (exposed through `FUN_tblUse_Load`, `FUN_tblUse_LoadRecord`).

## 5. The authority's bill (`tblBill`)

Plain data entry (`SP_tblBill_INSERT/UPDATE/DELETE`): `Use` (total water billed to the complex), `Price` (amount), `Tax`, `Period`. Only `fldUse` and `fldPrice` take part in the allocation. Everything else is informational.

**Unused fields.** `fldTax` of the bill is stored but **not used** in any formula, and the bill's dates and `fldPeriod` are only copied to the report. Nothing checks that the bill's dates or length match the reading period it is combined with (see 6.4).

**Is `fldPrice` tax‑inclusive?** The schema does not say. The allocation treats `Bill.fldPrice` as the total amount to be recovered from the homes and the shared consumption: the final amounts add up to it. In effect it is handled as the final payable amount, and the bill's own tax is ignored. Whoever enters the bill must therefore enter the tax‑inclusive total in `fldPrice`.

**Locks (what is and is not enforced).**
- `FUN_tblBill_CheckLock` returns rows when a final bill uses the bill, `FUN_tblUse_CheckLock` when the reading's period already has a final bill, and `FUN_tblPeriodUse_CheckLock` when the period has **any** readings (not only when a final bill exists).
- These are **advisory**: they are functions that the application layer calls. The database has no triggers or check constraints, so a direct SQL `UPDATE` of readings after a final bill is not blocked.
- The only database‑level protection is the foreign keys, which stop deleting a bill or period that a final bill references.

## 6. Final bill allocation (`SP_tblFinalBill_INSERT` + `VIEW_tblFinalBill_CalculateUse`)

### 6.1 Aggregates saved at issue time

From `VIEW_tblUse_CalculateUse` for the chosen period (`@PeriodUseId`) and `tblBill` (`@Use`, `@BillPrice`):

```
TotalHomeUse   = Σ TotalUse            (all homes in period)
TotalHomePrice = Σ TotalPrice          (Price + 4 % tax, per home)
TotalHomeCount = COUNT(homes)
TotalSharedUse   = Bill.Use − TotalHomeUse                      -- water not metered by homes (common areas, loss)
TotalSharedPrice = Bill.Price × TotalSharedUse / Bill.Use       -- its pro‑rata share of the authority's price
Increaser        = (Bill.Price − TotalSharedPrice) / TotalHomePrice
```

`Increaser` is the scaling factor that makes the homes' tariff‑based amounts sum to the part of the authority's bill attributable to homes (`Bill.Price − TotalSharedPrice`). Because it scales `TotalPrice`, it scales the 4 % tax as well. All values are stored in `tblFinalBill`; `SELECT SCOPE_IDENTITY()` returns the new id. The procedure resolves the business numbers to ids with `FUN_tblPeriodUse_Select_Id` / `FUN_tblBill_Select_Id`. Unique (period, bill) prevents issuing twice.

### 6.2 Per‑home final amount (view `VIEW_tblFinalBill_CalculateUse`)

```
RealPrice       = TotalPrice × Increaser
SharedPrice     = TotalSharedPrice / TotalHomeCount      -- equal share of the shared consumption for every home
TotalRealPrice  = RealPrice + SharedPrice
FinalRealPrice  = ROUND(TotalRealPrice, −3)              -- nearest 1 000 Rial
RoundPrice      = FinalRealPrice − TotalRealPrice        -- rounding adjustment (+/−)
```

`FinalRealPrice` is the amount payable by the home. The shared share is split **equally** between all homes, regardless of size or consumption; `fldRent` and tenant data play no role in the amounts. The view joins `tblFinalBill` with `tblBill` and with `VIEW_tblUse_CalculateUse` on `fldPeriodUseId`, so each final bill yields one row per home reading of that period, and carries the bill's own number/dates/period/use/price for the report.

> Caveat: the view joins `tblFinalBill` ↔ `tblUse` only on period; if several authority bills were issued for the same period, home rows would be repeated per bill. Filters in `FUN_tblFinalBill_Load*` (`@PrivateIdBill`) select the desired bill.

### 6.3 Worked example

Bill: Use = 100 000, Price = 50 000 000. Homes (2): TotalUse = 40 000 and 50 000 → ΣUse = 90 000; ΣTotalPrice (tier prices + tax) = 36 000 000.

```
SharedUse   = 100 000 − 90 000 = 10 000
SharedPrice = 50 000 000 × 10 000 / 100 000 = 5 000 000
Increaser   = (50 000 000 − 5 000 000) / 36 000 000 = 1.25
```

Home A with TotalPrice 16 000 000: RealPrice = 20 000 000; shared = 5 000 000 / 2 = 2 500 000; Total = 22 500 000 → Final 22 500 000, Round 0. Sum of the unrounded amounts of all homes equals Bill.Price, because Σ(RealPrice) = Bill.Price − SharedPrice and Σ(share) = SharedPrice. The sum of the **rounded** final amounts can differ from Bill.Price by the sum of the `RoundPrice` values, since the rounding is per home and is not reconciled.

Rounding example: if a home's TotalRealPrice is 22 499 600, FinalRealPrice = 22 500 000 and RoundPrice = +400.

### 6.4 Business assumptions behind the allocation

- **Periods are assumed to match.** The reading period and the authority's bill are assumed to cover the same dates. Nothing compares `tblBill.fldStartDate/fldEndDate/fldPeriod` with the reading period, and `Bill.Use` is compared with `ΣTotalUse` as is. If the periods differ, the "shared use" absorbs the difference (it can be wrong or even negative).
- **Shared consumption** is everything the authority billed that the homes' meters did not record (common areas, leaks, meter error), and it is charged equally to all homes.
- **Negative shared use** (homes recorded more than the authority billed) makes `TotalSharedPrice` negative, which turns into an equal credit per home.

## 7. Data fields exposed to forms/reports

| Function                                                                                 | Purpose                                                                                                                    |
|------------------------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------|
| `FUN_tblUse_Load(period, number, name, block)`                                           | Reading list with computed use/price                                                                                       |
| `FUN_tblFinalBill_Load(...)`                                                             | Final bill grid (use, patterns, TotalPrice, SharedPrice, RealPrice, TotalRealPrice, Increaser, RoundPrice, FinalRealPrice) |
| `FUN_tblFinalBill_LoadRecord(useId)`                                                     | Single home detail                                                                                                         |
| `FUN_tblFinalBill_LoadReport(...)`                                                       | Printable bill (adds meter digits)                                                                                         |
| `FUN_tblPattern_Load/LoadRecord`, `FUN_tblHome_*`, `FUN_tblPeriodUse_*`, `FUN_tblBill_*` | Searches (empty filter ⇒ `%`, non-empty ⇒ exact match; names use `LIKE %x%`)                                              |

All CRUD procedures return an `@ErrorMessage` OUTPUT (`''` on success); insert procedures also return `SCOPE_IDENTITY()`. The UI maps constraint names in the message (e.g. `UQ_fldPeriodUseIdfldBillId_tblFinalBill`, `'fldPeriodUseId'`) to Persian user messages *(code analysis)*; see risk 6 for why this only works if the error text is actually returned. Deleting a final bill is by (period no., bill no.): the first parameter, named `@Identity`, is actually the period's business number (`fldPrivateId`), not an id. `SP_Backup` backs up a database named `[PITS-DB]`, while the script itself creates objects in `[PITS-Town-DB]` (see risk 9). `tblUsers`/`cPassword` guard application entry.

## 8. Observations / risks

1. Tariff lookup lacks `ORDER BY fldToDigit` (non‑deterministic in theory).
2. Magic constants 0.04 (tax), 30 (days/month), 56 625 / 2 311 016 (out‑of‑tier fallback), rounding to −3 are hard‑coded in both SQL and C#.
3. UI preview rounds Price/Tax to integers; DB view does not → possible small differences between screen and report.
4. **Zero totals.** `Increaser` divides by `TotalHomePrice` and `TotalSharedPrice` by `Bill.Use`; `fldAverageUse` divides by `fldPeriod`. A zero in any of them raises a divide‑by‑zero error. A negative `TotalSharedUse` (homes consumed more than billed) makes the shared price negative (credit).
5. **Integer overflow.** Several procedures declare `@Identity TINYINT` (`SP_tblBill_DELETE`, `SP_tblHome_DELETE`, and the other `SP_tbl*_DELETE` procedures of the same pattern), which overflows for ids > 255. `FUN_tblBill_CheckLock`, `FUN_tblPeriodUse_CheckLock` and `FUN_tblUse_CheckLock` take `SMALLINT` ids, which overflow above 32 767. Period lengths are limited to 255 days (`TINYINT`).
6. **Broken error reporting.** `SP_..._DELETE/INSERT/UPDATE` and `SP_Backup` build the message with `ERROR_NUMBER() + ' - ' + ERROR_MESSAGE()`. `ERROR_NUMBER()` is an int, so SQL Server tries to convert `' - '` to int and raises a conversion error **inside the CATCH block**. As a result `@ErrorMessage` is not set and the caller receives an exception instead. This also affects risk 4: a divide‑by‑zero in `SP_tblFinalBill_INSERT` is caught, but the CATCH block then fails, so the user does not get the intended message. A `CAST(ERROR_NUMBER() AS VARCHAR(10))` is needed.
7. Bill `fldTax` is stored but unused in calculations; the 4 % tax is applied only to home tariff prices, and `Bill.fldPrice` is implicitly treated as tax‑inclusive (section 5).
8. **Locks are application‑level only** (section 5). Direct database access can change readings after a final bill was issued, which would change the printed amounts, because the view recalculates everything except the stored aggregates.
9. **Backup targets the wrong database name.** `SP_Backup` runs `BACKUP DATABASE [PITS-DB]`, but the script uses `[PITS-Town-DB]`. If the database is created under the script's name the backup will fail.
10. **`FUN_tblHome_Load` hides homes with NULL values.** It filters with `fldName1 LIKE '%' + @Name1 + '%'` and `fldRent LIKE '%' + @Rent + '%'`. Both columns are nullable, and `NULL LIKE '%%'` is not true, so homes without a tenant name or without a rent flag never appear in the list.
11. **Price frozen by `fldPatternId`** (section 4.4): editing a reading without re‑storing the tier leaves the old price in the views.
12. **Periods are not cross‑checked** (section 6.4): a bill and a reading period of different length or dates are silently combined.
13. **No protection against meter anomalies** (section 4.2): negative consumption, roll‑over and typing errors flow straight into the allocation.
14. **Rounded sum ≠ bill total.** Per‑home rounding to 1 000 Rial is not reconciled, so the sum of `FinalRealPrice` can differ slightly from `Bill.Price`.

## 9. Findings from real data (period 1405/07, checked against the live PITS DB)

1. **Zero-use homes still pay a tariff.** Homes with use 0 (units 18, 35, 51) are priced on the lowest used tier, so they paid 417,000 Rial: tariff share plus the equal shared share. The new app deliberately differs and charges them only the equal shared share (about 250,000).
2. **Rounded sum is really off.** The old per-home finals summed to 364,641,000 against an authority price of 364,642,000 (1,000 short), which confirms risk 14.
3. **Tier boundaries are order-sensitive.** In decimal arithmetic `use / period * 30` can give 35000.0000...1 for an exact boundary and push the home into the next tier. Compute `use * 30 / period` instead (fixed in the new app).
4. **Bad readings distort everyone.** One wrong reading (e.g. 403,106 instead of 8,500) inflates the sum of tariffs and lowers the multiplier from about 57 to about 13 for all homes. The old system has no outlier check (risk 13).
5. **A reading period can exist without an issued final bill.** Period 177 had `fldIncreaser` NULL, so amounts were computed only through the views/SQL, not stored.
6. **Old tier table.** `tblPattern` ids 218-260 hold tiers 2-44; tier 1 and tiers 45-50 are not in the old data (the new app adds them).

