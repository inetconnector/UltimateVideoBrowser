namespace UltimateVideoBrowser.Services;

public interface IFolderPickerService
{
    Task<IReadOnlyList<FolderPickResult>> PickFoldersAsync(CancellationToken ct = default);
}

public sealed record FolderPickResult(string Path, string DisplayName);