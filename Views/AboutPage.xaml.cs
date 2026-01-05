using UltimateVideoBrowser.Resources.Strings;
using UltimateVideoBrowser.Services;

namespace UltimateVideoBrowser.Views;

public partial class AboutPage : ContentPage
{
    public AboutPage(IProUpgradeService proUpgradeService)
    {
        InitializeComponent();

        AppNameLabel.Text = AppInfo.Name;
        AppVersionLabel.Text = string.Format(AppResources.AboutVersionFormat, AppInfo.VersionString);
        var status = proUpgradeService.IsProUnlocked
            ? AppResources.SettingsProStatusUnlockedTitle
            : AppResources.SettingsProStatusFreeTitle;
        LicenseStatusLabel.Text = string.Format(AppResources.AboutLicenseStatusFormat, status);
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        BackButton.IsEnabled = false;
        await Task.Yield();

        if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync(false);
    }

    private async void OnLicensesClicked(object sender, EventArgs e)
    {
        var body = await LegalDocumentLoader.LoadLicenseInfoAsync();
        await Navigation.PushAsync(new LegalDocumentPage(AppResources.LicenseInfoTitle, body), false);
    }
}
