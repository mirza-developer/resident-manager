namespace ResidentialComplex.Domain.Entities;

/// <summary>
/// Monthly usage for a house per financial item (used in Grouping / bracket-priced calculation).
/// Each grouping-type financial item tracks usage separately per house per month.
///
/// Usage is captured as a meter-reading differential rather than a single static value:
/// the worker records the meter's <see cref="PreviousReading"/> (lower limit) and
/// <see cref="CurrentReading"/> (upper limit) for the month, and the consumption for
/// billing is their difference. A month's <see cref="CurrentReading"/> becomes next
/// month's <see cref="PreviousReading"/> automatically (carried forward), so normally
/// only the CurrentReading needs to be entered after the very first reading.
/// </summary>
public class MonthlyUsage
{
    public int Id { get; set; }
    public int HouseId { get; set; }
    public House House { get; set; } = null!;
    public int FinancialItemId { get; set; }
    public FinancialItem FinancialItem { get; set; } = null!;
    public int Year { get; set; }
    public int Month { get; set; }

    /// <summary>Meter reading at the start of the month (lower limit). Normally equal to the previous month's <see cref="CurrentReading"/>.</summary>
    public int PreviousReading { get; set; }

    /// <summary>Meter reading at the end of the month (upper limit), entered by the worker.</summary>
    public int CurrentReading { get; set; }

    /// <summary>
    /// Consumption for the month, i.e. CurrentReading − PreviousReading. This is what
    /// billing (CalculateBracketAmountAsync) actually reads, kept as its own persisted
    /// column so the billing/reporting code did not need to change.
    /// </summary>
    public int UsageCount { get; set; }
}
