namespace ResidentialComplex.Web.Security;

/// <summary>Builds the absolute URL Zibal redirects the user back to.</summary>
public static class PaymentCallbackUrl
{
    public const string Path = "payment/callback";

    /// <param name="configuredBaseUrl">Optional <c>Zibal:CallbackBaseUrl</c> (preferred in production).</param>
    /// <param name="currentBaseUri">The host the user is browsing (NavigationManager.BaseUri); used when nothing is configured
    /// so the auth cookie of that host is sent back on return.</param>
    public static string Build(string? configuredBaseUrl, string currentBaseUri)
    {
        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl) ? currentBaseUri : configuredBaseUrl.Trim();
        if (!baseUrl.EndsWith('/')) baseUrl += "/";
        return new Uri(new Uri(baseUrl), Path).ToString();
    }
}
