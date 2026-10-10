using ResidentialComplex.Web.Security;
using Xunit;

namespace ResidentialComplex.Tests;

/// <summary>Security helpers around the login → bank → back-to-site round trip.</summary>
public class PaymentWebHelperTests
{
    private const string Host = "site.example";

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("/", "/")]
    [InlineData("/payments/result/7c1b", "/payments/result/7c1b")]
    [InlineData("/a?b=1&c=2", "/a?b=1&c=2")]
    [InlineData("https://site.example/payments/result/1", "/payments/result/1")]
    [InlineData("HTTPS://SITE.EXAMPLE/x", "/x")]
    // --- everything below must be rejected (falls back to "/") ---
    [InlineData("//evil.com", "/")]
    [InlineData("/\\evil.com", "/")]
    [InlineData("\\\\evil.com", "/")]
    [InlineData("\\evil.com", "/")]
    [InlineData("https://evil.com/x", "/")]
    [InlineData("http://evil.com", "/")]
    [InlineData("javascript:alert(1)", "/")]
    [InlineData("data:text/html,x", "/")]
    [InlineData("ftp://site.example/x", "/")]
    [InlineData("https://site.example:5001/a", "/")]               // different port = different origin
    [InlineData("http://site.example@evil.com/", "/")]            // userinfo trick, real host is evil.com
    [InlineData("https://site.example.evil.com/", "/")]
    [InlineData("https://site.example//evil.com", "/")]           // would become scheme-relative
    public void ReturnUrl_IsAlwaysLocal(string? input, string expected)
    {
        Assert.Equal(expected, ReturnUrlHelper.GetSafeLocalUrl(input, Host));
    }

    [Theory]
    [InlineData("/ok\r\nSet-Cookie: x=1")]
    [InlineData("/ok\nx")]
    public void ReturnUrl_WithControlCharacters_IsRejected(string input)
    {
        Assert.Equal("/", ReturnUrlHelper.GetSafeLocalUrl(input, Host));
    }

    [Fact]
    public void ReturnUrl_AbsoluteUrlWithPort_MatchesOnlyThatHostAndPort()
    {
        Assert.Equal("/a?b=1", ReturnUrlHelper.GetSafeLocalUrl("https://site.example:5001/a?b=1", "site.example:5001"));
    }

    [Theory]
    [InlineData(null, "https://site.example/", "https://site.example/payment/callback")]
    [InlineData("", "https://site.example/app/", "https://site.example/app/payment/callback")]
    [InlineData("https://resident.example.ir", "http://localhost:5000/", "https://resident.example.ir/payment/callback")]
    [InlineData(null, "https://localhost:5001/", "https://localhost:5001/payment/callback")]
    public void CallbackUrl_IsBuiltFromConfigOrCurrentHost(string? configured, string current, string expected)
    {
        Assert.Equal(expected, PaymentCallbackUrl.Build(configured, current));
    }
}
