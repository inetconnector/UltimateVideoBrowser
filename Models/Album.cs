using SQLite;

namespace UltimateVideoBrowser.Models;

public class Album
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;

    [Indexed] public string Name { get; set; } = string.Empty;

    public long CreatedUtcSeconds { get; set; }
}