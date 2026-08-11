namespace ResidentialComplex.Application.Interfaces;

/// <summary>
/// Service for sending SMS messages through the configured provider.
/// </summary>
public interface ISmsService
{
    /// <summary>
    /// Sends an SMS message to the specified phone number.
    /// </summary>
    /// <param name="toPhone">Recipient phone number.</param>
    /// <param name="text">Message text.</param>
    Task SendAsync(string toPhone, string text);
}
