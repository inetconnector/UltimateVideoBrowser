namespace UltimateVideoBrowser.Services;

public interface IDialogService
{
    Task DisplayAlertAsync(string title, string message, string cancel);
    Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel);

    Task<string?> DisplayPromptAsync(
        string title,
        string message,
        string accept,
        string cancel,
        string? placeholder,
        int maxLength,
        Keyboard keyboard,
        string? initialValue = null);
}
