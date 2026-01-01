using SQLite;

namespace UltimateVideoBrowser.Models;

public class AlbumItem
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed(Name = "IX_AlbumItem_AlbumMedia", Unique = true)]
    public string AlbumId { get; set; } = string.Empty;

    [Indexed(Name = "IX_AlbumItem_AlbumMedia", Unique = true)]
    public string MediaPath { get; set; } = string.Empty;
}
