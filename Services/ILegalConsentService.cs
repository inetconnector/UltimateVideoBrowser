namespace UltimateVideoBrowser.Services;

public interface ILegalConsentService
{
    Task<bool> RequestProPurchaseConsentAsync(string priceText, CancellationToken ct);
}
