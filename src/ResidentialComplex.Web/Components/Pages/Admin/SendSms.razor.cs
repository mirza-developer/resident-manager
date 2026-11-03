using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using MudBlazor;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Application.Services;
using ResidentialComplex.Domain.Entities;
using ResidentialComplex.Persistence;

namespace ResidentialComplex.Web.Components.Pages.Admin;

[Authorize(Roles = "Administrator")]
public partial class SendSms : ComponentBase
{
    [Inject] private IHouseRepository HouseRepo { get; set; } = default!;
    [Inject] private ISmsService SmsSender { get; set; } = default!;
    [Inject] private IAuditService AuditService { get; set; } = default!;
    [Inject] private UserManager<ApplicationUser> UserManager { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthState { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private const int MaxMessageLength = 1000;

    private List<House> houses = new();
    private List<House> filteredHouses = new();
    private readonly HashSet<int> selectedIds = new();
    private string? filterTitle;
    private string? filterName;
    private string? filterPhone;
    private string? messageText;
    private bool isLoading;
    private bool isSending;
    private int selectableCount;
    private bool allSelected;

    private bool CanSend => !isLoading && !isSending && selectedIds.Count > 0 && !string.IsNullOrWhiteSpace(messageText);

    protected override async Task OnInitializedAsync()
    {
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        isLoading = true;
        try
        {
            houses = await HouseRepo.GetAllAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در بارگذاری واحدها: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void ApplyFilter()
    {
        var title = Normalize(filterTitle);
        var name = Normalize(filterName);
        var phone = Digits(filterPhone);
        filteredHouses = houses.Where(h =>
                (title.Length == 0 || Normalize(h.Title).Contains(title)) &&
                (name.Length == 0 || Normalize(h.ResidentName).Contains(name)) &&
                (phone.Length == 0 || Digits(h.ResidentPhoneNumber).Contains(phone)))
            .ToList();

        // The selection is always a subset of what is currently listed, so nobody that is hidden by a filter
        // can receive the message by accident.
        var visibleIds = filteredHouses.Where(IsSelectable).Select(h => h.Id).ToHashSet();
        selectedIds.IntersectWith(visibleIds);
        selectableCount = visibleIds.Count;
        RefreshSelectAllState();
    }

    private void OnTitleFilterChanged(string? value)
    {
        filterTitle = value;
        ApplyFilter();
    }

    private void OnNameFilterChanged(string? value)
    {
        filterName = value;
        ApplyFilter();
    }

    private void OnPhoneFilterChanged(string? value)
    {
        filterPhone = value;
        ApplyFilter();
    }

    /// <summary>Lower-cases and unifies Arabic/Persian letters and digits so filtering ignores those variants.</summary>
    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var chars = text.Trim().Select(c => c switch
        {
            '\u064A' => '\u06CC',
            '\u0643' => '\u06A9',
            >= '\u06F0' and <= '\u06F9' => (char)('0' + (c - '\u06F0')),
            >= '\u0660' and <= '\u0669' => (char)('0' + (c - '\u0660')),
            _ => char.ToLowerInvariant(c)
        });
        return new string(chars.ToArray());
    }

    private static string Digits(string? text) => new(Normalize(text).Where(char.IsAsciiDigit).ToArray());

    private static bool IsSelectable(House house) => PaymentService.NormalizeMobile(house.ResidentPhoneNumber) is not null;

    private bool IsSelected(int houseId) => selectedIds.Contains(houseId);

    private void SetSelected(int houseId, bool selected)
    {
        if (selected)
        {
            selectedIds.Add(houseId);
        }
        else
        {
            selectedIds.Remove(houseId);
        }

        RefreshSelectAllState();
    }

    /// <summary>Selects (or clears) every selectable house of the filtered list, across all pages of the grid.</summary>
    private void OnSelectAllChanged(bool selected)
    {
        foreach (var house in filteredHouses.Where(IsSelectable))
        {
            if (selected)
            {
                selectedIds.Add(house.Id);
            }
            else
            {
                selectedIds.Remove(house.Id);
            }
        }

        RefreshSelectAllState();
    }

    private void RefreshSelectAllState() =>
        allSelected = selectableCount > 0 && filteredHouses.Where(IsSelectable).All(h => selectedIds.Contains(h.Id));

    private async Task SendAsync()
    {
        if (!CanSend)
        {
            return;
        }

        var confirmed = await DialogService.ShowMessageBox(
            "تأیید ارسال پیامک",
            $"پیامک برای {selectedIds.Count} واحد ارسال می‌شود. ادامه می‌دهید؟",
            yesText: "ارسال",
            cancelText: "انصراف");
        if (confirmed != true)
        {
            return;
        }

        isSending = true;
        try
        {
            var text = messageText!.Trim();
            var outcomes = new List<SmsSendOutcome>();
            var messagedPhones = new HashSet<string>();

            foreach (var house in houses.Where(h => selectedIds.Contains(h.Id)).OrderBy(h => h.Id))
            {
                var phone = PaymentService.NormalizeMobile(house.ResidentPhoneNumber);
                if (phone is null)
                {
                    outcomes.Add(new SmsSendOutcome(house.Id, house.Title, house.ResidentName, house.ResidentPhoneNumber, false, "شماره موبایل نامعتبر است."));
                }
                else if (!messagedPhones.Add(phone))
                {
                    outcomes.Add(new SmsSendOutcome(house.Id, house.Title, house.ResidentName, phone, true, "شماره تکراری؛ پیامک یک‌بار برای این شماره ارسال شد."));
                }
                else
                {
                    try
                    {
                        await SmsSender.SendAsync(phone, text);
                        outcomes.Add(new SmsSendOutcome(house.Id, house.Title, house.ResidentName, phone, true, null));
                    }
                    catch (Exception ex)
                    {
                        outcomes.Add(new SmsSendOutcome(house.Id, house.Title, house.ResidentName, phone, false, ex.Message));
                    }
                }
            }

            var failedCount = outcomes.Count(o => !o.Success);
            var (userId, userName) = await GetCurrentUserAsync();
            // Counts only: neither the text nor phone numbers go to the audit trail.
            await AuditService.LogAsync(userId, userName, "Sms", "Manual", "Sent", null,
                $"Recipients={outcomes.Count}, Sent={outcomes.Count - failedCount}, Failed={failedCount}");

            // Keep the text and re-select exactly the failed houses so the administrator can retry.
            selectedIds.Clear();
            if (failedCount == 0)
            {
                messageText = null;
            }
            else
            {
                foreach (var o in outcomes.Where(o => !o.Success && houses.Any(h => h.Id == o.HouseId && IsSelectable(h))))
                {
                    selectedIds.Add(o.HouseId);
                }
            }

            ApplyFilter();
            isSending = false;
            await ShowResultAsync(outcomes);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در ارسال پیامک: {ex.Message}", Severity.Error);
        }
        finally
        {
            isSending = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task ShowResultAsync(List<SmsSendOutcome> outcomes)
    {
        var parameters = new DialogParameters
        {
            [nameof(SendSmsResultDialog.Outcomes)] = outcomes
        };
        var options = new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true, CloseButton = true };
        await DialogService.ShowAsync<SendSmsResultDialog>(string.Empty, parameters, options);
    }

    private async Task<(string userId, string userName)> GetCurrentUserAsync()
    {
        var auth = await AuthState.GetAuthenticationStateAsync();
        var user = await UserManager.GetUserAsync(auth.User);
        return (user?.Id ?? string.Empty, user?.UserName ?? string.Empty);
    }
}
