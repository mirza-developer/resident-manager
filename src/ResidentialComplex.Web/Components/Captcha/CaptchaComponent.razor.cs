using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace ResidentialComplex.Web.Captcha;
public partial class CaptchaComponent
{
    private static int _idCounter;
    private readonly string _titleId = $"bc-t-{Interlocked.Increment(ref _idCounter)}";
    private readonly string _msgId = $"bc-m-{Interlocked.Increment(ref _idCounter)}";

    private CaptchaChallenge? _challenge;
    private string? _answer;
    private string? _message;
    private int _attempts;
    private bool _verified;
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;

    // ---- Parameters ----
    /// <summary>UI language. Null = infer from CultureInfo.CurrentUICulture ("fa" → Persian).</summary>
    [Parameter] public CaptchaLanguage? Language { get; set; }
    /// <summary>Text (distorted code) or Math (arithmetic).</summary>
    [Parameter] public CaptchaMode Mode { get; set; } = CaptchaMode.Text;
    /// <summary>Length of the text code (4–8).</summary>
    [Parameter] public int Length { get; set; } = 5;
    /// <summary>Seconds before a challenge expires.</summary>
    [Parameter] public int ExpirySeconds { get; set; } = 120;
    /// <summary>Wrong tries allowed before a new challenge is issued.</summary>
    [Parameter] public int MaxAttempts { get; set; } = 3;
    /// <summary>Minimum seconds between manual refreshes (anti-hammering).</summary>
    [Parameter] public int RefreshCooldownSeconds { get; set; } = 2;
    /// <summary>Extra CSS class on the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Inline styles for the root element.</summary>
    [Parameter] public string? Style { get; set; }

    // ---- Events (the only way the component talks to the outside) ----
    [Parameter] public EventCallback<CaptchaSuccessEventArgs> OnSuccess { get; set; }
    [Parameter] public EventCallback<CaptchaFailureEventArgs> OnFailure { get; set; }

    private bool IsPersian => (Language ?? (System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fa"
                                  ? CaptchaLanguage.Persian : CaptchaLanguage.English)) == CaptchaLanguage.Persian;
    private CaptchaLanguage Lang => IsPersian ? CaptchaLanguage.Persian : CaptchaLanguage.English;
    private int AttemptsLeft => Math.Max(0, Math.Max(1, MaxAttempts) - _attempts);
    private bool CanRefresh => (DateTimeOffset.Now - _lastRefresh).TotalSeconds >= RefreshCooldownSeconds;
    private string T(string key) => CaptchaLocalizer.T(Lang, key);
    private string FormatNumber(int n) => IsPersian ? CaptchaLocalizer.ToPersianDigits(n.ToString()) : n.ToString();

    protected override void OnInitialized() => NewChallenge();

    /// <summary>Resets the component so it can be used again (e.g. after a failed form submit on the server).</summary>
    public void Reset()
    {
        _verified = false;
        NewChallenge();
        StateHasChanged();
    }

    private void NewChallenge(string? message = null)
    {
        _challenge = CaptchaChallenge.Create(Mode, Lang, Math.Clamp(Length, 4, 8));
        _answer = string.Empty;
        _attempts = 0;
        _message = message;
        _lastRefresh = DateTimeOffset.Now;
    }

    private void Refresh()
    {
        if (_verified || !CanRefresh) return;
        NewChallenge();
    }

    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") await Verify();
    }

    private async Task Verify()
    {
        if (_verified || _challenge is null) return;

        int max = Math.Max(1, MaxAttempts);

        if (_challenge.IsExpired(ExpirySeconds))
        {
            NewChallenge(T("expired"));
            await OnFailure.InvokeAsync(new(CaptchaFailureReason.Expired, 0, max));
            return;
        }

        if (string.IsNullOrWhiteSpace(_answer))
        {
            _message = T("empty");
            await OnFailure.InvokeAsync(new(CaptchaFailureReason.Empty, _attempts, AttemptsLeft));
            return;
        }

        _attempts++;

        if (_challenge.Check(_answer))
        {
            var elapsed = DateTimeOffset.Now - _challenge.CreatedAt;
            int used = _attempts;
            _verified = true;
            _challenge = null;   // single use: the challenge is discarded
            _answer = null;
            _message = null;
            await OnSuccess.InvokeAsync(new(used, elapsed));
            return;
        }

        if (_attempts >= max)
        {
            NewChallenge(T("too_many"));
            await OnFailure.InvokeAsync(new(CaptchaFailureReason.TooManyAttempts, max, 0));
            return;
        }

        _answer = string.Empty;
        _message = T("incorrect");
        await OnFailure.InvokeAsync(new(CaptchaFailureReason.Incorrect, _attempts, AttemptsLeft));
    }
}
