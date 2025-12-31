using SQLite;

namespace UltimateVideoBrowser.Models;

public sealed class PersonAlias
{
    [PrimaryKey] [AutoIncrement] public int Id { get; set; }

    [Indexed] public string PersonId { get; set; } = string.Empty;

    [Indexed] public string AliasName { get; set; } = string.Empty;
}