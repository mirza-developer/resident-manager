namespace ResidentialComplex.Infrastructure.Settings;

/// <summary>
/// Configuration settings for the SMS provider.
/// </summary>
public class SmsOptions
{
    public const string SectionName = "Sms";

    public string ApiBaseAddress { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ProviderPhone { get; set; } = string.Empty;
}
