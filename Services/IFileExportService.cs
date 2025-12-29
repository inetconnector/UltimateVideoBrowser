using UltimateVideoBrowser.Models;

namespace UltimateVideoBrowser.Services;

public interface IFileExportService
{
    Task SaveAsAsync(MediaItem item);
    Task CopyToFolderAsync(IEnumerable<MediaItem> items);
    Task<IReadOnlyList<MediaItem>> MoveToFolderAsync(IEnumerable<MediaItem> items);
    Task<IReadOnlyList<MediaItem>> DeletePermanentlyAsync(IEnumerable<MediaItem> items);
}
