namespace ResidentialComplex.Application.Helpers;

/// <summary>
/// Zibal payment status / result code tables (from Docs/Zibal-docs.json) with Persian
/// descriptions and the classification used by <see cref="Services.PaymentService"/>.
/// </summary>
public static class ZibalStatusCodes
{
    // ----- payment status ("status") -----
    public const int Waiting = -1;
    public const int InternalError = -2;
    public const int PaidVerified = 1;
    public const int PaidNotVerified = 2;

    // ----- result codes ("result") -----
    public const int ResultOk = 100;
    public const int ResultAlreadyVerified = 201;
    public const int ResultNotPaid = 202;
    public const int ResultInvalidTrackId = 203;

    private static readonly Dictionary<int, string> StatusText = new()
    {
        [-1] = "در انتظار پرداخت",
        [-2] = "خطای داخلی درگاه",
        [1] = "پرداخت شده - تاییدشده",
        [2] = "پرداخت شده - تاییدنشده",
        [3] = "لغوشده توسط کاربر",
        [4] = "شماره کارت نامعتبر است",
        [5] = "موجودی حساب کافی نیست",
        [6] = "رمز واردشده اشتباه است",
        [7] = "تعداد درخواست‌ها بیش از حد مجاز است",
        [8] = "تعداد پرداخت اینترنتی روزانه بیش از حد مجاز است",
        [9] = "مبلغ پرداخت اینترنتی روزانه بیش از حد مجاز است",
        [10] = "صادرکننده‌ی کارت نامعتبر است",
        [11] = "خطای سوییچ",
        [12] = "کارت قابل دسترسی نیست",
        [15] = "تراکنش استرداد شده",
        [16] = "تراکنش در حال استرداد",
        [18] = "تراکنش ریورس شده",
        [21] = "پذیرنده نامعتبر است"
    };

    private static readonly Dictionary<int, string> RequestResultText = new()
    {
        [100] = "با موفقیت تایید شد",
        [102] = "merchant یافت نشد",
        [103] = "merchant غیرفعال است یا قرارداد درگاه امضا نشده",
        [104] = "merchant نامعتبر است",
        [105] = "مبلغ باید بزرگتر از ۱٬۰۰۰ ریال باشد",
        [106] = "آدرس بازگشت (callbackUrl) نامعتبر است",
        [113] = "مبلغ تراکنش از سقف مجاز بیشتر است",
        [115] = "IP سرور در پنل زیبال ثبت نشده است"
    };

    private static readonly Dictionary<int, string> VerifyResultText = new()
    {
        [100] = "با موفقیت تایید شد",
        [102] = "merchant یافت نشد",
        [103] = "merchant غیرفعال است",
        [104] = "merchant نامعتبر است",
        [201] = "قبلا تایید شده است",
        [202] = "سفارش پرداخت نشده یا ناموفق بوده است",
        [203] = "trackId نامعتبر است"
    };

    public static string DescribeStatus(int? status) =>
        status.HasValue && StatusText.TryGetValue(status.Value, out var text) ? text : $"وضعیت نامشخص ({status?.ToString() ?? "-"})";

    public static string DescribeRequestResult(int code) =>
        RequestResultText.TryGetValue(code, out var text) ? text : $"کد نتیجه {code}";

    public static string DescribeVerifyResult(int code) =>
        VerifyResultText.TryGetValue(code, out var text) ? text : $"کد نتیجه {code}";

    /// <summary>
    /// True for statuses that mean "this session will never be paid": cancelled, declined,
    /// invalid card, limits, switch errors, refunded / reversed, invalid merchant.
    /// </summary>
    public static bool IsPermanentFailure(int status) =>
        status is 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 15 or 16 or 18 or 21;
}
