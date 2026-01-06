using System.Text.Json.Serialization;

namespace UltimateVideoBrowser.LicenseServer.Models;

public sealed record PricingInfo(string Value, string Currency);

public sealed record CheckoutResponse(string CheckoutUrl, PricingInfo Price);

public sealed record PricingResponse(PricingInfo Price, string ProductId, string ProductName);

public sealed record ActivateRequest(string LicenseKey, string DeviceFingerprint, string Platform);

public sealed record ActivationResponse(
    string Status,
    string? ActivationToken,
    DateTimeOffset? ValidUntil,
    string[]? Features);

public sealed record PayPalWebhookRequest(
    string OrderId,
    string PaymentStatus,
    PricingInfo Amount,
    string? PayerId,
    string? Email,
    string? Platform);

public sealed record PurchaseRecord
{
    public required string OrderId { get; init; }
    public required string PaymentProvider { get; init; }
    public required string PaymentStatus { get; init; }
    public required PricingInfo Amount { get; init; }
    public required ProductInfo Product { get; init; }
    public required BuyerInfo Buyer { get; init; }
    public required LicenseInfo License { get; init; }
}

public sealed record ProductInfo(string Id, string Name, string Version);

public sealed record BuyerInfo(string? PayPalPayerId, string? EmailHash);

public sealed record LicenseInfo
{
    public required string LicenseId { get; init; }
    public required string Type { get; init; }
    public required string[] Platforms { get; init; }
    public required int MaxDevices { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed record LicenseRecord
{
    public required string LicenseId { get; init; }
    public required string ProductId { get; init; }
    public required string ProductName { get; init; }
    public required int MaxDevices { get; init; }
    public required string[] Platforms { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? DeviceFingerprint { get; set; }
}

public sealed record SignedLicense
{
    public required string LicenseKey { get; init; }
    public required LicenseKeyPayload Payload { get; init; }
}

public sealed record LicenseKeyPayload
{
    [JsonPropertyName("licenseId")] public required string LicenseId { get; init; }

    [JsonPropertyName("product")] public required string Product { get; init; }

    [JsonPropertyName("platform")] public required string Platform { get; init; }

    [JsonPropertyName("deviceFingerprint")]
    public required string DeviceFingerprint { get; init; }

    [JsonPropertyName("issuedAt")] public required long IssuedAt { get; init; }

    [JsonPropertyName("expiresAt")] public long? ExpiresAt { get; init; }
}

public sealed record ActivationTokenPayload
{
    [JsonPropertyName("licenseId")] public required string LicenseId { get; init; }

    [JsonPropertyName("deviceFingerprint")]
    public required string DeviceFingerprint { get; init; }

    [JsonPropertyName("issuedAt")] public required long IssuedAt { get; init; }

    [JsonPropertyName("expiresAt")] public required long ExpiresAt { get; init; }
}