using UltimateVideoBrowser.Models;

namespace UltimateVideoBrowser.Services;

public interface ISourceService
{
    Task<List<MediaSource>> GetSourcesAsync();
    Task EnsureDefaultSourceAsync();
    Task UpsertAsync(MediaSource src);
    Task DeleteAsync(MediaSource src);
}
