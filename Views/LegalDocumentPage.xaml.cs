namespace UltimateVideoBrowser.Views;

public partial class LegalDocumentPage : ContentPage
{
    public LegalDocumentPage(string title, string body)
    {
        InitializeComponent();
        Title = title;
        DocumentTitleLabel.Text = title;
        DocumentBodyLabel.Text = body;
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        BackButton.IsEnabled = false;
        await Task.Yield();

        if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync(false);
        else
            await Navigation.PopModalAsync(false);
    }
}