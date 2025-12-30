namespace UltimateVideoBrowser.Services.Faces;

public sealed record DetectedFace(
    float X,
    float Y,
    float W,
    float H,
    float[] Landmarks10,
    float Score);

public sealed record FaceMatch(
    string PersonId,
    string Name,
    float Similarity,
    int FaceIndex);
