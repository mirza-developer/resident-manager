using Microsoft.AspNetCore.Components;
using MudBlazor;
using ResidentialComplex.Application.Helpers;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Domain.Entities;

namespace ResidentialComplex.Web.Components.Pages.Admin;

public partial class SmsTemplateEditDialog : ComponentBase
{
    [CascadingParameter] private MudDialogInstance MudDialog { get; set; } = default!;
    [Inject] private ISmsTemplateRepository SmsTemplateRepo { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    [Parameter] public int TemplateId { get; set; }

    private SmsTemplate? template;
    private IReadOnlyList<(string Token, string Description)> placeholders = Array.Empty<(string, string)>();
    private bool isLoading;
    private bool isSaving;

    /// <summary>Generic sample values shown in the preview, keyed by placeholder token (without braces).</summary>
    private static readonly Dictionary<string, string> SampleValues = new()
    {
        ["ResidentName"] = "علی محمدی",
        ["HouseTitle"] = "واحد ۱۲",
        ["ApartmentTitle"] = "برج یاس",
        ["PeriodTitle"] = "فروردین ۱۴۰۴",
        ["TotalAmount"] = "۱٬۲۰۰٬۰۰۰",
        ["CurrentDebt"] = "۳٬۴۰۰٬۰۰۰"
    };

    private string PreviewText
    {
        get
        {
            if (template is null)
            {
                return string.Empty;
            }

            var values = placeholders.ToDictionary(
                p => p.Token.Trim('{', '}'),
                p => SampleValues.GetValueOrDefault(p.Token.Trim('{', '}'), "مقدار نمونه"));

            return SmsTemplateRenderer.Render(template.Text, values);
        }
    }

    protected override async Task OnInitializedAsync()
    {
        isLoading = true;
        try
        {
            template = await SmsTemplateRepo.GetByIdAsync(TemplateId);
            if (template is null)
            {
                Snackbar.Add("قالب پیامک یافت نشد.", Severity.Error);
                MudDialog.Cancel();
                return;
            }

            placeholders = SmsTemplateRenderer.GetPlaceholders(template.Key);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در بارگذاری قالب پیامک: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
        }
    }

    private void InsertPlaceholder(string token)
    {
        if (template is null)
        {
            return;
        }

        var separator = string.IsNullOrEmpty(template.Text) || template.Text.EndsWith(' ') || template.Text.EndsWith('\n')
            ? string.Empty
            : " ";
        template.Text += separator + token;
    }

    private async Task SaveAsync()
    {
        if (template is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(template.Title) || string.IsNullOrWhiteSpace(template.Text))
        {
            Snackbar.Add("عنوان و متن قالب نمی‌توانند خالی باشند.", Severity.Warning);
            return;
        }

        isSaving = true;
        try
        {
            template.UpdatedAtUtc = DateTime.Now;
            await SmsTemplateRepo.UpdateAsync(template);
            Snackbar.Add("قالب پیامک با موفقیت ذخیره شد.", Severity.Success);
            MudDialog.Close(DialogResult.Ok(true));
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در ذخیره قالب پیامک: {ex.Message}", Severity.Error);
        }
        finally
        {
            isSaving = false;
        }
    }

    private void Cancel() => MudDialog.Cancel();
}
