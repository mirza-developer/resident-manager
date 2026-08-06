using ResidentialComplex.Domain.Entities;
using ResidentialComplex.Domain.Enums;
using ResidentialComplex.Application.Interfaces;

namespace ResidentialComplex.Application.Services;

/// <summary>
/// Handles monthly billing generation, approval, and payment.
/// </summary>
public class BillingService
{
    private readonly IBillRepository _billRepo;
    private readonly IHouseRepository _houseRepo;
    private readonly IFinancialItemRepository _financialItemRepo;
    private readonly IMonthlyUsageRepository _usageRepo;
    private readonly IPaymentRepository _paymentRepo;
    private readonly IAuditService _audit;

    public BillingService(
        IBillRepository billRepo,
        IHouseRepository houseRepo,
        IFinancialItemRepository financialItemRepo,
        IMonthlyUsageRepository usageRepo,
        IPaymentRepository paymentRepo,
        IAuditService audit)
    {
        _billRepo = billRepo;
        _houseRepo = houseRepo;
        _financialItemRepo = financialItemRepo;
        _usageRepo = usageRepo;
        _paymentRepo = paymentRepo;
        _audit = audit;
    }

    /// <summary>
    /// Generates draft bills for all active houses for the given month.
    /// </summary>
    /// <param name="year">Billing year.</param>
    /// <param name="month">Billing month (1-12).</param>
    /// <param name="finalAmounts">Dictionary mapping FinancialItem.Id to the final amount entered by administrator.</param>
    /// <param name="userId">Current user id for audit.</param>
    /// <param name="userName">Current user name for audit.</param>
    /// <returns>Generated draft bills.</returns>
    public async Task<List<Bill>> GenerateBillsAsync(int year, int month, Dictionary<int, decimal> finalAmounts, string userId, string userName)
    {
        var activeHouses = await _houseRepo.GetActiveHousesAsync();
        if (activeHouses.Count == 0)
            throw new InvalidOperationException("واحد فعالی وجود ندارد.");

        var activeItems = await _financialItemRepo.GetActiveAsync();
        var applicableItems = activeItems
            .Where(fi => finalAmounts.ContainsKey(fi.Id))
            .Where(fi => IsApplicable(fi))
            .ToList();

        var usages = await _usageRepo.GetByMonthYearAsync(year, month);
        var usageByHouseItem = usages
            .GroupBy(u => (u.HouseId, u.FinancialItemId))
            .ToDictionary(g => g.Key, g => g.First().UsageCount);

        var bills = new List<Bill>();

        foreach (var house in activeHouses)
        {
            // Check uniqueness constraint
            var existing = await _billRepo.GetByHouseMonthYearAsync(house.Id, year, month);
            if (existing != null)
                continue;

            var bill = new Bill
            {
                HouseId = house.Id,
                Year = year,
                Month = month,
                Status = BillStatus.Draft,
                CreatedDate = DateTime.UtcNow,
                BillItems = new List<BillItem>()
            };

            foreach (var fi in applicableItems)
            {
                var finalAmount = finalAmounts[fi.Id];
                decimal houseAmount;

                if (fi.PeriodType == PeriodType.Installment && fi.TotalAmount.HasValue && fi.NumberOfInstallments.HasValue && fi.NumberOfInstallments.Value > 0)
                {
                    finalAmount = fi.TotalAmount.Value / fi.NumberOfInstallments.Value;
                }

                if (fi.CalculationType == CalculationType.EqualDivision)
                {
                    houseAmount = finalAmount / activeHouses.Count;
                }
                else // Grouping
                {
                    houseAmount = CalculateGroupingAmount(fi, house, activeHouses, usageByHouseItem, finalAmount);
                }

                bill.BillItems.Add(new BillItem
                {
                    FinancialItemId = fi.Id,
                    Amount = houseAmount,
                    Description = fi.Title
                });
            }

            // Apply rounding adjustment to ensure total matches
            foreach (var fi in applicableItems)
            {
                var finalAmount = finalAmounts[fi.Id];
                if (fi.PeriodType == PeriodType.Installment && fi.TotalAmount.HasValue && fi.NumberOfInstallments.HasValue && fi.NumberOfInstallments.Value > 0)
                {
                    finalAmount = fi.TotalAmount.Value / fi.NumberOfInstallments.Value;
                }

                var relatedItems = bills.SelectMany(b => b.BillItems).Where(bi => bi.FinancialItemId == fi.Id).ToList();
                // Add current bill's items
                var currentItems = bill.BillItems.Where(bi => bi.FinancialItemId == fi.Id).ToList();

                // Rounding adjustment will be done after all bills are created
            }

            bill.TotalAmount = bill.BillItems.Sum(bi => bi.Amount);
            bills.Add(bill);
        }

        // Apply rounding adjustments per financial item across all bills
        foreach (var fi in applicableItems)
        {
            var finalAmount = finalAmounts[fi.Id];
            if (fi.PeriodType == PeriodType.Installment && fi.TotalAmount.HasValue && fi.NumberOfInstallments.HasValue && fi.NumberOfInstallments.Value > 0)
            {
                finalAmount = fi.TotalAmount.Value / fi.NumberOfInstallments.Value;
            }

            var allItems = bills.SelectMany(b => b.BillItems).Where(bi => bi.FinancialItemId == fi.Id).ToList();
            if (allItems.Count > 0)
            {
                var totalCalculated = allItems.Sum(bi => bi.Amount);
                var diff = finalAmount - totalCalculated;
                if (diff != 0)
                {
                    // Apply rounding difference to the last item
                    allItems.Last().Amount += diff;
                }
            }
        }

        // Recalculate totals after rounding adjustment
        foreach (var bill in bills)
        {
            bill.TotalAmount = bill.BillItems.Sum(bi => bi.Amount);
        }

        await _billRepo.AddRangeAsync(bills);

        foreach (var bill in bills)
        {
            await _audit.LogAsync(userId, userName, nameof(Bill), bill.Id.ToString(), "Created", null,
                $"House={bill.HouseId}, Year={bill.Year}, Month={bill.Month}, Total={bill.TotalAmount}");
        }

        return bills;
    }

