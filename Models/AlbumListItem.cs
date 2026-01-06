namespace UltimateVideoBrowser.Models;

public sealed class AlbumListItem
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int ItemCount { get; set; }

    public bool IsAll { get; set; }
}