using System.Linq;
using System.Windows.Input;
using UltimateVideoBrowser.Resources.Strings;

namespace UltimateVideoBrowser.Views.Components;

public partial class MainHeroView : ContentView
{
    private readonly MenuFlyout actionsFlyout;

    public MainHeroView()
    {
        InitializeComponent();
    }

    private async void OnActionsClicked(object sender, EventArgs e)
    {
        var page = GetPage();
        if (page == null)
            return;

        var options = BuildActionOptions();
        if (options.Count == 0)
            return;

        var selection = await MainThread.InvokeOnMainThreadAsync(() =>
            page.DisplayActionSheetAsync(
                AppResources.ActionsButton,
                AppResources.CancelButton,
                null,
                options.Select(option => option.Title).ToArray()));

        if (string.IsNullOrWhiteSpace(selection) || selection == AppResources.CancelButton)
            return;

        var selected = options.FirstOrDefault(option => option.Title == selection);
        selected?.Command.Execute(null);
    }

    private List<ActionOption> BuildActionOptions()
    {
        var options = new List<ActionOption>();
        if (BindingContext is null)
            return options;

        if (TryGetCommand("OpenSourcesCommand", out var openSources))
        {
            options.Add(new ActionOption(AppResources.SourcesButton, openSources));
        }

        if (TryGetCommand("OpenAlbumsCommand", out var openAlbums))
        {
            options.Add(new ActionOption(AppResources.AlbumsButton, openAlbums));
        }

        if (TryGetCommand("OpenSettingsCommand", out var openSettings))
        {
            options.Add(new ActionOption(AppResources.SettingsButton, openSettings));
        }

        if (!IsProUnlocked() && TryGetCommand("OpenProUpgradeCommand", out var openProUpgrade))
        {
            options.Add(new ActionOption(AppResources.SettingsProTitle, openProUpgrade));
        }

        if (TryGetCommand("RunIndexCommand", out var runIndex))
        {
            options.Add(new ActionOption(AppResources.ReindexButton, runIndex));
        }

        return options;
    }

    private bool IsProUnlocked()
    {
        if (BindingContext is null)
            return false;

        var property = BindingContext.GetType().GetProperty("IsProUnlocked");
        if (property?.GetValue(BindingContext) is bool isProUnlocked)
            return isProUnlocked;

        return false;
    }

    private bool TryGetCommand(string propertyName, out ICommand command)
    {
        command = null!;
        if (BindingContext is null)
            return false;

        var property = BindingContext.GetType().GetProperty(propertyName);
        if (property?.GetValue(BindingContext) is ICommand resolved)
        {
            command = resolved;
            return true;
        }

        return false;
    }

    private static Page? GetPage()
    {
        var app = Application.Current;
        if (app == null)
            return null;

        return app.Windows.FirstOrDefault()?.Page;
    }

    private sealed record ActionOption(string Title, ICommand Command);
}
