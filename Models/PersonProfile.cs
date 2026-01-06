using SQLite;

namespace UltimateVideoBrowser.Models;

public sealed class PersonProfile
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [Indexed] public string Name { get; set; } = "Unknown";

    [Indexed] public string? MergedIntoPersonId { get; set; }

    public int? PrimaryFaceEmbeddingId { get; set; }

    public float QualityScore { get; set; }

    public bool IsIgnored { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}