namespace UltimateVideoBrowser.Services;

public interface IFolderPickerService
{
    Task<FolderPickResult?> PickFolderAsync(CancellationToken ct = default);
}

public sealed record FolderPickResult(string Path, string DisplayName);
