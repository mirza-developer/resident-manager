using ResidentialComplex.Application.Constants;
using ResidentialComplex.Domain.Entities;

namespace ResidentialComplex.Application.Helpers;

/// <summary>One house's hot and cold consumption for <see cref="AuthorityBillCalculator"/>.</summary>
public record WaterHouseUsage(int HouseId, decimal HotUsage, decimal ColdUsage);

/// <summary>Per-house result of the water allocation (combined metered line and shared remainder).</summary>
public record WaterHouseResult(
    int HouseId,
    decimal Use,
    decimal Average,
    int? TierOrder,
    decimal MonthPrice,
    decimal Price,
    decimal Tax,
    decimal TariffTotal,
    decimal MeteredAmount,
    decimal SharedAmount,
    decimal TotalReal,
    decimal Final,
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
/// Old PITS final-bill formula (Docs/old-docs.md, sections 4 and 6): combined hot + cold usage is
/// matched against the parent's tiers (flat monthly price), with a fallback formula outside the tiers.
/// </summary>
public static class AuthorityBillCalculator
{
    public static AuthorityBillResult Calculate(
        AuthorityBill bill,
        IReadOnlyList<FinancialItemTier> tiers,
        IReadOnlyList<WaterHouseUsage> houses)
    {
        if (houses.Count == 0)
            throw new InvalidOperationException("واحد فعالی وجود ندارد.");
        if (bill.TotalUse <= 0)
            throw new InvalidOperationException("مصرف کل قبض مرجع باید بزرگتر از صفر باشد.");
        if (bill.PeriodDays <= 0)
            throw new InvalidOperationException("تعداد روزهای دوره باید بزرگتر از صفر باشد.");
        if (tiers.Count > 0 && tiers.Any(t => t.MonthlyPrice <= 0))
            throw new InvalidOperationException("قیمت ماهانه همه تعرفه‌های آیتم آب باید بزرگتر از صفر باشد.");

        var ordered = tiers.OrderBy(t => t.TierOrder).ToList();

        var rows = new List<(int HouseId, decimal Use, decimal Avg, int? Tier, decimal MonthPrice, decimal Price, decimal Tax, decimal Tariff)>();
        foreach (var h in houses)
        {
            var use = Math.Max(0m, h.HotUsage) + Math.Max(0m, h.ColdUsage);
            var avg = use * WaterBillingConstants.DaysPerMonth / bill.PeriodDays;
            if (use <= 0)
            {
                rows.Add((h.HouseId, 0m, 0m, null, 0m, 0m, 0m, 0m));
                continue;
            }

            var tier = ordered.FirstOrDefault(t => !t.UpperLimit.HasValue || avg <= t.UpperLimit.Value);
            var monthPrice = tier?.MonthlyPrice
                ?? Math.Max(0m, avg / WaterBillingConstants.FallbackDivisor * WaterBillingConstants.FallbackFactor
                    - WaterBillingConstants.FallbackOffset);
            var price = Math.Round(monthPrice * bill.PeriodDays / WaterBillingConstants.DaysPerMonth, 0, MidpointRounding.AwayFromZero);
            var tax = Math.Round(price * WaterBillingConstants.TaxRate, 0, MidpointRounding.AwayFromZero);
            rows.Add((h.HouseId, use, avg, tier?.TierOrder, monthPrice, price, tax, price + tax));
        }

        var totalHomeUse = rows.Sum(r => r.Use);
        var totalHomePrice = rows.Sum(r => r.Tariff);

        // Allocation weight: tier tariff, floored so any positive usage always carries a positive weight.
        var weights = rows.Select(r => r.Use > 0
            ? Math.Max(r.Tariff, r.Use * WaterBillingConstants.MinWeightPerUnit)
            : 0m).ToList();
        var totalWeight = weights.Sum();
        if (totalWeight <= 0)
            throw new InvalidOperationException("مجموع مبلغ تعرفه‌ای واحدها صفر است؛ مصرف واحدها را ثبت کنید.");

        var homeCount = rows.Count;
        // Meters summing above the authority total are never turned into a credit.
        var sharedUse = Math.Max(0m, bill.TotalUse - totalHomeUse);
        var sharedPrice = bill.TotalPrice * sharedUse / bill.TotalUse;
        var privatePool = bill.TotalPrice - sharedPrice;
        var increaser = privatePool / totalWeight;
        var sharedPerHome = sharedPrice / homeCount;

        var step = WaterBillingConstants.HomeRoundingStep;
        var realPrices = weights.Select(w => w * increaser).ToList();
        var totals = realPrices.Select(p => p + sharedPerHome).ToList();

        // Largest-remainder rounding so the rounded finals add up to the authority price.
        var units = totals.Select(t => Math.Floor(t / step)).ToList();
        var remaining = (int)(Math.Round(bill.TotalPrice / step, 0, MidpointRounding.AwayFromZero) - units.Sum());
        if (remaining > 0)
        {
            foreach (var i in Enumerable.Range(0, homeCount)
                         .OrderByDescending(i => totals[i] / step - units[i])
                         .Take(remaining))
                units[i] += 1;
        }

        var results = new List<WaterHouseResult>(homeCount);
        for (var i = 0; i < homeCount; i++)
        {
            var r = rows[i];
            var metered = Math.Round(realPrices[i], 0, MidpointRounding.AwayFromZero);
            if (r.Use > 0)
                metered = Math.Max(1m, metered);
            var final = units[i] * step;
            if (r.Use > 0 && final < metered)
                final = Math.Ceiling(metered / step) * step;
            // The rounding difference is carried by the shared line so metered + shared == final.
            results.Add(new WaterHouseResult(
                r.HouseId, r.Use, r.Avg, r.Tier, r.MonthPrice, r.Price, r.Tax, r.Tariff,
                metered, final - metered, totals[i], final, final - totals[i]));
            }

        return new AuthorityBillResult(totalHomeUse, totalHomePrice, homeCount, sharedUse, sharedPrice, increaser, results);
    }
}