    /// <summary>
    /// Approves bills for a given month, updating house debts.
    /// </summary>
    public async Task ApproveBillsAsync(int year, int month, string userId, string userName)
    {
        var bills = await _billRepo.GetByMonthYearAsync(year, month);
        var draftBills = bills.Where(b => b.Status == BillStatus.Draft).ToList();

        foreach (var bill in draftBills)
        {
            bill.Status = BillStatus.Approved;
            bill.ApprovedDate = DateTime.UtcNow;
            await _billRepo.UpdateAsync(bill);

            // Update house debt
            var house = await _houseRepo.GetByIdAsync(bill.HouseId);
            if (house != null)
            {
                house.CurrentDebt += bill.TotalAmount;
                await _houseRepo.UpdateAsync(house);
            }

            await _audit.LogAsync(userId, userName, nameof(Bill), bill.Id.ToString(), "Approved", null,
                $"TotalAmount={bill.TotalAmount}");
        }

        // Handle Once-type and completed Installment financial items
        var financialItems = await _financialItemRepo.GetActiveAsync();
        foreach (var fi in financialItems)
        {
            if (fi.PeriodType == PeriodType.Once)
            {
                fi.IsActive = false;
                await _financialItemRepo.UpdateAsync(fi);
            }
            else if (fi.PeriodType == PeriodType.Installment)
            {
                fi.InstallmentsBilled++;
                if (fi.NumberOfInstallments.HasValue && fi.InstallmentsBilled >= fi.NumberOfInstallments.Value)
                {
                    fi.IsActive = false;
                }
                await _financialItemRepo.UpdateAsync(fi);
            }
        }
    }

    /// <summary>
    /// Records a payment for a bill.
    /// </summary>
    public async Task RecordPaymentAsync(int billId, string userId, string userName)
    {
        var bill = await _billRepo.GetByIdAsync(billId);
        if (bill == null)
            throw new InvalidOperationException("قبض یافت نشد.");
        if (bill.Status != BillStatus.Approved)
            throw new InvalidOperationException("قبض باید تایید شده باشد.");

        var payment = new Payment
        {
            BillId = billId,
            Amount = bill.TotalAmount,
            PaymentDate = DateTime.UtcNow,
            Description = $"پرداخت قبض {bill.Year}/{bill.Month}"
        };

        await _paymentRepo.AddAsync(payment);

        bill.Status = BillStatus.Paid;
        bill.PaidDate = DateTime.UtcNow;
        await _billRepo.UpdateAsync(bill);

        var house = await _houseRepo.GetByIdAsync(bill.HouseId);
        if (house != null)
        {
            house.CurrentDebt -= bill.TotalAmount;
            await _houseRepo.UpdateAsync(house);
        }

        await _audit.LogAsync(userId, userName, nameof(Payment), payment.Id.ToString(), "PaymentConfirmed", null,
            $"BillId={billId}, Amount={bill.TotalAmount}");
    }

    private static bool IsApplicable(FinancialItem fi)
    {
        if (!fi.IsActive) return false;
        if (fi.PeriodType == PeriodType.Installment && fi.NumberOfInstallments.HasValue && fi.InstallmentsBilled >= fi.NumberOfInstallments.Value)
            return false;
        return true;
    }

    private static decimal CalculateGroupingAmount(FinancialItem fi, House house, List<House> allHouses,
        Dictionary<(int HouseId, int FinancialItemId), int> usageByHouseItem, decimal finalAmount)
    {
        if (fi.NumberOfGroups == null || fi.NumberOfGroups.Value <= 0 || fi.GroupPoints.Count == 0)
            return finalAmount / allHouses.Count; // Fallback to equal division

        var groupCount = fi.NumberOfGroups.Value;
        var sortedHouses = allHouses
            .OrderBy(h => usageByHouseItem.GetValueOrDefault((h.Id, fi.Id), 0))
            .ToList();

        var housesPerGroup = sortedHouses.Count / groupCount;
        var remainder = sortedHouses.Count % groupCount;

        // Assign groups to houses
        var houseGroups = new Dictionary<int, int>(); // houseId -> groupNumber
        var index = 0;
        for (int g = 1; g <= groupCount; g++)
        {
            var count = housesPerGroup + (g <= remainder ? 1 : 0);
            for (int i = 0; i < count && index < sortedHouses.Count; i++, index++)
            {
                houseGroups[sortedHouses[index].Id] = g;
            }
        }

        // Get point values per group
        var groupPoints = fi.GroupPoints.ToDictionary(gp => gp.GroupNumber, gp => gp.PointValue);

        // Calculate house points
        decimal housePoint = 0;
        if (houseGroups.TryGetValue(house.Id, out var groupNum) && groupPoints.TryGetValue(groupNum, out var pointVal))
        {
            housePoint = pointVal;
        }

        // Calculate total points
        decimal totalPoints = 0;
        foreach (var h in allHouses)
        {
            if (houseGroups.TryGetValue(h.Id, out var gn) && groupPoints.TryGetValue(gn, out var pv))
            {
                totalPoints += pv;
            }
        }

        if (totalPoints == 0) return finalAmount / allHouses.Count;

        return Math.Round(housePoint / totalPoints * finalAmount, 0);
    }
}
