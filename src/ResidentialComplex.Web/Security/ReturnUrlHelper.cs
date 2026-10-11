namespace ResidentialComplex.Web.Security;

/// <summary>
/// Validates the post-login <c>returnUrl</c>. Only local paths (or absolute URLs on the same host)
/// are accepted, so a crafted login link can never bounce the user — who is about to type a
/// password or has just paid — to another site (open-redirect protection).
/// </summary>
public static class ReturnUrlHelper
{
    /// <summary>Returns a safe local path+query, or "/" when the value is empty or unsafe.</summary>
    /// <param name="returnUrl">Value received from the login form / query string.</param>
    /// <param name="requestHost">Host (with port when not default) the current request was made to.</param>
    public static string GetSafeLocalUrl(string? returnUrl, string requestHost)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) || returnUrl.Any(char.IsControl))
            return "/";

        if (returnUrl[0] == '/')
            return IsSafeLocalPath(returnUrl) ? returnUrl : "/";

        if (Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && string.Equals(uri.Authority, requestHost, StringComparison.OrdinalIgnoreCase))
        {
            var local = uri.PathAndQuery;
            return IsSafeLocalPath(local) ? local : "/";
        }

        return "/";
    }

    private static bool IsSafeLocalPath(string path) =>
        path.Length > 0 && path[0] == '/'
        && (path.Length == 1 || (path[1] != '/' && path[1] != '\\'))
        && !path.Contains('\\');
}
