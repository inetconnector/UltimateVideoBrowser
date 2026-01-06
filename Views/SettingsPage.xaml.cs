using UltimateVideoBrowser.Resources.Strings;
using UltimateVideoBrowser.Services;
using UltimateVideoBrowser.ViewModels;

namespace UltimateVideoBrowser.Views;

public partial class SettingsPage : ContentPage
{
    private readonly IServiceProvider serviceProvider;

    public SettingsPage(SettingsViewModel vm, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        BindingContext = vm;
        this.serviceProvider = serviceProvider;
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        // Make the UI react instantly: disable the button immediately, then yield one frame.
        // Any longer work (refreshing queries, indexing, etc.) is handled asynchronously when the MainPage appears.
        BackButton.IsEnabled = false;
        await Task.Yield();

        try
        {
            if (Navigation.NavigationStack.Count > 1)
            {
                await Navigation.PopAsync(false);
                return;
            }

            if (Navigation.ModalStack.Count > 0)
            {
                await Navigation.PopModalAsync(false);
                return;
            }

            if (Shell.Current != null)
                await Shell.Current.GoToAsync("..", false);
        }
        finally
        {
            BackButton.IsEnabled = true;
        }
    }

    private Task NavigateToDocumentAsync(string title, string body)
    {
        return Navigation.PushAsync(new LegalDocumentPage(title, body), false);
    }

    private async void OnImprintClicked(object sender, EventArgs e)
    {
        var body = await LegalDocumentLoader.LoadImprintAsync();
        await NavigateToDocumentAsync(AppResources.LegalImprintTitle, body);
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

    private async void OnAboutClicked(object sender, EventArgs e)
    {
        var target = serviceProvider.GetService<AboutPage>()
                     ?? ActivatorUtilities.CreateInstance<AboutPage>(serviceProvider);
        await Navigation.PushAsync(target, false);
    }
}