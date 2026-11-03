namespace ResidentialComplex.Application.Constants;

/// <summary>
/// Constants of the old PITS authority-bill allocation formula.
/// </summary>
public static class WaterBillingConstants
{
    /// <summary>Tax rate applied to each house's tariff price (4 %).</summary>
    public const decimal TaxRate = 0.04m;

    /// <summary>Days per month used to normalise usage for tier selection.</summary>
    public const decimal DaysPerMonth = 30m;

    /// <summary>Per-house final amount is rounded to this step (Rial), i.e. ROUND(x, -3).</summary>
    public const decimal HomeRoundingStep = 1000m;

    /// <summary>Minimum allocation weight per usage unit, so a house with usage is never allocated zero.</summary>
    public const decimal MinWeightPerUnit = 1m;

    /// <summary>Fallback monthly
    public const decimal FallbackDivisor = 1000m;
    public const decimal FallbackFactor = 56625m;
    public const decimal FallbackOffset = 2311016m;
}
