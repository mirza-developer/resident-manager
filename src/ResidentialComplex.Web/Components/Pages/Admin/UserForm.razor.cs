using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using MudBlazor;
using ResidentialComplex.Persistence;

namespace ResidentialComplex.Web.Components.Pages.Admin;

[Authorize(Roles = "Administrator")]
public partial class UserForm : ComponentBase
{
    [Parameter] public string? Id { get; set; }

    [Inject] private UserManager<ApplicationUser> UserManager { get; set; } = default!;
    [Inject] private RoleManager<IdentityRole> RoleManager { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private readonly List<string> roles = new();
    private UserFormModel formModel = new();
    private bool isLoading;
    private bool notFound;

    private bool IsEditMode => !string.IsNullOrWhiteSpace(Id);

    protected override async Task OnParametersSetAsync()
    {
        isLoading = true;
        try
        {
            roles.Clear();
            roles.AddRange(RoleManager.Roles.Select(x => x.Name!).Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => x));
            if (!roles.Any())
            {
                roles.AddRange(new[] { "Administrator", "Worker", "Resident" });
            }

            if (!IsEditMode)
            {
                formModel = new UserFormModel { Role = roles.Contains("Resident") ? "Resident" : roles.First() };
                notFound = false;
                return;
            }

            var user = await UserManager.FindByIdAsync(Id!);
            if (user is null)
            {
                notFound = true;
                return;
            }

            var currentRoles = await UserManager.GetRolesAsync(user);
            formModel = new UserFormModel
            {
                Id = user.Id,
                FullName = user.FullName,
                UserName = user.UserName ?? string.Empty,
                Role = currentRoles.FirstOrDefault() ?? roles.First()
            };
            notFound = false;
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در بارگذاری کاربر: {ex.Message}", Severity.Error);
            notFound = true;
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task SaveAsync()
    {
        isLoading = true;
        try
        {
            if (IsEditMode)
            {
                var user = await UserManager.FindByIdAsync(formModel.Id);
                if (user is null)
                {
                    Snackbar.Add("کاربر یافت نشد.", Severity.Warning);
                    return;
                }

                user.FullName = formModel.FullName;
                var updateResult = await UserManager.UpdateAsync(user);
                if (!updateResult.Succeeded)
                {
                    Snackbar.Add(string.Join("، ", updateResult.Errors.Select(x => x.Description)), Severity.Error);
                    return;
                }

                var currentRoles = await UserManager.GetRolesAsync(user);
                if (currentRoles.Any())
                {
                    await UserManager.RemoveFromRolesAsync(user, currentRoles);
                }
                await UserManager.AddToRoleAsync(user, formModel.Role);
                Snackbar.Add("کاربر با موفقیت بروزرسانی شد.", Severity.Success);
            }
            else
            {
                var user = new ApplicationUser
                {
                    UserName = formModel.UserName,
                    FullName = formModel.FullName
                };
                var createResult = await UserManager.CreateAsync(user, formModel.Password!);
                if (!createResult.Succeeded)
                {
                    Snackbar.Add(string.Join("، ", createResult.Errors.Select(x => x.Description)), Severity.Error);
                    return;
                }

                await UserManager.AddToRoleAsync(user, formModel.Role);
                Snackbar.Add("کاربر با موفقیت ایجاد شد.", Severity.Success);
            }

            Navigation.NavigateTo("/admin/users");
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در ذخیره کاربر: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void GoBack() => Navigation.NavigateTo("/admin/users");

    private static string GetRoleLabel(string role) => role switch
    {
        "Administrator" => "مدیر",
        "Worker" => "کارگر",
        "Resident" => "ساکن",
        _ => role
    };

    private sealed class UserFormModel : IValidatableObject
    {
        public string Id { get; set; } = string.Empty;

        [Required(ErrorMessage = "نام کامل الزامی است.")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "نام کاربری الزامی است.")]
        public string UserName { get; set; } = string.Empty;

        public string? Password { get; set; }

        [Required(ErrorMessage = "نقش الزامی است.")]
        public string Role { get; set; } = "Resident";

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(Id) && string.IsNullOrWhiteSpace(Password))
            {
                yield return new ValidationResult("رمز عبور الزامی است.", new[] { nameof(Password) });
            }
        }
    }
}
