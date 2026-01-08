namespace UltimateVideoBrowser.Services;

public sealed record IndexProgress(
    int Processed,
    int Total,
    int Inserted,
    string SourceName,
    string? CurrentPath,
    int ThumbsQueued,
    int ThumbsDone,
    int LocationsQueued,
    int LocationsDone,
    int DurationsQueued,
    int DurationsDone)
{
    public double Ratio
    {
        get
        {
            var safeTotal = Math.Max(Total, Processed);
            if (safeTotal <= 0)
                return 0;

            var ratio = (double)Processed / safeTotal;
            return Math.Clamp(ratio, 0, 1);
        }
    }
}
