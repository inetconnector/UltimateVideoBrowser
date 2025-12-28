using UltimateVideoBrowser.Models;

namespace UltimateVideoBrowser.Services;

public interface IFileExportService
{
    Task SaveAsAsync(VideoItem item);
    Task CopyToFolderAsync(IEnumerable<VideoItem> items);
    Task<IReadOnlyList<VideoItem>> MoveToFolderAsync(IEnumerable<VideoItem> items);
}
