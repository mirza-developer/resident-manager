using ResidentialComplex.Application.DTOs;
using ResidentialComplex.Application.Helpers;
using Xunit;

namespace ResidentialComplex.Tests;

/// <summary>The numbers behind the charts on the admin reports page.</summary>
public class ReportChartDataTests
{
    private static MonthlyBreakdownDto Month(int year, int month, decimal billed, decimal paid) =>
        new() { Year = year, Month = month, Billed = billed, Paid = paid };

    // ------------------------------------------------------------------ unit

    [Theory]
    [InlineData(0, 1, "ریال")]
    [InlineData(999, 1, "ریال")]
    [InlineData(1_000, 1_000, "هزار ریال")]
    [InlineData(999_999, 1_000, "هزار ریال")]
    [InlineData(1_000_000, 1_000_000, "میلیون ریال")]
    [InlineData(999_999_999, 1_000_000, "میلیون ریال")]
    [InlineData(1_000_000_000, 1_000_000_000, "میلیارد ریال")]
    [InlineData(-2_500_000_000, 1_000_000_000, "میلیارد ریال")]   // sign is ignored
    public void PickUnit_KeepsTheBiggestValueReadable(long max, long divisor, string label)
    {
        var unit = ReportChartData.PickUnit(max);

        Assert.Equal(divisor, unit.Divisor);
        Assert.Equal(label, unit.Label);
    }

    [Fact]
    public void Build_UsesOneCommonUnit_ForEveryChart()
    {
        var report = new FinancialReportDto
        {
            TotalBilled = 3_000_000_000m,
            TotalPaid = 1_000_000_000m,
            MonthlyBreakdown = { Month(1404, 1, 1_500_000_000m, 500_000_000m) },
            DebtPerApartment = { new ApartmentDebtDto { ApartmentTitle = "برج", TotalDebt = 2_000_000_000m } },
            DebtPerHouse = { new HouseDebtDto { HouseTitle = "واحد 1", CurrentDebt = 1_250_000_000m } }
        };

        var data = ReportChartData.Build(report);

        Assert.Equal("میلیارد ریال", data.Unit.Label);
        Assert.Equal(new[] { 1.5 }, data.BilledByMonth);
        Assert.Equal(new[] { 0.5 }, data.PaidByMonth);
        Assert.Equal(new[] { 1.0, 2.0 }, data.StatusValues);
        Assert.Equal(new[] { 2.0 }, data.TopApartmentDebts);
        Assert.Equal(new[] { 1.25 }, data.TopHouseDebts);
    }

    // ------------------------------------------------------------------ monthly series

    [Fact]
    public void Monthly_IsOrderedOldestFirst_AndLimitedToTheLastTwelveMonths()
    {
        var report = new FinancialReportDto();
        // 15 months across two Persian years, deliberately shuffled
        var all = Enumerable.Range(0, 15).Select(i => (Year: 1403 + (i + 9) / 12, Month: (i + 9) % 12 + 1)).ToList();   // 1403/10 .. 1404/12
        foreach (var (year, month) in all.OrderByDescending(x => (x.Month * 7) % 5).ThenBy(x => x.Year))
            report.MonthlyBreakdown.Add(Month(year, month, 100_000m, 50_000m));

        var data = ReportChartData.Build(report);

        Assert.Equal(12, data.MonthLabels.Length);
        Assert.Equal(12, data.BilledByMonth.Length);
        Assert.Equal(12, data.PaidByMonth.Length);
        var expected = all.OrderBy(x => x.Year).ThenBy(x => x.Month).TakeLast(12)
            .Select(x => $"{PersianCalendarHelper.GetMonthName(x.Month)} {x.Year % 100:00}").ToArray();
        Assert.Equal(expected, data.MonthLabels);
    }

    [Fact]
    public void MonthLabel_IsMonthNameWithTwoDigitYear()
    {
        var report = new FinancialReportDto { MonthlyBreakdown = { Month(1404, 1, 10m, 5m) } };

        Assert.Equal(new[] { "فروردین 04" }, ReportChartData.Build(report).MonthLabels);
    }

    [Fact]
    public void ValuesAreRoundedToTwoDecimals()
    {
        var report = new FinancialReportDto { MonthlyBreakdown = { Month(1404, 1, 1_234_567m, 0m) } };

        Assert.Equal(new[] { 1.23 }, ReportChartData.Build(report).BilledByMonth);   // million Rial, 2 decimals
    }

    // ------------------------------------------------------------------ paid vs unpaid

    [Fact]
    public void Status_SplitsBilledIntoPaidAndUnpaid()
    {
        var data = ReportChartData.Build(new FinancialReportDto { TotalBilled = 5_000_000m, TotalPaid = 3_000_000m });

        Assert.Equal(new[] { "پرداخت‌شده", "پرداخت‌نشده" }, data.StatusLabels);
        Assert.Equal(new[] { 3.0, 2.0 }, data.StatusValues);
        Assert.True(data.HasStatus);
    }

