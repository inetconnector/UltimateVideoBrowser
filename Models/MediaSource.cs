using SQLite;

namespace UltimateVideoBrowser.Models;

public class MediaSource
{
    [PrimaryKey]
    public string Id { get; set; } = "";

    [Indexed]
    public string DisplayName { get; set; } = "";

    public string LocalFolderPath { get; set; } = "";

    public bool IsEnabled { get; set; } = true;

    public long LastIndexedUtcSeconds { get; set; } = 0;
}
