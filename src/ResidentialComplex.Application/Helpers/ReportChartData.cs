using ResidentialComplex.Application.DTOs;

namespace ResidentialComplex.Application.Helpers;

/// <summary>Display unit for chart amounts (raw Rial values are too long to read on a chart axis).</summary>
public sealed record AmountUnit(decimal Divisor, string Label);

/// <summary>
/// Turns a <see cref="FinancialReportDto"/> into the arrays the report charts need: monthly billed vs paid,
/// paid vs unpaid share, and the biggest debtors (apartments and houses). Pure and UI-free so it can be unit tested.
/// All amounts are scaled by one common <see cref="Unit"/> (e.g. million Rial) so the charts stay readable.
/// </summary>
public sealed class ReportChartData
{
    public const int DefaultTopCount = 10;
    public const int DefaultMaxMonths = 12;

    public AmountUnit Unit { get; private init; } = new(1m, "ریال");

    // monthly billed vs paid (oldest → newest, at most the last N months)
    public string[] MonthLabels { get; private init; } = Array.Empty<string>();
    public double[] BilledByMonth { get; private init; } = Array.Empty<double>();
    public double[] PaidByMonth { get; private init; } = Array.Empty<double>();

    // paid vs unpaid share of what was billed
    public string[] StatusLabels { get; private init; } = Array.Empty<string>();
    public double[] StatusValues { get; private init; } = Array.Empty<double>();

    // largest debts (only positive debts), biggest first
    public string[] TopApartmentLabels { get; private init; } = Array.Empty<string>();
    public double[] TopApartmentDebts { get; private init; } = Array.Empty<double>();
    public string[] TopHouseLabels { get; private init; } = Array.Empty<string>();
    public double[] TopHouseDebts { get; private init; } = Array.Empty<double>();

    public bool HasMonthly => MonthLabels.Length > 0;
    public bool HasStatus => StatusValues.Sum() > 0;
    public bool HasApartmentDebts => TopApartmentDebts.Length > 0;
    public bool HasHouseDebts => TopHouseDebts.Length > 0;

    /// <summary>Picks the unit that keeps the biggest value readable (≥ 1 billion → billion Rial, ≥ 1 million → million Rial, ...).</summary>
    public static AmountUnit PickUnit(decimal maxAmount)
    {
        var max = Math.Abs(maxAmount);
        if (max >= 1_000_000_000m) return new AmountUnit(1_000_000_000m, "میلیارد ریال");
        if (max >= 1_000_000m) return new AmountUnit(1_000_000m, "میلیون ریال");
        if (max >= 1_000m) return new AmountUnit(1_000m, "هزار ریال");
        return new AmountUnit(1m, "ریال");
    }

    public static ReportChartData Build(FinancialReportDto report, int topCount = DefaultTopCount, int maxMonths = DefaultMaxMonths)
    {
        var months = report.MonthlyBreakdown
            .OrderBy(m => m.Year).ThenBy(m => m.Month)
            .TakeLast(Math.Max(1, maxMonths))
            .ToList();

        var apartments = report.DebtPerApartment
            .Where(a => a.TotalDebt > 0)
            .OrderByDescending(a => a.TotalDebt)
            .Take(Math.Max(1, topCount))
            .ToList();

        var houses = report.DebtPerHouse
            .Where(h => h.CurrentDebt > 0)
            .OrderByDescending(h => h.CurrentDebt)
            .Take(Math.Max(1, topCount))
            .ToList();

        var unpaid = Math.Max(0m, report.TotalBilled - report.TotalPaid);

        // one common unit for every chart, chosen from the biggest amount shown anywhere
        var max = new[]
        {
            months.Select(m => Math.Max(m.Billed, m.Paid)).DefaultIfEmpty(0m).Max(),
            apartments.Select(a => a.TotalDebt).DefaultIfEmpty(0m).Max(),
            houses.Select(h => h.CurrentDebt).DefaultIfEmpty(0m).Max(),
            report.TotalBilled
        }.Max();
        var unit = PickUnit(max);

        double Scale(decimal value) => (double)Math.Round(value / unit.Divisor, 2);

        return new ReportChartData
        {
            Unit = unit,
            MonthLabels = months.Select(m => $"{PersianCalendarHelper.GetMonthName(m.Month)} {m.Year % 100:00}").ToArray(),
            BilledByMonth = months.Select(m => Scale(m.Billed)).ToArray(),
            PaidByMonth = months.Select(m => Scale(m.Paid)).ToArray(),
            StatusLabels = new[] { "پرداخت‌شده", "پرداخت‌نشده" },
            StatusValues = new[] { Scale(report.TotalPaid), Scale(unpaid) },
            TopApartmentLabels = apartments.Select(a => a.ApartmentTitle).ToArray(),
            TopApartmentDebts = apartments.Select(a => Scale(a.TotalDebt)).ToArray(),
            TopHouseLabels = houses.Select(h => h.HouseTitle).ToArray(),
            TopHouseDebts = houses.Select(h => Scale(h.CurrentDebt)).ToArray()
        };
    }
}
