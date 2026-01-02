using SQLite;

namespace UltimateVideoBrowser.Models;

public sealed class FaceScanJob
{
    [PrimaryKey]
    public string MediaPath { get; set; } = string.Empty;

    public long EnqueuedAtSeconds { get; set; }

    public long LastAttemptSeconds { get; set; }

    public int AttemptCount { get; set; }
}
