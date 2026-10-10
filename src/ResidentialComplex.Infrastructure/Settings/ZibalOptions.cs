namespace ResidentialComplex.Infrastructure.Settings;

/// <summary>
/// Configuration of the Zibal payment gateway ("Zibal" section of appsettings).
/// Keep the real merchant out of source control (environment variable Zibal__Merchant or user-secrets).
/// </summary>
public class ZibalOptions
{
    public const string SectionName = "Zibal";

    /// <summary>
    /// Merchant id from the Zibal panel. Empty = online payment disabled.
    /// "zibal" is Zibal's TEST merchant (simulated payments) — never use it in production.
    /// </summary>
    public string Merchant { get; set; } = string.Empty;

    public string BaseAddress { get; set; } = "https://gateway.zibal.ir";

    /// <summary>
    /// Optional public base URL of this site (e.g. https://resident.example.ir) used to build the
    /// callback URL. When empty, the host the user is browsing is used.
    /// </summary>
    public string CallbackBaseUrl { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 30;
}
