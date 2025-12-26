namespace UltimateVideoBrowser.Services;

public sealed record IndexProgress(int Processed, int Total, int Inserted, string SourceName, string? CurrentPath)
{
    public double Ratio => Total == 0 ? 0 : (double)Processed / Total;
}
