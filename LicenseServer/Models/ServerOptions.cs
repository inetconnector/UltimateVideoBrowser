namespace UltimateVideoBrowser.LicenseServer.Models;

public sealed class LicenseOptions
{
    public string ProductId { get; init; } = "ultimatevideobrowser_pro";
    public string ProductName { get; init; } = "UltimateVideoBrowser Pro";
    public PriceOptions Price { get; init; } = new();
    public int MaxDevices { get; init; } = 1;
    public int ActivationDays { get; init; } = 30;
    public string HmacSecret { get; init; } = string.Empty;
}

public sealed class PriceOptions
{
    public string Value { get; init; } = "3.92";
    public string Currency { get; init; } = "EUR";
}

public sealed class PayPalOptions
{
    public string CheckoutBaseUrl { get; init; } = "https://www.paypal.com/cgi-bin/webscr";
    public string BusinessEmail { get; init; } = "sales@netregservice.com";
    public string ItemName { get; init; } = "Ultimate Video Browser Pro";
}

public sealed class DataStorageOptions
{
    public string BasePath { get; init; } = "Data";
}

public sealed class LegalOptions
{
    public string ProviderName { get; init; } = "Digitaxo.com";
    public string Address { get; init; } = "Daniel Frede\nBismarckstrasse 6\n97209 Veitshöchheim\nDeutschland";
    public string Email { get; init; } = "sales@digitaxo.com";
    public string VatId { get; init; } = "DE234497343";
    public string ResponsiblePerson { get; init; } = "Daniel Frede\nBismarckstrasse 6\n97209 Veitshöchheim\nDeutschland";
    public string SupportEmail { get; init; } = "sales@digitaxo.com";
}

public sealed class OptionsFilePathOptions
{
    public string? OptionsFilePath { get; init; }
}
