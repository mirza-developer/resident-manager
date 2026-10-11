using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ResidentialComplex.Web.Captcha;

public enum CaptchaMode
{
    /// <summary>Distorted random code (digits, or unambiguous letters+digits).</summary>
    Text,
    /// <summary>Simple arithmetic expression, e.g. 7 + 4 (with Persian digits in Persian mode).</summary>
    Math
}

/// <summary>
/// One captcha challenge. The expected answer is never stored in plain text:
/// only a salted HMAC-SHA256 is kept, and comparison is constant-time.
/// The image is an inline SVG data URI generated in C# (no image files, no JS, no canvas).
/// </summary>
internal sealed class CaptchaChallenge
{
    // No 0/O, 1/I/L, 2/Z, 5/S, 8/B — the classic look-alikes.
    private const string SafeAlphabet = "34679ACDEFGHJKMNPQRTUVWXY";
    private const string Digits = "0123456789";

    private readonly byte[] _salt = RandomNumberGenerator.GetBytes(16);
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly byte[] _answerMac;

    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.Now;
    public string ImageDataUri { get; }
    public CaptchaMode Mode { get; }

    private CaptchaChallenge(CaptchaMode mode, string display, string answer, CaptchaLanguage lang)
    {
        Mode = mode;
        _answerMac = Mac(answer);
        ImageDataUri = RenderSvg(display, lang, mode);
    }

    public static CaptchaChallenge Create(CaptchaMode mode, CaptchaLanguage lang, int length)
    {
        if (mode == CaptchaMode.Math)
        {
            int a = RandomNumberGenerator.GetInt32(2, 13);
            int b = RandomNumberGenerator.GetInt32(2, 10);
            int op = RandomNumberGenerator.GetInt32(0, 3); // + - ×
            if (op == 1 && b > a) (a, b) = (b, a);          // keep result non-negative
            int result = op switch { 0 => a + b, 1 => a - b, _ => a * b };
            string sym = op switch { 0 => "+", 1 => "−", _ => "×" };
            string expr = $"{a} {sym} {b} = ?";
            if (lang == CaptchaLanguage.Persian) expr = CaptchaLocalizer.ToPersianDigits(expr);
            return new CaptchaChallenge(mode, expr, result.ToString(CultureInfo.InvariantCulture), lang);
        }

        // Persian users can type their own digits, so Persian mode uses digits only.
        string alphabet = lang == CaptchaLanguage.Persian ? Digits : SafeAlphabet;
        var code = new StringBuilder(length);
        for (int i = 0; i < length; i++) code.Append(alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]);
        string answer = code.ToString();
        string shown = lang == CaptchaLanguage.Persian ? CaptchaLocalizer.ToPersianDigits(answer) : answer;
        return new CaptchaChallenge(mode, shown, answer, lang);
    }

    public bool IsExpired(int expirySeconds) =>
        DateTimeOffset.Now - CreatedAt > TimeSpan.FromSeconds(expirySeconds);

    public bool Check(string? userInput)
    {
        var candidate = Mac(CaptchaLocalizer.Normalize(userInput));
        return CryptographicOperations.FixedTimeEquals(candidate, _answerMac);
    }

    private byte[] Mac(string value)
    {
        var data = new byte[_salt.Length + Encoding.UTF8.GetByteCount(value)];
        _salt.CopyTo(data, 0);
        Encoding.UTF8.GetBytes(value, data.AsSpan(_salt.Length));
        return HMACSHA256.HashData(_key, data);
    }

    // ---------- SVG rendering ----------

    private static int R(int min, int max) => RandomNumberGenerator.GetInt32(min, max);
    private static string F(double d) => d.ToString("0.#", CultureInfo.InvariantCulture);

    private static string RenderSvg(string text, CaptchaLanguage lang, CaptchaMode mode)
    {
        const int w = 240, h = 80;
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns='http://www.w3.org/2000/svg' width='{w}' height='{h}' viewBox='0 0 {w} {h}'>");
        sb.Append($"<rect width='{w}' height='{h}' fill='rgb({R(235, 252)},{R(235, 252)},{R(235, 252)})'/>");

        // Wavy displacement filter makes OCR harder but keeps glyphs readable.
        sb.Append("<defs><filter id='d' x='-5%' y='-5%' width='110%' height='110%'>");
        sb.Append($"<feTurbulence type='fractalNoise' baseFrequency='0.{R(1, 3)}{R(1, 9)}' numOctaves='2' seed='{R(1, 9999)}'/>");
        sb.Append($"<feDisplacementMap in='SourceGraphic' scale='{R(5, 9)}'/></filter></defs>");

        // Background noise lines
        for (int i = 0; i < 7; i++)
            sb.Append($"<path d='M{R(0, w)} {R(0, h)} C {R(0, w)} {R(0, h)}, {R(0, w)} {R(0, h)}, {R(0, w)} {R(0, h)}' " +
                      $"stroke='rgb({R(90, 200)},{R(90, 200)},{R(90, 200)})' stroke-width='{F(1 + R(0, 15) / 10.0)}' fill='none'/>");

        // Glyphs. Math expressions are laid out as a single centered run (so RTL/LTR digits stay correct);
        // text codes are placed per character with random rotation/offset.
        string fontFamily = lang == CaptchaLanguage.Persian ? "Vazirmatn,Tahoma,'Segoe UI',sans-serif" : "'Courier New',Consolas,monospace";
        sb.Append("<g filter='url(#d)'>");
        if (mode == CaptchaMode.Math)
        {
            sb.Append($"<text x='{w / 2}' y='{h / 2 + 12}' text-anchor='middle' direction='ltr' unicode-bidi='plaintext' " +
                      $"font-family=\"{fontFamily}\" font-size='38' font-weight='700' fill='rgb({R(20, 90)},{R(20, 90)},{R(20, 90)})'>" +
                      $"{Esc(text)}</text>");
        }
        else
        {
            int n = text.Length;
            double step = (w - 40.0) / n;
            for (int i = 0; i < n; i++)
            {
                double x = 28 + i * step + R(-4, 5);
                double y = h / 2.0 + 14 + R(-8, 9);
                int rot = R(-22, 23);
                int size = R(34, 44);
                sb.Append($"<text x='{F(x)}' y='{F(y)}' text-anchor='middle' transform='rotate({rot} {F(x)} {F(y)})' " +
                          $"font-family=\"{fontFamily}\" font-size='{size}' font-weight='700' " +
                          $"fill='rgb({R(20, 100)},{R(20, 100)},{R(20, 100)})'>{Esc(text[i].ToString())}</text>");
            }
        }
        sb.Append("</g>");

        // Foreground strike-through lines
        for (int i = 0; i < 3; i++)
            sb.Append($"<line x1='{R(0, 40)}' y1='{R(10, h - 10)}' x2='{R(w - 40, w)}' y2='{R(10, h - 10)}' " +
                      $"stroke='rgb({R(60, 160)},{R(60, 160)},{R(60, 160)})' stroke-width='{F(1 + R(0, 10) / 10.0)}' opacity='.75'/>");
        // Dots
        for (int i = 0; i < 40; i++)
            sb.Append($"<circle cx='{R(0, w)}' cy='{R(0, h)}' r='{F(0.6 + R(0, 10) / 10.0)}' fill='rgb({R(60, 200)},{R(60, 200)},{R(60, 200)})'/>");

        sb.Append("</svg>");
        return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
