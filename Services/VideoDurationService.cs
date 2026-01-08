#if WINDOWS
using Windows.Storage;
#endif

namespace UltimateVideoBrowser.Services;

/// <summary>
///     Retrieves video duration without blocking the main indexing/scanning loop.
/// </summary>
public sealed class VideoDurationService
{
    public async Task<long> TryGetDurationMsAsync(string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
            return 0;

#if WINDOWS
        try
        {
            ct.ThrowIfCancellationRequested();
            var file = await StorageFile.GetFileFromPathAsync(path);
            var props = await file.Properties.GetVideoPropertiesAsync();
            return (long)props.Duration.TotalMilliseconds;
        }
        catch
        {
            return 0;
        }
#else
        await Task.CompletedTask;
        return 0;
#endif
    }
}
