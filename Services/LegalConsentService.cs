using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using UltimateVideoBrowser.Views;

namespace UltimateVideoBrowser.Services;

public sealed class LegalConsentService : ILegalConsentService
{
    public Task<bool> RequestProPurchaseConsentAsync(string priceText, CancellationToken ct)
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var navigation = Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
            if (navigation == null)
                return false;

            var page = new LegalConsentPage(priceText);
            return await page.ShowAsync(navigation, ct).ConfigureAwait(false);
        });
    }
}
