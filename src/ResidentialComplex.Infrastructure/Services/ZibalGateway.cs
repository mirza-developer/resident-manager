using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResidentialComplex.Application.DTOs;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Infrastructure.Settings;

namespace ResidentialComplex.Infrastructure.Services;

/// <summary>
/// Zibal IPG client (Docs/Zibal-docs.json): request, inquiry and verify.
/// Never throws for network/gateway problems (returns <c>TransportError</c>) and never puts the
/// merchant id into logs or into the returned <c>RawData</c> (only the gateway's JSON answer is kept).
/// </summary>
public class ZibalGateway : IPaymentGateway
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ZibalOptions _options;
    private readonly ILogger<ZibalGateway> _logger;

    public ZibalGateway(IHttpClientFactory httpClientFactory, IOptions<ZibalOptions> options, ILogger<ZibalGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "Zibal";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Merchant);

    public string GetStartUrl(long trackId) => $"{_options.BaseAddress.TrimEnd('/')}/start/{trackId}";

    public async Task<GatewayRequestResult> RequestPaymentAsync(GatewayRequest request, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object>
        {
            ["merchant"] = _options.Merchant,
            ["amount"] = request.AmountRial,
            ["callbackUrl"] = request.CallbackUrl,
            ["orderId"] = request.OrderId,
            ["description"] = request.Description
        };
        if (!string.IsNullOrEmpty(request.Mobile))
            body["mobile"] = request.Mobile;

        var (json, error) = await PostAsync("/v1/request", body, ct);
        if (json is null)
            return new GatewayRequestResult { TransportError = true, Message = error };

        using (json)
        {
            var root = json.RootElement;
            return new GatewayRequestResult
            {
                ResultCode = GetInt(root, "result") ?? -1,
                Message = GetString(root, "message"),
                TrackId = GetLong(root, "trackId"),
                RawData = root.GetRawText()
            };
        }
    }

    public Task<GatewayTransactionInfo> InquiryAsync(long trackId, CancellationToken ct = default) =>
        PostForInfoAsync("/v1/inquiry", trackId, ct);

    public Task<GatewayTransactionInfo> VerifyAsync(long trackId, CancellationToken ct = default) =>
        PostForInfoAsync("/v1/verify", trackId, ct);

    private async Task<GatewayTransactionInfo> PostForInfoAsync(string path, long trackId, CancellationToken ct)
    {
        var (json, error) = await PostAsync(path, new Dictionary<string, object>
        {
            ["merchant"] = _options.Merchant,
            ["trackId"] = trackId
        }, ct);

        if (json is null)
            return new GatewayTransactionInfo { TransportError = true, Message = error };

        using (json)
        {
            var root = json.RootElement;
            return new GatewayTransactionInfo
            {
                ResultCode = GetInt(root, "result") ?? -1,
                Message = GetString(root, "message"),
                Status = GetInt(root, "status"),
                Amount = GetLong(root, "amount"),
                OrderId = GetString(root, "orderId"),
                RefNumber = GetLong(root, "refNumber"),
                CardNumber = GetString(root, "cardNumber"),
                PaidAt = GetDate(root, "paidAt"),
                VerifiedAt = GetDate(root, "verifiedAt"),
                RawData = root.GetRawText()
            };
        }
    }

    private async Task<(JsonDocument? Json, string? Error)> PostAsync(string path, Dictionary<string, object> body, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(nameof(ZibalGateway));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds)));

            var uri = new Uri($"{_options.BaseAddress.TrimEnd('/')}{path}");
            using var response = await client.PostAsJsonAsync(uri, body, timeout.Token);
            var text = await response.Content.ReadAsStringAsync(timeout.Token);

            // Zibal answers with a JSON body (result/message) even for logical errors.
            try
            {
                return (JsonDocument.Parse(text), null);
            }
            catch (JsonException)
            {
                _logger.LogWarning("Zibal {Path} returned a non-JSON answer (HTTP {Status}).", path, (int)response.StatusCode);
                return (null, $"پاسخ نامعتبر از درگاه (HTTP {(int)response.StatusCode})");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            _logger.LogWarning("Zibal {Path} call failed: {Error}", path, ex.GetType().Name);
            return (null, "ارتباط با درگاه برقرار نشد.");
        }
    }

    // ---- lenient JSON helpers (Zibal returns numbers sometimes as strings) ----

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? (p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString())
            : null;

    private static long? GetLong(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p)) return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var n)) return n;
        if (p.ValueKind == JsonValueKind.String && long.TryParse(p.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)) return s;
        return null;
    }

    private static int? GetInt(JsonElement e, string name)
    {
        var v = GetLong(e, name);
        return v.HasValue && v.Value is >= int.MinValue and <= int.MaxValue ? (int)v.Value : null;
    }

    private static DateTime? GetDate(JsonElement e, string name)
    {
        var s = GetString(e, name);
        return s is not null && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }
}
