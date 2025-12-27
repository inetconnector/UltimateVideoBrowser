using System.Linq;

namespace UltimateVideoBrowser.Services;

public sealed class DialogService : IDialogService
{
    public Task DisplayAlertAsync(string title, string message, string cancel)
    {
        var page = GetPage();
        return page == null ? Task.CompletedTask : page.DisplayAlertAsync(title, message, cancel);
    }

    public Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel)
    {
        var page = GetPage();
        return page == null ? Task.FromResult(false) : page.DisplayAlertAsync(title, message, accept, cancel);
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
            : page.DisplayPromptAsync(title, message, accept, cancel, placeholder, maxLength, keyboard, initialValue);
    }

    private static Page? GetPage()
    {
        return Shell.Current ?? Application.Current?.Windows.FirstOrDefault()?.Page;
    }
}
