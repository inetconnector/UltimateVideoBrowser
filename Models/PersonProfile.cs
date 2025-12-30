using SQLite;

namespace UltimateVideoBrowser.Models;

public sealed class PersonProfile
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [Indexed] public string Name { get; set; } = "Unknown";

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}