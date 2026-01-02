namespace UltimateVideoBrowser.Services;

public sealed record IndexProgress(int Processed, int Total, int Inserted, string SourceName, string? CurrentPath)
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