using UltimateVideoBrowser.Resources.Strings;

namespace UltimateVideoBrowser.Views;

public partial class LegalConsentPage : ContentPage
{
    private readonly TaskCompletionSource<bool> resultSource = new();
    private CancellationTokenRegistration cancellationTokenRegistration;

    public LegalConsentPage(string priceText)
    {
        InitializeComponent();
        PriceLabel.Text = string.Format(AppResources.LegalConsentPriceFormat, priceText);
    }

    public async Task<bool> ShowAsync(INavigation navigation, CancellationToken ct)
    {
        cancellationTokenRegistration = ct.Register(() => resultSource.TrySetResult(false));
        await navigation.PushModalAsync(new NavigationPage(this), false);
        return await resultSource.Task.ConfigureAwait(false);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (!resultSource.Task.IsCompleted)
            resultSource.TrySetResult(false);
        cancellationTokenRegistration.Dispose();
    }

    private async void OnConfirmClicked(object sender, EventArgs e)
    {
        if (WithdrawalConsentCheckBox.IsChecked != true)
        {
            await DisplayAlert(
                AppResources.LegalConsentMissingTitle,
                AppResources.LegalConsentMissingMessage,
                AppResources.OkButton);
            return;
        }

        await CloseAsync(true);
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        await CloseAsync(false);
    }

    private Task CloseAsync(bool accepted)
    {
        if (resultSource.Task.IsCompleted)
            return Task.CompletedTask;

        resultSource.TrySetResult(accepted);
        return Navigation.PopModalAsync(false);
    }

    private Task NavigateToDocumentAsync(string title, string body)
    {
        return Navigation.PushAsync(new LegalDocumentPage(title, body), false);
    }

    private async void OnImprintClicked(object sender, EventArgs e)
    {
        await NavigateToDocumentAsync(AppResources.LegalImprintTitle, AppResources.LegalImprintBody);
    }

    private async void OnPrivacyClicked(object sender, EventArgs e)
    {
        await NavigateToDocumentAsync(AppResources.LegalPrivacyTitle, AppResources.LegalPrivacyBody);
    }

    private async void OnTermsClicked(object sender, EventArgs e)
    {
        await NavigateToDocumentAsync(AppResources.LegalTermsTitle, AppResources.LegalTermsBody);
    }

    private async void OnWithdrawalClicked(object sender, EventArgs e)
    {
        await NavigateToDocumentAsync(AppResources.LegalWithdrawalTitle, AppResources.LegalWithdrawalBody);
    }
}
