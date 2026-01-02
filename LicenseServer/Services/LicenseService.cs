using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UltimateVideoBrowser.LicenseServer.Models;

namespace UltimateVideoBrowser.LicenseServer.Services;

public sealed class LicenseService
{
    private readonly LicenseOptions licenseOptions;
    private readonly PayPalOptions payPalOptions;

    public LicenseService(IConfiguration configuration)
    {
        licenseOptions = OptionsLoader.LoadOptions<LicenseOptions>(configuration, "License", "LicenseFile");
        payPalOptions = OptionsLoader.LoadOptions<PayPalOptions>(configuration, "PayPal", "PayPalFile");
    }

    public PricingResponse GetPricing()
    {
        return new PricingResponse(
            new PricingInfo(licenseOptions.Price.Value, licenseOptions.Price.Currency),
            licenseOptions.ProductId,
            licenseOptions.ProductName);
    }

    public CheckoutResponse CreateCheckout()
    {
        return new CheckoutResponse(BuildPayPalCheckoutUrl(),
            new PricingInfo(licenseOptions.Price.Value, licenseOptions.Price.Currency));
    }

    public string BuildPayPalCheckoutUrl()
    {
        var itemName = Uri.EscapeDataString(payPalOptions.ItemName);
        var business = Uri.EscapeDataString(payPalOptions.BusinessEmail);
        var amount = Uri.EscapeDataString(licenseOptions.Price.Value);
        var currency = Uri.EscapeDataString(licenseOptions.Price.Currency);
        return $"{payPalOptions.CheckoutBaseUrl}?cmd=_xclick&business={business}&item_name={itemName}&currency_code={currency}&amount={amount}";
    }

    public SignedLicense IssueLicense(string platform, string deviceFingerprint, DateTimeOffset issuedAt, DateTimeOffset? expiresAt)
    {
        var payload = new LicenseKeyPayload
        {
            LicenseId = Guid.NewGuid().ToString("N"),
            Product = licenseOptions.ProductId,
            Platform = platform,
            DeviceFingerprint = deviceFingerprint,
            IssuedAt = issuedAt.ToUnixTimeSeconds(),
            ExpiresAt = expiresAt?.ToUnixTimeSeconds()
        };

        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var signature = Sign(payloadJson);
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson));
        var licenseKey = $"{token}.{signature}";
        return new SignedLicense { LicenseKey = licenseKey, Payload = payload };
    }

    public bool TryValidateLicense(string licenseKey, out LicenseKeyPayload? payload)
    {
        payload = null;
        var parts = licenseKey.Split('.', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return false;

        string json;
        try
        {
            json = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0]));
        }
        catch
        {
            return false;
        }

        var expectedSignature = Sign(json);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expectedSignature), Encoding.UTF8.GetBytes(parts[1])))
            return false;

        payload = JsonSerializer.Deserialize<LicenseKeyPayload>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return payload != null;
    }

    public string CreateActivationToken(ActivationTokenPayload payload)
    {
        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var signature = Sign(payloadJson);
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson));
        return $"{token}.{signature}";
    }

    public string HashEmail(string? email, string salt)
    {
        if (string.IsNullOrWhiteSpace(email))
            return string.Empty;

        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes($"{email.Trim().ToLowerInvariant()}:{salt}");
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string Sign(string payloadJson)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(licenseOptions.HmacSecret));
        var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadJson));
        return Convert.ToBase64String(signature);
    }
}
