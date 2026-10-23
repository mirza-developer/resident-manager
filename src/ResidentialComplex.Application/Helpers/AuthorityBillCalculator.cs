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
        if (tiers.Count > 0 && tiers.All(t => t.MonthlyPrice <= 0))
            throw new InvalidOperationException("برای تعرفه‌های آیتم آب قیمت ماهانه تعریف نشده است.");

        var ordered = tiers.OrderBy(t => t.TierOrder).ToList();

        var rows = new List<(int HouseId, decimal Use, decimal Avg, int? Tier, decimal MonthPrice, decimal Price, decimal Tax, decimal Tariff)>();
        foreach (var h in houses)
        {
            var use = Math.Max(0m, h.HotUsage) + Math.Max(0m, h.ColdUsage);
            var avg = use / bill.PeriodDays * WaterBillingConstants.DaysPerMonth;
            if (use <= 0)
            {
                rows.Add((h.HouseId, 0m, 0m, null, 0m, 0m, 0m, 0m));
                continue;
            }

            var tier = ordered.FirstOrDefault(t => !t.UpperLimit.HasValue || avg <= t.UpperLimit.Value);
            var monthPrice = tier?.MonthlyPrice
                ?? (avg / WaterBillingConstants.FallbackDivisor * WaterBillingConstants.FallbackFactor
                    - WaterBillingConstants.FallbackOffset);
            var price = Math.Round(monthPrice * bill.PeriodDays / WaterBillingConstants.DaysPerMonth, 0, MidpointRounding.AwayFromZero);
            var tax = Math.Round(price * WaterBillingConstants.TaxRate, 0, MidpointRounding.AwayFromZero);
            rows.Add((h.HouseId, use, avg, tier?.TierOrder, monthPrice, price, tax, price + tax));
        }

        var totalHomeUse = rows.Sum(r => r.Use);
        var totalHomePrice = rows.Sum(r => r.Tariff);
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
            var realPrice = r.Tariff * increaser;
            var metered = Math.Round(realPrice, 0, MidpointRounding.AwayFromZero);
            var totalReal = realPrice + sharedPerHome;
            var final = Math.Round(totalReal / WaterBillingConstants.HomeRoundingStep, 0, MidpointRounding.AwayFromZero)
                        * WaterBillingConstants.HomeRoundingStep;
            // The rounding difference is carried by the shared line so metered + shared == final.
            results.Add(new WaterHouseResult(
                r.HouseId, r.Use, r.Avg, r.Tier, r.MonthPrice, r.Price, r.Tax, r.Tariff,
                metered, final - metered, totalReal, final, final - totalReal));
        }

        return new AuthorityBillResult(totalHomeUse, totalHomePrice, homeCount, sharedUse, sharedPrice, increaser, results);
    }
}
