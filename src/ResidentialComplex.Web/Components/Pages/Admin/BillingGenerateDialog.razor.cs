using Microsoft.AspNetCore.Components;
using MudBlazor;
using ResidentialComplex.Domain.Enums;

namespace ResidentialComplex.Web.Components.Pages.Admin;
public partial class BillingGenerateDialog
{
    [CascadingParameter] private MudDialogInstance MudDialog { get; set; } = default!;

    [Parameter] public int InitialYear { get; set; }
    [Parameter] public int InitialMonth { get; set; }
    [Parameter] public List<Billing.FinancialItemAmountRow> FinancialAmountRows { get; set; } = [];
    [Parameter] public Func<int, int, Task> OnGenerate { get; set; } = default!;

    private int year;
    private int month;

    protected override async Task OnInitializedAsync()
    {
        year = InitialYear;
        month = InitialMonth;
    }

    private async Task GenerateClickedAsync()
    {
        await OnGenerate(year, month);
        MudDialog.Close();
    }

    private void Cancel() => MudDialog.Cancel();

    private static string GetPeriodLabel(PeriodType periodType) => periodType switch
    {
        PeriodType.Once => "یکبار",
        PeriodType.Permanent => "دائمی",
        PeriodType.Installment => "اقساط",
        _ => string.Empty
    };

    private static string GetCalcLabel(CalculationType calculationType) => calculationType switch
    {
        CalculationType.EqualDivision => "تقسیم مساوی",
        CalculationType.Grouping => "تعرفه پلکانی (IBT)",
        _ => string.Empty
    };
}
