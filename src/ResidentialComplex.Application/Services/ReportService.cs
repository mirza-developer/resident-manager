using ResidentialComplex.Application.DTOs;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Domain.Enums;

namespace ResidentialComplex.Application.Services;

/// <summary>
/// Generates financial reports and analytics.
/// </summary>
public class ReportService
{
    private readonly IBillRepository _billRepo;
    private readonly IApartmentRepository _apartmentRepo;
    private readonly IHouseRepository _houseRepo;

    public ReportService(IBillRepository billRepo, IApartmentRepository apartmentRepo, IHouseRepository houseRepo)
    {
        _billRepo = billRepo;
        _apartmentRepo = apartmentRepo;
        _houseRepo = houseRepo;
    }

    /// <summary>
    /// Generates a financial report for the given scope.
    /// </summary>
    public async Task<FinancialReportDto> GenerateReportAsync(int? year, int? month, int? houseId)
    {
        var bills = await _billRepo.GetForReportAsync(year, month, houseId);
        var apartments = await _apartmentRepo.GetAllAsync();
        var houses = await _houseRepo.GetAllAsync();

        var report = new FinancialReportDto
        {
            TotalBilled = bills.Where(b => b.Status >= BillStatus.Approved).Sum(b => b.TotalAmount),
            TotalPaid = bills.Where(b => b.Status == BillStatus.Paid).Sum(b => b.TotalAmount),
            OutstandingDebt = houses.Sum(h => h.CurrentDebt),
        };

        report.CollectionRate = report.TotalBilled > 0 ? Math.Round(report.TotalPaid / report.TotalBilled * 100, 2) : 0;

        report.DebtPerApartment = apartments.Select(a => new ApartmentDebtDto
        {
            ApartmentId = a.Id,
            ApartmentTitle = a.Title,
            TotalDebt = houses.Where(h => h.ApartmentId == a.Id).Sum(h => h.CurrentDebt)
        }).ToList();

        report.DebtPerHouse = houses.Select(h => new HouseDebtDto
        {
            HouseId = h.Id,
            HouseTitle = h.Title,
            CurrentDebt = h.CurrentDebt
        }).ToList();

        report.MonthlyBreakdown = bills
            .GroupBy(b => new { b.Year, b.Month })
            .Select(g => new MonthlyBreakdownDto
            {
                Year = g.Key.Year,
                Month = g.Key.Month,
                Billed = g.Where(b => b.Status >= BillStatus.Approved).Sum(b => b.TotalAmount),
                Paid = g.Where(b => b.Status == BillStatus.Paid).Sum(b => b.TotalAmount)
            })
            .OrderBy(m => m.Year).ThenBy(m => m.Month)
            .ToList();

        return report;
    }
}
