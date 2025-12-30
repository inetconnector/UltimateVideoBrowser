using SQLite;

namespace UltimateVideoBrowser.Models;

public sealed class PersonTag
{
    [PrimaryKey] [AutoIncrement] public int Id { get; set; }

    [Indexed] public string MediaPath { get; set; } = string.Empty;

    [Indexed] public string PersonName { get; set; } = string.Empty;
}