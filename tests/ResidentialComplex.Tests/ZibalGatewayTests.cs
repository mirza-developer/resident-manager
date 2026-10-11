using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ResidentialComplex.Application.DTOs;
using ResidentialComplex.Infrastructure.Services;
using ResidentialComplex.Infrastructure.Settings;
using Xunit;

namespace ResidentialComplex.Tests;

public class StubHttpHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);
    public List<(string Url, string Body)> Calls { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Calls.Add((request.RequestUri!.ToString(), body));
        return Respond(request);
    }
}

public class StubHttpClientFactory : IHttpClientFactory
{
    public StubHttpHandler Handler { get; } = new();
    public HttpClient CreateClient(string name) => new(Handler, disposeHandler: false);
}

/// <summary>Checks the HTTP contract with Zibal (Docs/Zibal-docs.json) without any network.</summary>
public class ZibalGatewayTests
{
    private const string Merchant = "SECRET-MERCHANT";

    private readonly StubHttpClientFactory _factory = new();
    private readonly ZibalGateway _gateway;

    public ZibalGatewayTests()
    {
        _gateway = new ZibalGateway(_factory,
            Options.Create(new ZibalOptions { Merchant = Merchant, BaseAddress = "https://gateway.zibal.ir/" }),
            NullLogger<ZibalGateway>.Instance);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Request_PostsTheDocumentedBody_AndParsesTrackId()
    {
        _factory.Handler.Respond = _ => Json("{\"trackId\":15966442233311,\"result\":100,\"message\":\"success\"}");

        var result = await _gateway.RequestPaymentAsync(new GatewayRequest(500000, "https://s/payment/callback", "ord1", "desc", "09123456789"));

        Assert.True(result.Success);
        Assert.Equal(15966442233311, result.TrackId);
        var call = _factory.Handler.Calls.Last();
        Assert.Equal("https://gateway.zibal.ir/v1/request", call.Url);
        Assert.Contains($"\"merchant\":\"{Merchant}\"", call.Body);
        Assert.Contains("\"amount\":500000", call.Body);
        Assert.Contains("\"callbackUrl\":", call.Body);
        Assert.Contains("\"orderId\":\"ord1\"", call.Body);
        Assert.Contains("\"mobile\":\"09123456789\"", call.Body);
    }

    [Fact]
    public async Task Request_NeverLeaksTheMerchantIntoStoredData()
    {
        _factory.Handler.Respond = _ => Json("{\"trackId\":1,\"result\":100,\"message\":\"success\"}");

        var result = await _gateway.RequestPaymentAsync(new GatewayRequest(5000, "https://s/cb", "o", "d", null));

        Assert.NotNull(result.RawData);
        Assert.DoesNotContain(Merchant, result.RawData);
        Assert.DoesNotContain("mobile", _factory.Handler.Calls.Last().Body);
    }

    [Fact]
    public async Task Request_WithErrorResult_IsNotSuccess_AndNotATransportError()
    {
        _factory.Handler.Respond = _ => Json("{\"result\":105,\"message\":\"amount must be greater than 1,000 Rials\"}");

        var result = await _gateway.RequestPaymentAsync(new GatewayRequest(10, "https://s/cb", "o", "d", null));

        Assert.False(result.Success);
        Assert.False(result.TransportError);
        Assert.Equal(105, result.ResultCode);
    }

    [Fact]
    public async Task Verify_ParsesAllDocumentedFields()
    {
        _factory.Handler.Respond = _ => Json("{\"paidAt\":\"2018-03-25T23:43:01.053000\",\"cardNumber\":\"62741****1234\",\"status\":2,\"amount\":500000,\"refNumber\":\"1234567\",\"description\":\"d\",\"orderId\":\"ord1\",\"result\":100,\"message\":\"success\"}");

        var info = await _gateway.VerifyAsync(15966442233311);

        var call = _factory.Handler.Calls.Last();
        Assert.EndsWith("/v1/verify", call.Url);
        Assert.Contains("\"trackId\":15966442233311", call.Body);
        Assert.Equal(100, info.ResultCode);
        Assert.Equal(2, info.Status);
        Assert.Equal(500000, info.Amount);
        Assert.Equal("ord1", info.OrderId);
        Assert.Equal("62741****1234", info.CardNumber);
        Assert.Equal(1234567, info.RefNumber);                       // refNumber arrived as a string
        Assert.Equal(new DateTime(2018, 3, 25, 23, 43, 1, 53), info.PaidAt);
        Assert.DoesNotContain(Merchant, info.RawData);
    }

    [Fact]
    public async Task Inquiry_UsesTheInquiryEndpoint_AndToleratesMissingFields()
    {
        _factory.Handler.Respond = _ => Json("{\"status\":-1,\"result\":100,\"message\":\"success\"}");

        var info = await _gateway.InquiryAsync(1);

        Assert.EndsWith("/v1/inquiry", _factory.Handler.Calls.Last().Url);
        Assert.Equal(-1, info.Status);
        Assert.Null(info.Amount);
        Assert.Null(info.PaidAt);
    }

    [Fact]
    public async Task NetworkFailure_BecomesTransportError_WithoutThrowing()
    {
        _factory.Handler.Respond = _ => throw new HttpRequestException("boom");

        var info = await _gateway.InquiryAsync(1);

        Assert.True(info.TransportError);
    }

    [Fact]
    public async Task NonJsonAnswer_BecomesTransportError()
    {
        _factory.Handler.Respond = _ => Json("<html>502 Bad Gateway</html>", HttpStatusCode.BadGateway);

        var verify = await _gateway.VerifyAsync(1);
        var request = await _gateway.RequestPaymentAsync(new GatewayRequest(5000, "https://s/cb", "o", "d", null));

        Assert.True(verify.TransportError);
        Assert.True(request.TransportError);
        Assert.False(request.Success);
    }

    [Fact]
    public async Task Result203_IsSurfaced_NotTreatedAsTransportError()
    {
        _factory.Handler.Respond = _ => Json("{\"result\":203,\"message\":\"trackId invalid\"}");

        var info = await _gateway.VerifyAsync(1);

        Assert.False(info.TransportError);
        Assert.Equal(203, info.ResultCode);
    }

    [Fact]
    public async Task CallerCancellation_StillPropagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _factory.Handler.Respond = _ => throw new TaskCanceledException();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _gateway.InquiryAsync(1, cts.Token));
    }

    [Fact]
    public void StartUrl_AndConfiguredFlag()
    {
        Assert.Equal("https://gateway.zibal.ir/start/77", _gateway.GetStartUrl(77));
        Assert.True(_gateway.IsConfigured);

        var unconfigured = new ZibalGateway(_factory, Options.Create(new ZibalOptions { Merchant = " " }), NullLogger<ZibalGateway>.Instance);
        Assert.False(unconfigured.IsConfigured);
    }
}
