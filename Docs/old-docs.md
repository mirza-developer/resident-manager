# PITS – Business Logic and Bill Calculation (Full Details)

Source of truth: `script.sql` (SQL Server schema, UTF‑16 encoded), the business layer (`c*.cs` in the repo root) and the WinForms presentation layer (`presentation/`, especially `frmUseData.cs`, `frmPeriodUseData.cs`, `frmFinalBillData.cs`, `cCalculate.cs`, `cConvert.cs`).

## 1. Purpose of the application

PITS (Town) is a desktop application (VB‑derived C#/WinForms + SQL Server) for a residential complex/town that receives **one bulk water bill** from the water authority and must **split it among the homes (units)** based on each unit's own cold‑ and hot‑water meters. Dates are Persian (Shamsi, `yyyy/MM/dd`, stored as `varchar(10)`); money is in Rial (displayed with thousands separators via `cFormat.Currency`).

Layers:
| Layer | Files | Role |
|---|---|---|
| Presentation | `presentation/frm*.cs`, `cCalculate.cs`, `cConvert.cs`, `cFormat.cs`, `rptPrint.rpt` | Forms, live (client-side) preview calculations, printing |
| Business | `cBill`, `cFinalBill`, `cHome`, `cPattern`, `cPeriodUse`, `cUse`, `cPassword`, `cBackup`, `cEnter`… | Thin wrappers; call stored procedures/functions through `cConnection` |
| Database | `script.sql` | Tables, views (the real calculation engine), table-valued functions, CRUD stored procedures |

## 2. Entities (tables)

| Table | Meaning | Key columns / rules |
|---|---|---|
| `tblHome` | A unit/home | `fldNumber` (unique business no.), `fldBlockNumber`, `fldTel` (unique), `fldName` (owner), mobile, `fldRent` + tenant name/mobile |
| `tblPeriodUse` | A meter‑reading period ("دوره کنترخوانی") | `fldPrivateId` (unique period no.), `fldStartDate`, `fldEndDate` (both unique), `fldPeriod` = length in **days**, `fldReader` |
| `tblUse` | One home's meter readings in one reading period | `fldLastDigitCold/Hot`, `fldNewDigitCold/Hot`, `fldPatternId` (nullable FK). Unique (`fldPeriodUseId`,`fldHomeId`) |
| `tblPattern` | Tiered tariff ("الگو") | `fldPrivateId` (tier no.), `fldToDigit` (upper bound of monthly consumption), `fldPrice` (price **per 30‑day month** for that tier), `fldDel` (0 = active; >0 = archived version) |
| `tblBill` | The water authority's bill received for the whole complex | `fldPrivateId` (unique), start/end date, `fldPeriod` (days), `fldUse` (total consumption billed), `fldPrice` (total amount), `fldTax` |
| `tblFinalBill` | The "final bill" run: allocation of one `tblBill` to one `tblPeriodUse`. Stores aggregates | `fldTotalHomeUse/Price/Count`, `fldTotalSharedUse/Price`, `fldIncreaser`, `fldDescription`. Unique (`fldPeriodUseId`,`fldBillId`) |
| `tblPITSSetting` | Company name / version (single row, id = 1) | |
| `tblUsers` | Single password row (`fldPassword`) | |

Foreign keys: `tblUse`→`tblHome`, `tblPeriodUse`, `tblPattern`; `tblFinalBill`→`tblPeriodUse`, `tblBill`.

## 3. Workflow

1. Maintain **Homes** (`SP_tblHome_*`).
2. Maintain **Tariff patterns** (`SP_tblPattern_*`). Edit/delete are *soft*: the old row gets `fldDel = (count of archived rows for that tier)+1` and, on update, a new row with `fldDel = 0` is inserted, so historic `tblUse.fldPatternId` references still point to the tariff that was valid then.
3. Create a **Reading period** (`tblPeriodUse`): start date, end date; the form computes the length (`cCalculate.DeffDate`, below).
4. Enter **Readings** per home for that period (`tblUse`). `SP_tblUse_LastUse` returns the home's latest `NewDigit*` values (ordered by period number desc) to pre‑fill the "last digit" fields. The form computes consumption, monthly average, matches a tariff and shows the price preview (section 4).
5. Enter the authority's **Bill** (`tblBill`): total use, total price, tax, period.
6. Issue the **Final bill** (`SP_tblFinalBill_INSERT`) combining one reading period + one authority bill. Each home's final payable amount is produced by view `VIEW_tblFinalBill_CalculateUse` (section 6).
7. Print (`FUN_tblFinalBill_LoadReport`, `rptPrint.rpt`).

## 4. Per‑home consumption and price (step 4)

### 4.1 Period length (`cCalculate.DeffDate`)
`Period (days) = DateDiff("d", Miladi(StartDate), Miladi(EndDate))` after converting Shamsi dates with `PersianCalendar` (`cConvert.ShamsiToMiladi`). The end date is *not* inclusive (end − start).

### 4.2 Consumption
```
TotalUseCold = NewDigitCold − LastDigitCold
TotalUseHot  = NewDigitHot  − LastDigitHot
TotalUse     = TotalUseCold + TotalUseHot
```
(No validation that new ≥ last is done in SQL; units are those of the meter, i.e. litres in the tariff scale below.)

### 4.3 Monthly‑normalised average (`cCalculate.AverageForMonth`, `CalculateAverage`, view column `fldAverageUse`)
```
AverageUse = TotalUse / Period × 30
```
UI rounds to an integer (`Math.Round`); the view keeps it as FLOAT.

### 4.4 Tariff selection (`FindPattern`, `FUN_tblPattern_Select_Pattern`)
```
SELECT TOP 1 ... FROM tblPattern WHERE fldToDigit >= @AverageUse AND fldDel = 0
```
i.e. the first active tier whose upper limit is ≥ the monthly average. **Note:** the query has no `ORDER BY`; correctness relies on the engine returning the lowest tier first (typically clustered‑index/insert order). Adding `ORDER BY fldToDigit` would make it deterministic.
The selected `fldPatternId` is stored in `tblUse.fldPatternId`.

### 4.5 Monthly price
```
MonthPrice =
   Pattern.fldPrice                                   if a tier matched
   (AverageUse / 1000) × 56 625 − 2 311 016           if no tier matched (pattern shown as "!")
```
Hard‑coded constants (56 625 and 2 311 016) are a fallback linear formula for consumption above the last defined tier. In the view the fallback is applied when the left‑joined `tblPattern.fldPrice IS NULL`. Both the UI (`frmUseData.FindPattern`) and the view (`VIEW_tblUse_CalculateUse`) implement it; in the fallback case `tblUse.fldPatternId` is NULL, so the view recomputes it from the stored readings.

### 4.6 Price for the actual period, tax and total
```
Price      = MonthPrice × Period / 30
Tax        = Price × 0.04        (4 % VAT / levy)
TotalPrice = Price + Tax
```
- UI (`SetPriceUse`): each of Price and Tax is rounded to whole Rial (`Math.Round`) and `TotalPrice = Price + Tax` of the rounded values.
- View: unrounded FLOAT/MONEY values (so tiny differences vs. the form preview are possible).

Worked example: readings give TotalUse = 45 000, Period = 60 days → Average = 22 500. Suppose the matching tier price is 1 000 000 → Price = 1 000 000 × 60/30 = 2 000 000; Tax = 80 000; TotalPrice = 2 080 000.

Implemented in: `VIEW_tblUse_CalculateUse` (exposed through `FUN_tblUse_Load`, `FUN_tblUse_LoadRecord`).

## 5. The authority's bill (`tblBill`)
Plain data entry (`SP_tblBill_INSERT/UPDATE/DELETE`): `Use` (total water billed to the complex), `Price` (amount), `Tax`, `Period`. `fldTax` of the bill is stored but **not used** in any allocation formula; only `fldUse` and `fldPrice` are. A bill/period cannot be edited/deleted once used (`FUN_tblBill_CheckLock`, `FUN_tblPeriodUse_CheckLock`, `FUN_tblUse_CheckLock` – the last one locks readings of a period that already has a final bill).

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
`Increaser` is the scaling factor that makes the homes' tariff‑based amounts sum to the part of the authority's bill attributable to homes (`Bill.Price − TotalSharedPrice`). All values are stored in `tblFinalBill`; `SELECT SCOPE_IDENTITY()` returns the new id. The procedure resolves the business numbers to ids with `FUN_tblPeriodUse_Select_Id` / `FUN_tblBill_Select_Id`. Unique (period, bill) prevents issuing twice.

### 6.2 Per‑home final amount (view `VIEW_tblFinalBill_CalculateUse`)
```
RealPrice       = TotalPrice × Increaser
SharedPrice     = TotalSharedPrice / TotalHomeCount      -- equal share of the shared consumption for every home
TotalRealPrice  = RealPrice + SharedPrice
FinalRealPrice  = ROUND(TotalRealPrice, −3)              -- nearest 1 000 Rial
RoundPrice      = FinalRealPrice − TotalRealPrice        -- rounding adjustment (+/−)
```
`FinalRealPrice` is the amount payable by the home. The view joins `tblFinalBill` with `tblBill` and with `VIEW_tblUse_CalculateUse` on `fldPeriodUseId`, so each final bill yields one row per home reading of that period, and carries the bill's own number/dates/period/use/price for the report.

> Caveat: the view joins `tblFinalBill` ↔ `tblUse` only on period; if several authority bills were issued for the same period, home rows would be repeated per bill. Filters in `FUN_tblFinalBill_Load*` (`@PrivateIdBill`) select the desired bill.

### 6.3 Worked example
Bill: Use = 100 000, Price = 50 000 000. Homes (2): TotalUse = 40 000 and 50 000 → ΣUse = 90 000; ΣTotalPrice (tier prices + tax) = 36 000 000.
```
SharedUse   = 100 000 − 90 000 = 10 000
SharedPrice = 50 000 000 × 10 000 / 100 000 = 5 000 000
Increaser   = (50 000 000 − 5 000 000) / 36 000 000 = 1.25
```
Home A with TotalPrice 16 000 000: RealPrice = 20 000 000; shared = 5 000 000 / 2 = 2 500 000; Total = 22 500 000 → Final 22 500 000, Round 0.
Sum of final amounts of all homes ≈ Bill.Price (differences only from rounding), because Σ(RealPrice) = Bill.Price − SharedPrice and Σ(share) = SharedPrice.

## 7. Data fields exposed to forms/reports
| Function | Purpose |
|---|---|
| `FUN_tblUse_Load(period, number, name, block)` | Reading list with computed use/price |
| `FUN_tblFinalBill_Load(...)` | Final bill grid (use, patterns, TotalPrice, SharedPrice, RealPrice, TotalRealPrice, Increaser, RoundPrice, FinalRealPrice) |
| `FUN_tblFinalBill_LoadRecord(useId)` | Single home detail |
| `FUN_tblFinalBill_LoadReport(...)` | Printable bill (adds meter digits) |
| `FUN_tblPattern_Load/LoadRecord`, `FUN_tblHome_*`, `FUN_tblPeriodUse_*`, `FUN_tblBill_*` | Searches (empty filter ⇒ `%`, non-empty ⇒ exact match; names use `LIKE %x%`) |

All CRUD procedures return an `@ErrorMessage` OUTPUT (`''` on success); insert procedures also return `SCOPE_IDENTITY()`. The UI maps constraint names in the message (e.g. `UQ_fldPeriodUseIdfldBillId_tblFinalBill`, `'fldPeriodUseId'`) to Persian user messages. Deleting a final bill is by (period no., bill no.). `SP_Backup` backs up the database; `tblUsers`/`cPassword` guard application entry.

## 8. Observations / risks
1. Tariff lookup lacks `ORDER BY fldToDigit` (non‑deterministic in theory).
2. Magic constants 0.04 (tax), 30 (days/month), 56 625 / 2 311 016 (out‑of‑tier fallback), rounding to −3 are hard‑coded in both SQL and C#.
3. UI preview rounds Price/Tax to integers; DB view does not → possible small differences between screen and report.
4. `Increaser` divides by `TotalHomePrice` and `TotalSharedPrice` by `Bill.Use`: zero totals cause a divide‑by‑zero (caught → `@ErrorMessage`). A negative `TotalSharedUse` (homes consumed more than billed) makes the shared price negative (credit).
5. Several procedures declare `@Identity TINYINT` (`SP_tbl*_DELETE`) which overflows for ids > 255.
6. `SP_..._DELETE/INSERT` concatenate `ERROR_NUMBER()` (int) with strings using `+`, which raises a conversion error inside CATCH; `CAST` would be needed to return the message.
7. Bill `fldTax` is stored but unused in calculations; the 4 % tax is applied only to home tariff prices.
