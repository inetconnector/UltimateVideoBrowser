using SQLite;

namespace UltimateVideoBrowser.Models;

public class VideoItem
{
    [PrimaryKey] public string Path { get; set; } = "";

    [Indexed] public string Name { get; set; } = "";

    public long DurationMs { get; set; }
    public long DateAddedSeconds { get; set; }

    [Indexed] public string? SourceId { get; set; }

    public string? ThumbnailPath { get; set; }

    [Ignore]
    public string DurationText => DurationMs <= 0 ? "" : TimeSpan.FromMilliseconds(DurationMs).ToString(@"hh\:mm\:ss");

    [Ignore]
    public string FirstLetter
        => string.IsNullOrWhiteSpace(Name) ? "#" : Name.Trim().Substring(0, 1).ToUpperInvariant();
}