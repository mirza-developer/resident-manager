using MudBlazor;
using ResidentialComplex.Application.Helpers;
using ResidentialComplex.Domain.Enums;

namespace ResidentialComplex.Web.Components.Pages.Shared;

/// <summary>Persian labels / colors for online payment statuses, shared by the payment pages.</summary>
public static class PaymentDisplay
{
    public static string StatusLabel(PaymentAttemptStatus status) => status switch
    {
        PaymentAttemptStatus.Initiated => "ایجاد شده",
        PaymentAttemptStatus.Requested => "در انتظار پرداخت",
        PaymentAttemptStatus.RequestFailed => "خطا در اتصال به درگاه",
        PaymentAttemptStatus.CallbackReceived => "در حال بررسی",
        PaymentAttemptStatus.Verifying => "در حال بررسی",
        PaymentAttemptStatus.Verified => "در حال ثبت",
        PaymentAttemptStatus.Succeeded => "موفق",
        PaymentAttemptStatus.Failed => "ناموفق",
        PaymentAttemptStatus.NeedsReview => "نیازمند بررسی مدیر",
        PaymentAttemptStatus.Expired => "منقضی‌شده",
        _ => string.Empty
    };

    public static Color StatusColor(PaymentAttemptStatus status) => status switch
    {
        PaymentAttemptStatus.Succeeded => Color.Success,
        PaymentAttemptStatus.Failed or PaymentAttemptStatus.RequestFailed => Color.Error,
        PaymentAttemptStatus.NeedsReview => Color.Warning,
        PaymentAttemptStatus.Expired or PaymentAttemptStatus.Initiated => Color.Default,
        _ => Color.Info
    };

    public static string EventLabel(PaymentEventType type) => type switch
    {
        PaymentEventType.Created => "ایجاد",
        PaymentEventType.GatewayRequest => "درخواست به درگاه",
        PaymentEventType.Callback => "بازگشت از درگاه",
        PaymentEventType.Inquiry => "استعلام",
        PaymentEventType.Verify => "تایید",
        PaymentEventType.Settled => "ثبت در قبض",
        PaymentEventType.StatusChanged => "تغییر وضعیت",
        PaymentEventType.Warning => "هشدار",
        _ => string.Empty
    };

    public static string Date(DateTime? value) => value.HasValue ? PersianCalendarHelper.ToPersianDateTimeString(value.Value) : "—";

    public static string Money(decimal? value) => value.HasValue ? $"{value.Value:N0} ریال" : "—";
}
