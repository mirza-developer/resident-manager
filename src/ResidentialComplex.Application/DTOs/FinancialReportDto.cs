namespace ResidentialComplex.Application.DTOs;

/// <summary>
/// Report data for financial analytics.
/// </summary>
public class FinancialReportDto
{
    public decimal TotalBilled { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal OutstandingDebt { get; set; }
    public decimal CollectionRate { get; set; }
    public List<ApartmentDebtDto> DebtPerApartment { get; set; } = new();
    public List<HouseDebtDto> DebtPerHouse { get; set; } = new();
    public List<MonthlyBreakdownDto> MonthlyBreakdown { get; set; } = new();
}

public class ApartmentDebtDto
{
    public int ApartmentId { get; set; }
    public string ApartmentTitle { get; set; } = string.Empty;
    public decimal TotalDebt { get; set; }
}

public class HouseDebtDto
{
    public int HouseId { get; set; }
    public string HouseTitle { get; set; } = string.Empty;
    public decimal CurrentDebt { get; set; }
}

public class MonthlyBreakdownDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Billed { get; set; }
    public decimal Paid { get; set; }
}
