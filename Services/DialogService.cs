namespace UltimateVideoBrowser.Services;

public sealed class DialogService : IDialogService
{
    public Task DisplayAlertAsync(string title, string message, string cancel)
    {
        var page = GetPage();
        return page == null ? Task.CompletedTask : page.DisplayAlert(title, message, cancel);
    }

    public Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel)
    {
        var page = GetPage();
        return page == null ? Task.FromResult(false) : page.DisplayAlert(title, message, accept, cancel);
    }

    public Task<string?> DisplayPromptAsync(
        string title,
        string message,
        string accept,
        string cancel,
        string? placeholder,
        int maxLength,
        Keyboard keyboard)
    {
        var page = GetPage();
        return page == null
            ? Task.FromResult<string?>(null)
            : page.DisplayPromptAsync(title, message, accept, cancel, placeholder, maxLength, keyboard);
    }

    private static Page? GetPage()
    {
        return Shell.Current ?? Application.Current?.MainPage;
    }
}