using ResidentialComplex.Application.Helpers;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Application.Services;
using ResidentialComplex.Domain.Entities;
using ResidentialComplex.Domain.Enums;
using Xunit;

namespace ResidentialComplex.Tests;

/// <summary>Charts built from a REAL report (EF + billing), not hand-made DTOs.</summary>
public class ReportChartDataPersistenceTests : TestBase
{
    [Fact]
    public async Task ChartData_FromARealReport_MatchesTheBilledAndPaidAmounts()
    {
        var apt = await GetService<IApartmentRepository>().AddAsync(new Apartment { Title = "بلوک" });
        await GetService<IHouseRepository>().AddAsync(new House { Title = "واحد 1", ApartmentId = apt.Id, ResidentName = "ساکن", ResidentPhoneNumber = "0", IsActive = true });
        await GetService<IHouseRepository>().AddAsync(new House { Title = "واحد 2", ApartmentId = apt.Id, ResidentName = "ساکن", ResidentPhoneNumber = "0", IsActive = true });
        var fi = await GetService<IFinancialItemRepository>().AddAsync(new FinancialItem { Title = "شارژ", PeriodType = PeriodType.Permanent, CalculationType = CalculationType.EqualDivision, IsActive = true });
        var billing = GetService<BillingService>();

        var bills = await billing.GenerateBillsAsync(1404, 1, new Dictionary<int, decimal> { [fi.Id] = 2_000_000m }, "test", "test");
        await billing.ApproveBillsAsync(1404, 1, "test", "test");
        await billing.RecordPaymentAsync(bills[0].Id, "test", "test");   // one of the two houses pays

        var report = await GetService<ReportService>().GenerateReportAsync(1404, null, null);
        var data = ReportChartData.Build(report);

        Assert.Equal("میلیون ریال", data.Unit.Label);
        Assert.Equal(new[] { "فروردین 04" }, data.MonthLabels);
        Assert.Equal(new[] { 2.0 }, data.BilledByMonth);
        Assert.Equal(new[] { 1.0 }, data.PaidByMonth);
        Assert.Equal(new[] { 1.0, 1.0 }, data.StatusValues);
        Assert.Equal(new[] { "بلوک" }, data.TopApartmentLabels);
        Assert.Single(data.TopHouseLabels);   // only the house that has not paid still owes
    }
}
