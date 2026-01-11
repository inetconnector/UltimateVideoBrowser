namespace UltimateVideoBrowser.Services;

public sealed class DialogService : IDialogService
{
    public Task DisplayAlertAsync(string title, string message, string cancel)
    {
        var page = GetPage();
        return page == null
            ? Task.CompletedTask
            : MainThread.InvokeOnMainThreadAsync(() => page.DisplayAlertAsync(title, message, cancel));
    }

    public Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel)
    {
        var page = GetPage();
        return page == null
            ? Task.FromResult(false)
            : MainThread.InvokeOnMainThreadAsync(() => page.DisplayAlertAsync(title, message, accept, cancel));
    }

    public Task<string?> DisplayPromptAsync(
        string title,
        string message,
        string accept,
        string cancel,
        string? placeholder,
        int maxLength,
        Keyboard keyboard,
        string? initialValue = null)
    {
        var page = GetPage();
        return page == null
            ? Task.FromResult<string?>(null)
            : MainThread.InvokeOnMainThreadAsync(() =>
                page.DisplayPromptAsync(title, message, accept, cancel, placeholder, maxLength, keyboard,
                    initialValue));
    }

    public Task<string?> DisplayActionSheetAsync(
        string title,
        string cancel,
        string? destruction,
        params string[] buttons)
    {
        var page = GetPage();
        if (page == null)
            return Task.FromResult<string?>(null);

        return MainThread.InvokeOnMainThreadAsync(() =>
            page.DisplayActionSheetAsync(title, cancel, destruction, buttons));
    }

    private static Page? GetPage()
    {
        var app = Application.Current;
        if (app == null)
            return null;

        return app.Windows.FirstOrDefault()?.Page;
    }
}