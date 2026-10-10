using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Infrastructure.Settings;

namespace ResidentialComplex.Infrastructure.Services;

/// <summary>
/// SMS service implementation that communicates with the Melipayamak provider.
/// </summary>
public class SmsService : ISmsService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SmsOptions _options;
    private readonly ILogger<SmsService> _logger;

    public SmsService(IHttpClientFactory httpClientFactory, IOptions<SmsOptions> options, ILogger<SmsService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SendAsync(string toPhone, string text)
    {
        var client = _httpClientFactory.CreateClient(nameof(SmsService));

        var requestBody = new
        {
            from = _options.ProviderPhone,
            to = toPhone,
            text
        };

        // The Melipayamak API requires the API key as part of the URL path:
        // POST api/send/simple/{API_KEY}
        // This is the provider-mandated URL format and cannot be changed.
        SmsResponse? response = null;
        try
        {
            var httpResponse = await client.PostAsJsonAsync($"api/send/simple/{_options.ApiKey}", requestBody);

            if (!httpResponse.IsSuccessStatusCode)
            {
                var body = await httpResponse.Content.ReadAsStringAsync();
                _logger.LogWarning("SMS provider returned non-success status {StatusCode} for recipient {ToPhone}. Body: {Body}",
                    (int)httpResponse.StatusCode, toPhone, body);
                return;
            }

            response = await httpResponse.Content.ReadFromJsonAsync<SmsResponse>();

            if (response is null)
            {
                _logger.LogWarning("SMS provider returned an empty or unparseable response for recipient {ToPhone}", toPhone);
                return;
            }

            _logger.LogInformation("SMS provider response for recipient {ToPhone}: recId={RecId}, status={Status}",
                toPhone, response.RecId, response.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while sending SMS to recipient {ToPhone}", toPhone);
        }
    }

    public async Task<string?> SendOtpAsync(string toPhone)
    {
        var client = _httpClientFactory.CreateClient(nameof(SmsService));

        var requestBody = new
        {
            to = toPhone,
        };

        // The Melipayamak API requires the API key as part of the URL path:
        // POST api/send/simple/{API_KEY}
        // This is the provider-mandated URL format and cannot be changed.
        OtpResponse? response = null;
        try
        {
            var httpResponse = await client.PostAsJsonAsync($"api/send/otp/{_options.ApiKey}", requestBody);

            if (!httpResponse.IsSuccessStatusCode)
            {
                var body = await httpResponse.Content.ReadAsStringAsync();
                _logger.LogWarning("SMS provider returned non-success status {StatusCode} for recipient {ToPhone}. Body: {Body}",
                    (int)httpResponse.StatusCode, toPhone, body);
                return null;
            }

            response = await httpResponse.Content.ReadFromJsonAsync<OtpResponse>();

            if (response is null)
            {
                _logger.LogWarning("SMS provider returned an empty or unparseable response for recipient {ToPhone}", toPhone);
                return null;
            }

            _logger.LogInformation("SMS provider response for recipient {ToPhone}: code={Code}, status={Status}",
                toPhone, response.Code, response.Status);

            return response.Code;    
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while sending SMS to recipient {ToPhone}", toPhone);
            return null;
        }
    }

    private sealed class SmsResponse
    {
        [JsonPropertyName("recId")]
        public long RecId { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }
    }

    private sealed class OtpResponse
    {
        [JsonPropertyName("code")]
        public string Code { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }
    }
}
