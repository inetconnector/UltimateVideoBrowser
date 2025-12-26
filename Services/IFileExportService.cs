using UltimateVideoBrowser.Models;

namespace UltimateVideoBrowser.Services;

public interface IFileExportService
{
    Task SaveAsAsync(VideoItem item);
}
