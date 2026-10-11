namespace ResidentialComplex.Web.Captcha;

/// <summary>Why a captcha check did not succeed.</summary>
public enum CaptchaFailureReason
{
    /// <summary>The user typed a wrong answer.</summary>
    Incorrect,
    /// <summary>The challenge lived longer than <c>ExpirySeconds</c>.</summary>
    Expired,
    /// <summary>Too many wrong attempts on one challenge (a new challenge is issued automatically).</summary>
    TooManyAttempts,
    /// <summary>The user submitted an empty answer.</summary>
    Empty
}

/// <summary>Raised through <c>OnSuccess</c>.</summary>
/// <param name="Attempts">How many tries the user needed for this challenge.</param>
/// <param name="Elapsed">Time between showing the challenge and the correct answer.</param>
public sealed record CaptchaSuccessEventArgs(int Attempts, TimeSpan Elapsed);

/// <summary>Raised through <c>OnFailure</c>.</summary>
/// <param name="Reason">Failure category.</param>
/// <param name="AttemptsUsed">Attempts used on the current challenge.</param>
/// <param name="AttemptsLeft">Attempts remaining (0 when a new challenge was issued).</param>
public sealed record CaptchaFailureEventArgs(CaptchaFailureReason Reason, int AttemptsUsed, int AttemptsLeft);
