using ResidentialComplex.Application.Helpers;
using ResidentialComplex.Domain.Entities;
using Xunit;

namespace ResidentialComplex.Tests;

public class AuthorityBillCalculatorTests
{
    private static List<FinancialItemTier> HotTiers() =>
    [
        new() { TierOrder = 1, UpperLimit = null, RatePerUnit = 800m }
    ];

    private static List<FinancialItemTier> ColdTiers() =>
    [
        new() { TierOrder = 1, UpperLimit = 30000, RatePerUnit = 400m },
        new() { TierOrder = 2, UpperLimit = null, RatePerUnit = 500m }
    ];

    private static AuthorityBill Bill(decimal use = 100_000m, decimal price = 50_000_000m, int days = 30) =>
        new() { TotalUse = use, TotalPrice = price, PeriodDays = days };

    private static WaterHouseUsage[] TwoHouses() =>
        [new WaterHouseUsage(1, 10_000, 30_000), new WaterHouseUsage(2, 20_000, 30_000)];

    [Fact]
    public void SharedUse_SharedPrice_And_Increaser_Follow_Old_Formula()
    {
        var r = AuthorityBillCalculator.Calculate(Bill(), HotTiers(), ColdTiers(), TwoHouses());

        Assert.Equal(90_000m, r.TotalHomeUse);
        Assert.Equal(10_000m, r.TotalSharedUse);
        Assert.Equal(5_000_000m, r.TotalSharedPrice);
        Assert.Equal(2, r.HomeCount);
        Assert.Equal((50_000_000m - 5_000_000m) / r.TotalHomePrice, r.Increaser);
    }

    [Fact]
    public void Hot_And_Cold_Use_Their_Own_Tiers_Plus_Four_Percent_Tax()
    {
        var r = AuthorityBillCalculator.Calculate(Bill(), HotTiers(), ColdTiers(), TwoHouses());
        var h = r.Houses[0];

        Assert.Equal(10_000m * 800m * 1.04m, h.HotTariffTotal);
        Assert.Equal(30_000m * 400m * 1.04m, h.ColdTariffTotal);
        Assert.Equal(1, h.HotTierOrder);
        Assert.Equal(1, h.ColdTierOrder);
    }

    [Fact]
    public void Lines_Add_Up_To_Final_Price_Rounded_To_Thousand()
    {
        var r = AuthorityBillCalculator.Calculate(Bill(), HotTiers(), ColdTiers(), TwoHouses());

        Assert.All(r.Houses, h =>
        {
            Assert.Equal(h.FinalPrice, h.HotAmount + h.ColdAmount + h.SharedAmount);
            Assert.Equal(0m, h.FinalPrice % 1000m);
            Assert.Equal(h.FinalPrice - h.TotalRealPrice, h.RoundPrice);
        });
    }

    [Fact]
    public void Sum_Of_Final_Prices_Matches_Authority_Bill_Within_Rounding()
    {
        var r = AuthorityBillCalculator.Calculate(Bill(), HotTiers(), ColdTiers(), TwoHouses());

        Assert.InRange(r.Houses.Sum(h => h.FinalPrice), 50_000_000m - 1000m, 50_000_000m + 1000m);
    }

    [Fact]
    public void Shared_Price_Is_Split_Equally_Between_Houses()
    {
        var r = AuthorityBillCalculator.Calculate(Bill(), HotTiers(), ColdTiers(), TwoHouses());

        var sharedPerHome = r.TotalSharedPrice / r.HomeCount;
        Assert.All(r.Houses, h =>
            Assert.Equal(h.TotalRealPrice, (h.HotTariffTotal + h.ColdTariffTotal) * r.Increaser + sharedPerHome));
    }

    [Fact]
    public void Average_Is_Normalised_To_Thirty_Days_And_Picks_Tier()
    {
        // 60-day period: cold 45,000 -> average 22,500 (tier 1, limit 30,000)
        var houses = new[] { new WaterHouseUsage(1, 0, 45_000) };

        var r = AuthorityBillCalculator.Calculate(Bill(use: 45_000, price: 10_000_000, days: 60), HotTiers(), ColdTiers(), houses);
        var h = r.Houses[0];

        Assert.Equal(22_500m, h.ColdAverage);
        Assert.Equal(1, h.ColdTierOrder);
        Assert.Null(h.HotTierOrder);
        Assert.Equal(0m, h.HotTariffTotal);
    }

    [Fact]
    public void Whole_Usage_Is_Billed_At_The_Matching_Tier_Rate()
    {
        // average 40,000 exceeds tier 1 limit -> tier 2 rate applies to the entire usage
        var houses = new[] { new WaterHouseUsage(1, 0, 40_000) };

        var r = AuthorityBillCalculator.Calculate(Bill(use: 40_000, price: 1_000_000), HotTiers(), ColdTiers(), houses);

        Assert.Equal(2, r.Houses[0].ColdTierOrder);
        Assert.Equal(40_000m * 500m * 1.04m, r.Houses[0].ColdTariffTotal);
    }

    [Fact]
    public void House_With_No_Usage_Pays_Only_Shared_Share()
    {
        var houses = new[] { new WaterHouseUsage(1, 0, 0), new WaterHouseUsage(2, 0, 40_000) };

        var r = AuthorityBillCalculator.Calculate(Bill(use: 50_000, price: 20_000_000), HotTiers(), ColdTiers(), houses);
        var idle = r.Houses.Single(h => h.HouseId == 1);

        Assert.Equal(0m, idle.HotAmount);
        Assert.Equal(0m, idle.ColdAmount);
        Assert.Equal(r.TotalSharedPrice / 2, idle.TotalRealPrice);
    }

    [Fact]
    public void Negative_Shared_Use_Gives_A_Credit()
    {
        var houses = new[] { new WaterHouseUsage(1, 20_000, 40_000) };

        var r = AuthorityBillCalculator.Calculate(Bill(use: 50_000, price: 10_000_000), HotTiers(), ColdTiers(), houses);

        Assert.Equal(-10_000m, r.TotalSharedUse);
        Assert.True(r.TotalSharedPrice < 0);
        Assert.True(r.Houses[0].SharedAmount < 0);
    }

    [Fact]
    public void Invalid_Input_Throws()
    {
        var houses = new[] { new WaterHouseUsage(1, 100, 100) };

        Assert.Throws<InvalidOperationException>(() => AuthorityBillCalculator.Calculate(Bill(use: 0), HotTiers(), ColdTiers(), houses));
        Assert.Throws<InvalidOperationException>(() => AuthorityBillCalculator.Calculate(Bill(days: 0), HotTiers(), ColdTiers(), houses));
        Assert.Throws<InvalidOperationException>(() => AuthorityBillCalculator.Calculate(Bill(), [], ColdTiers(), houses));
        Assert.Throws<InvalidOperationException>(() => AuthorityBillCalculator.Calculate(Bill(), HotTiers(), [], houses));
        Assert.Throws<InvalidOperationException>(() => AuthorityBillCalculator.Calculate(Bill(), HotTiers(), ColdTiers(), []));
        Assert.Throws<InvalidOperationException>(() =>
            AuthorityBillCalculator.Calculate(Bill(), HotTiers(), ColdTiers(), [new WaterHouseUsage(1, 0, 0)]));
    }
}
