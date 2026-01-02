using System.Net.Http.Json;

namespace UltimateVideoBrowser.Services;

public sealed class LicenseServerClient
{
    private readonly HttpClient httpClient;
    private readonly AppSettingsService settingsService;

    public LicenseServerClient(HttpClient httpClient, AppSettingsService settingsService)
    {
        this.httpClient = httpClient;
        this.settingsService = settingsService;
    }

    public async Task<PricingResponse?> GetPricingAsync(CancellationToken ct)
    {
        return await httpClient.GetFromJsonAsync<PricingResponse>(BuildUri("/api/pricing"), ct).ConfigureAwait(false);
    }

    public async Task<CheckoutResponse?> CreateCheckoutAsync(CancellationToken ct)
    {
        using var response = await httpClient.PostAsync(BuildUri("/api/checkout"), null, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<CheckoutResponse>(cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<ActivationResponse?> ActivateAsync(ActivateRequest request, CancellationToken ct)
    {
        using var response = await httpClient.PostAsJsonAsync(BuildUri("/api/licenses/activate"), request, ct)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<ActivationResponse>(cancellationToken: ct).ConfigureAwait(false);
    }

    private Uri BuildUri(string path)
    {
        var baseUrl = settingsService.LicenseServerBaseUrl.TrimEnd('/');
        return new Uri($"{baseUrl}{path}");
    }
}

public sealed record PricingResponse(PricingInfo Price, string ProductId, string ProductName);

public sealed record PricingInfo(string Value, string Currency);

public sealed record CheckoutResponse(string CheckoutUrl, PricingInfo Price);

public sealed record ActivateRequest(string LicenseKey, string DeviceFingerprint, string Platform);

public sealed record ActivationResponse(string Status, string? ActivationToken, DateTimeOffset? ValidUntil, string[]? Features);