    [Fact]
    public void Status_UnpaidNeverGoesNegative_WhenMoreWasPaidThanBilled()
    {
        var data = ReportChartData.Build(new FinancialReportDto { TotalBilled = 1_000_000m, TotalPaid = 1_500_000m });

        Assert.Equal(0.0, data.StatusValues[1]);
    }

    // ------------------------------------------------------------------ biggest debtors

    [Fact]
    public void TopApartments_AreBiggestFirst_OnlyPositiveDebts_AndLimited()
    {
        var report = new FinancialReportDto();
        for (var i = 1; i <= 15; i++)
            report.DebtPerApartment.Add(new ApartmentDebtDto { ApartmentId = i, ApartmentTitle = $"آپارتمان {i}", TotalDebt = i * 1_000_000m });
        report.DebtPerApartment.Add(new ApartmentDebtDto { ApartmentTitle = "بدون بدهی", TotalDebt = 0m });
        report.DebtPerApartment.Add(new ApartmentDebtDto { ApartmentTitle = "بستانکار", TotalDebt = -9_000_000m });

        var data = ReportChartData.Build(report);

        Assert.Equal(10, data.TopApartmentLabels.Length);
        Assert.Equal("آپارتمان 15", data.TopApartmentLabels[0]);
        Assert.Equal("آپارتمان 6", data.TopApartmentLabels[^1]);
        Assert.DoesNotContain("بدون بدهی", data.TopApartmentLabels);
        Assert.DoesNotContain("بستانکار", data.TopApartmentLabels);
        Assert.Equal(data.TopApartmentLabels.Length, data.TopApartmentDebts.Length);
        Assert.True(data.TopApartmentDebts.SequenceEqual(data.TopApartmentDebts.OrderByDescending(v => v)));
    }

    private static FinancialReportDto HousesReport() => new()
    {
        DebtPerHouse =
        {
            new HouseDebtDto { HouseTitle = "الف", CurrentDebt = 300_000m },
            new HouseDebtDto { HouseTitle = "ب", CurrentDebt = 900_000m },
            new HouseDebtDto { HouseTitle = "ج", CurrentDebt = 0m },
            new HouseDebtDto { HouseTitle = "د", CurrentDebt = -50_000m },
            new HouseDebtDto { HouseTitle = "ه", CurrentDebt = 600_000m }
        }
    };

    [Fact]
    public void TopHouses_ListOnlyRealDebtors_BiggestFirst()
    {
        var data = ReportChartData.Build(HousesReport());   // default top 10: credits/zero would show up if they were not filtered

        Assert.Equal(new[] { "ب", "ه", "الف" }, data.TopHouseLabels);
        Assert.Equal(new[] { 900.0, 600.0, 300.0 }, data.TopHouseDebts);   // thousand Rial
    }

    [Fact]
    public void TopHouses_RespectTheRequestedCount()
    {
        var data = ReportChartData.Build(HousesReport(), topCount: 2);

        Assert.Equal(new[] { "ب", "ه" }, data.TopHouseLabels);
    }

    // ------------------------------------------------------------------ empty data

    [Fact]
    public void EmptyReport_ProducesEmptyCharts_WithoutThrowing()
    {
        var data = ReportChartData.Build(new FinancialReportDto());

        Assert.False(data.HasMonthly);
        Assert.False(data.HasStatus);
        Assert.False(data.HasApartmentDebts);
        Assert.False(data.HasHouseDebts);
        Assert.Empty(data.MonthLabels);
        Assert.Equal("ریال", data.Unit.Label);
    }

    [Fact]
    public void AllSeriesOfOneChart_HaveTheSameLength()
    {
        var report = new FinancialReportDto
        {
            MonthlyBreakdown = { Month(1404, 3, 10m, 5m), Month(1404, 1, 20m, 10m), Month(1404, 2, 30m, 0m) },
            DebtPerApartment = { new ApartmentDebtDto { ApartmentTitle = "الف", TotalDebt = 5m }, new ApartmentDebtDto { ApartmentTitle = "ب", TotalDebt = 7m } }
        };

        var data = ReportChartData.Build(report);

        Assert.Equal(data.MonthLabels.Length, data.BilledByMonth.Length);
        Assert.Equal(data.MonthLabels.Length, data.PaidByMonth.Length);
        Assert.Equal(data.TopApartmentLabels.Length, data.TopApartmentDebts.Length);
        Assert.Equal(new[] { 20.0, 30.0, 10.0 }, data.BilledByMonth);   // sorted: month 1, 2, 3
    }
}
