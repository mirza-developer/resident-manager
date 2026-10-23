using ResidentialComplex.Application.Constants;
using ResidentialComplex.Application.Helpers;
using ResidentialComplex.Domain.Entities;
using Xunit;

namespace ResidentialComplex.Tests;

public class AuthorityBillCalculatorTests
{
    private static AuthorityBill Bill(decimal use = 200, decimal price = 5_200_000, int days = 30) =>
        new() { TotalUse = use, TotalPrice = price, PeriodDays = days };

    private static List<FinancialItemTier> Tiers() =>
    [
        new() { TierOrder = 1, UpperLimit = 100, MonthlyPrice = 1_000_000 },
        new() { TierOrder = 2, UpperLimit = 200, MonthlyPrice = 3_000_000 }
    ];

    private static List<WaterHouseUsage> TwoHouses() =>
    [
        new(1, 30, 20),
        new(2, 100, 50)
    ];

    [Fact]
    public void WorkedExample_IncreaserIsOnePointTwoFive()
    {
        var r = AuthorityBillCalculator.Calculate(Bill(), Tiers(), TwoHouses());

        Assert.Equal(1.25m, r.Increaser);
        Assert.Equal(0m, r.TotalSharedUse);
        Assert.Equal(4_160_000m, r.TotalHomePrice);

        var h1 = r.Houses.Single(h => h.HouseId == 1);
        Assert.Equal(50m, h1.Use);
        Assert.Equal(1, h1.TierOrder);
        Assert.Equal(1_000_000m, h1.Price);
        Assert.Equal(40_000m, h1.Tax);
        Assert.Equal(1_300_000m, h1.Final);
        Assert.Equal(h1.Final, h1.MeteredAmount + h1.SharedAmount);

        var h2 = r.Houses.Single(h => h.HouseId == 2);
        Assert.Equal(2, h2.TierOrder);
        Assert.Equal(3_900_000m, h2.Final);
    }

    [Fact]
    public void TierBoundary_AverageEqualToUpperLimit_UsesThatTier()
    {
        var r = AuthorityBillCalculator.Calculate(Bill(use: 100, price: 1_040_000), Tiers(), [new WaterHouseUsage(1, 60, 40)]);
        Assert.Equal(1, r.Houses[0].TierOrder);
    }

    [Fact]
    public void AboveLastTier_UsesFallbackFormula()
    {
        var r = AuthorityBillCalculator.Calculate(Bill(use: 100_000, price: 10_000_000), Tiers(), [new WaterHouseUsage(1, 60_000, 40_000)]);
        var h = r.Houses[0];
        var expected = 100_000m / 1000m * WaterBillingConstants.FallbackFactor - WaterBillingConstants.FallbackOffset;
        Assert.Null(h.TierOrder);
        Assert.Equal(expected, h.MonthPrice);
    }

    [Fact]
    public void NoTiers_UsesFallbackFormula()
    {
        var r = AuthorityBillCalculator.Calculate(Bill(use: 100_000, price: 10_000_000), [], [new WaterHouseUsage(1, 60_000, 40_000)]);
        Assert.Null(r.Houses[0].TierOrder);
        Assert.True(r.Houses[0].Price > 0);
    }

    [Fact]
    public void ZeroUsage_HasZeroTariff()
    {
        var houses = new List<WaterHouseUsage> { new(1, 0, 0), new(2, 100, 50) };
        var r = AuthorityBillCalculator.Calculate(Bill(use: 150, price: 3_000_000), Tiers(), houses);
        Assert.Equal(0m, r.Houses[0].TariffTotal);
        Assert.Equal(0m, r.Houses[0].MeteredAmount);
    }

    [Fact]
    public void SharedUseNegative_IsEqualCreditPerHome()
    {
        var r = AuthorityBillCalculator.Calculate(Bill(use: 150, price: 5_000_000), Tiers(), TwoHouses());
        Assert.True(r.TotalSharedUse < 0);
        Assert.True(r.TotalSharedPrice < 0);
    }

    [Fact]
    public void RoundingDifference_IsCarriedBySharedLine()
    {
        var r = AuthorityBillCalculator.Calculate(Bill(use: 250, price: 5_555_555), Tiers(), TwoHouses());
        foreach (var h in r.Houses)
        {
            Assert.Equal(0m, h.Final % 1000m);
            Assert.Equal(h.Final, h.MeteredAmount + h.SharedAmount);
        }
    }

    [Fact]
    public void InvalidInput_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => AuthorityBillCalculator.Calculate(Bill(use: 0), Tiers(), TwoHouses()));
        Assert.Throws<InvalidOperationException>(() => AuthorityBillCalculator.Calculate(Bill(days: 0), Tiers(), TwoHouses()));
        Assert.Throws<InvalidOperationException>(() => AuthorityBillCalculator.Calculate(Bill(), Tiers(), []));
        Assert.Throws<InvalidOperationException>(() =>
            AuthorityBillCalculator.Calculate(Bill(), Tiers(), [new WaterHouseUsage(1, 0, 0)]));
    }
}
