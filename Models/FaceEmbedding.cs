using SQLite;

namespace UltimateVideoBrowser.Models;

public sealed class FaceEmbedding
{
    [PrimaryKey] [AutoIncrement] public int Id { get; set; }

    [Indexed] public string MediaPath { get; set; } = string.Empty;

    [Indexed] public string? PersonId { get; set; }

    public int FaceIndex { get; set; }

    public float Score { get; set; }

    // Face bounding box in pixels on the auto-oriented image.
    // These are used to render Picasa-like face crops in the UI.
    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; }
    public float H { get; set; }

    // The dimensions of the auto-oriented image at the time of detection.
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public byte[] Embedding { get; set; } = Array.Empty<byte>();

    // Model keys used to create this row. These are used to skip work when models haven't changed.
    public string? DetectionModelKey { get; set; }
    public string? EmbeddingModelKey { get; set; }

    // Quality score (0..1). Used for sorting and selecting the best cover face.
    public float FaceQuality { get; set; }

    public string? Thumb96Path { get; set; }
}