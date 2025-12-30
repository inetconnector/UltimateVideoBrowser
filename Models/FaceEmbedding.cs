using SQLite;

namespace UltimateVideoBrowser.Models;

public sealed class FaceEmbedding
{
    [PrimaryKey] [AutoIncrement] public int Id { get; set; }

    [Indexed] public string MediaPath { get; set; } = string.Empty;

    [Indexed] public string? PersonId { get; set; }

    public int FaceIndex { get; set; }

    public float Score { get; set; }

    public byte[] Embedding { get; set; } = Array.Empty<byte>();
}