using System.Text;

namespace ResidentialComplex.Web.Captcha;

public enum CaptchaLanguage { English, Persian }

/// <summary>Built-in texts and digit helpers. No resource files, so the component stays fully standalone.</summary>
internal static class CaptchaLocalizer
{
    private const string PersianDigits = "۰۱۲۳۴۵۶۷۸۹";

    public static string T(CaptchaLanguage lang, string key) => (lang, key) switch
    {
        (CaptchaLanguage.Persian, "title") => "تأیید هویت",
        (CaptchaLanguage.Persian, "prompt_text") => "کدی را که در تصویر می‌بینید وارد کنید",
        (CaptchaLanguage.Persian, "prompt_math") => "حاصل عبارت زیر را وارد کنید",
        (CaptchaLanguage.Persian, "placeholder") => "پاسخ",
        (CaptchaLanguage.Persian, "verify") => "تأیید",
        (CaptchaLanguage.Persian, "refresh") => "تصویر جدید",
        (CaptchaLanguage.Persian, "success") => "تأیید شد ✓",
        (CaptchaLanguage.Persian, "incorrect") => "پاسخ نادرست است. دوباره تلاش کنید.",
        (CaptchaLanguage.Persian, "empty") => "لطفاً پاسخ را وارد کنید.",
        (CaptchaLanguage.Persian, "expired") => "زمان این تصویر تمام شد. تصویر جدید ساخته شد.",
        (CaptchaLanguage.Persian, "too_many") => "تعداد تلاش‌ها زیاد بود. تصویر جدید ساخته شد.",
        (CaptchaLanguage.Persian, "attempts_left") => "تلاش باقی‌مانده",
        (CaptchaLanguage.Persian, "image_alt") => "تصویر کپچا",

        (_, "title") => "Verification",
        (_, "prompt_text") => "Type the code shown in the image",
        (_, "prompt_math") => "Solve the expression below",
        (_, "placeholder") => "Answer",
        (_, "verify") => "Verify",
        (_, "refresh") => "New image",
        (_, "success") => "Verified ✓",
        (_, "incorrect") => "Incorrect answer. Please try again.",
        (_, "empty") => "Please enter an answer.",
        (_, "expired") => "This challenge expired. A new one was generated.",
        (_, "too_many") => "Too many attempts. A new challenge was generated.",
        (_, "attempts_left") => "Attempts left",
        (_, "image_alt") => "Captcha image",
    };

    /// <summary>Converts Latin digits to Persian digits (display only).</summary>
    public static string ToPersianDigits(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) sb.Append(c is >= '0' and <= '9' ? PersianDigits[c - '0'] : c);
        return sb.ToString();
    }

    /// <summary>
    /// Normalizes user input: maps Persian/Arabic-Indic digits to Latin, strips whitespace,
    /// zero-width characters and bidi marks, and upper-cases Latin letters.
    /// </summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (char.IsWhiteSpace(c) || c is '\u200c' or '\u200d' or '\u200e' or '\u200f' or '\u202a' or '\u202b' or '\u202c')
                continue;
            if (c is >= '\u06F0' and <= '\u06F9') sb.Append((char)('0' + (c - '\u06F0')));      // Persian
            else if (c is >= '\u0660' and <= '\u0669') sb.Append((char)('0' + (c - '\u0660'))); // Arabic-Indic
            else sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }
}
