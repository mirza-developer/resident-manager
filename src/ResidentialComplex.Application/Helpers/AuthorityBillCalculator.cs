using ResidentialComplex.Application.Constants;
using ResidentialComplex.Domain.Entities;

namespace ResidentialComplex.Application.Helpers;

/// <summary>One house's hot and cold consumption for <see cref="AuthorityBillCalculator"/>.</summary>
public record WaterHouseUsage(int HouseId, decimal HotUsage, decimal ColdUsage);

/// <summary>Per-house result of the water allocation (hot, cold and shared lines).</summary>
public record WaterHouseResult(
    int HouseId,
    decimal HotUsage,
    decimal ColdUsage,
    decimal HotAverage,
    decimal ColdAverage,
    int? HotTierOrder,
    int? ColdTierOrder,
    decimal HotTariffTotal,
    decimal ColdTariffTotal,
    decimal HotAmount,
    decimal ColdAmount,
    decimal SharedAmount,
    decimal TotalRealPrice,
    decimal FinalPrice,
    decimal RoundPrice);

/// <summary>Aggregates and per-house results of the allocation.</summary>
public record AuthorityBillResult(
    decimal TotalHomeUse,
    decimal TotalHomePrice,
    int HomeCount,
    decimal TotalSharedUse,
    decimal TotalSharedPrice,
    decimal Increaser,
    IReadOnlyList<WaterHouseResult> Houses);

/// <summary>
/// Old PITS final-bill formula (Docs/old-docs.md, sections 4 and 6) for a water parent item with
/// hot and cold children (own tiers and usage) and a shared-consumption child.
/// </summary>
public static class AuthorityBillCalculator
{
    public static AuthorityBillResult Calculate(
        AuthorityBill bill,
        IReadOnlyList<FinancialItemTier> hotTiers,
        IReadOnlyList<FinancialItemTier> coldTiers,
        IReadOnlyList<WaterHouseUsage> houses)
    {
        if (houses.Count == 0)
            throw new InvalidOperationException("واحد فعالی وجود ندارد.");
        if (bill.TotalUse <= 0)
            throw new InvalidOperationException("مصرف کل قبض مرجع باید بزرگتر از صفر باشد.");
        if (bill.PeriodDays <= 0)
            throw new InvalidOperationException("تعداد روزهای دوره باید بزرگتر از صفر باشد.");
        if (hotTiers.Count == 0)
            throw new InvalidOperationException("برای آیتم آب گرم هیچ تعرفه‌ای تعریف نشده است.");
        if (coldTiers.Count == 0)
            throw new InvalidOperationException("برای آیتم آب سرد هیچ تعرفه‌ای تعریف نشده است.");

        var orderedHot = hotTiers.OrderBy(t => t.TierOrder).ToList();
        var orderedCold = coldTiers.OrderBy(t => t.TierOrder).ToList();

        var rows = new List<(WaterHouseUsage House, decimal HotAvg, decimal ColdAvg, int? HotTier, int? ColdTier, decimal HotTariff, decimal ColdTariff)>();
        foreach (var h in houses)
        {
            var hot = Math.Max(0m, h.HotUsage);
            var cold = Math.Max(0m, h.ColdUsage);
            var (hotAvg, hotTier, hotTariff) = PriceUsage(hot, bill.PeriodDays, orderedHot);
            var (coldAvg, coldTier, coldTariff) = PriceUsage(cold, bill.PeriodDays, orderedCold);
            rows.Add((new WaterHouseUsage(h.HouseId, hot, cold), hotAvg, coldAvg, hotTier, coldTier, hotTariff, coldTariff));
        }

        var totalHomeUse = rows.Sum(r => r.House.HotUsage + r.House.ColdUsage);
        var totalHomePrice = rows.Sum(r => r.HotTariff + r.ColdTariff);
        if (totalHomePrice <= 0)
            throw new InvalidOperationException("مجموع مبلغ تعرفه‌ای واحدها صفر است؛ مصرف واحدها را ثبت کنید.");

        var homeCount = rows.Count;
        var sharedUse = bill.TotalUse - totalHomeUse;
        var sharedPrice = bill.TotalPrice * sharedUse / bill.TotalUse;
        var increaser = (bill.TotalPrice - sharedPrice) / totalHomePrice;
        var sharedPerHome = sharedPrice / homeCount;

        var results = new List<WaterHouseResult>(homeCount);
        foreach (var r in rows)
        {
            var hotAmount = Math.Round(r.HotTariff * increaser, 0, MidpointRounding.AwayFromZero);
            var coldAmount = Math.Round(r.ColdTariff * increaser, 0, MidpointRounding.AwayFromZero);
            var totalReal = r.HotTariff * increaser + r.ColdTariff * increaser + sharedPerHome;
            var final = Math.Round(totalReal / WaterBillingConstants.HomeRoundingStep, 0, MidpointRounding.AwayFromZero)
                        * WaterBillingConstants.HomeRoundingStep;
            // The rounding difference is carried by the shared line so hot + cold + shared == final.
            var sharedAmount = final - hotAmount - coldAmount;
            results.Add(new WaterHouseResult(
                r.House.HouseId, r.House.HotUsage, r.House.ColdUsage, r.HotAvg, r.ColdAvg, r.HotTier, r.ColdTier,
                r.HotTariff, r.ColdTariff, hotAmount, coldAmount, sharedAmount, totalReal, final, final - totalReal));
        }

        return new AuthorityBillResult(totalHomeUse, totalHomePrice, homeCount, sharedUse, sharedPrice, increaser, results);
    }

    private static (decimal Average, int? TierOrder, decimal TariffTotal) PriceUsage(
        decimal usage, int periodDays, List<FinancialItemTier> orderedTiers)
    {
        var average = usage / periodDays * WaterBillingConstants.DaysPerMonth;
        if (usage <= 0)
            return (average, null, 0m);

        var tier = orderedTiers.FirstOrDefault(t => !t.UpperLimit.HasValue || average <= t.UpperLimit.Value)
                   ?? orderedTiers[^1];
        var price = Math.Round(usage * tier.RatePerUnit, 0, MidpointRounding.AwayFromZero);
        var tax = Math.Round(price * WaterBillingConstants.TaxRate, 0, MidpointRounding.AwayFromZero);
        return (average, tier.TierOrder, price + tax);
    }
}
